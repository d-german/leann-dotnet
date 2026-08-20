using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using CSharpFunctionalExtensions;
using LeannMcp.Models;
using LeannMcp.Services;

namespace LeannMcp.Cli;

/// <summary>
/// Localhost HTTP front end over one resident <see cref="IndexManager"/>.
/// <para/>
/// The value here is process lifetime, not the protocol: <see cref="IndexManager"/>
/// caches each loaded index together with its ONNX session, so a long-lived host
/// answers every search after the first without paying model load again. That is
/// the same property the MCP server has, made available without an MCP client.
/// <para/>
/// Built on <see cref="HttpListener"/> rather than Kestrel on purpose: it lives in
/// the BCL, so packaging stays a plain console tool with no FrameworkReference on
/// Microsoft.AspNetCore.App (which would force an ASP.NET Core runtime onto every
/// install). Binding localhost needs no elevation and no netsh URL reservation.
/// </summary>
internal sealed class SearchDaemon(IndexManager indexManager, string indexesDir)
{
    public const int DefaultPort = 57391;

    /// <summary>
    /// Serializes calls into the embedding session. Health and list stay outside the
    /// gate so a liveness probe never blocks behind an in-flight search.
    /// </summary>
    private readonly SemaphoreSlim _searchGate = new(1, 1);

    private readonly TaskCompletionSource _shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private DateTimeOffset? _warmedAt;
    private int _searchCount;

    public async Task<int> RunAsync(int port, bool warmOnStart, CancellationToken cancellationToken)
    {
        var prefix = $"http://localhost:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            Console.Error.WriteLine($"ERROR: could not listen on {prefix}: {ex.Message}");
            Console.Error.WriteLine("Another daemon may already own this port. Probe /health, or pick another --port.");
            return 1;
        }

        Console.Error.WriteLine($"LEANN search daemon listening on {prefix}");
        Console.Error.WriteLine($"  Indexes: {indexesDir}");
        Console.Error.WriteLine($"  PID:     {Environment.ProcessId}");
        Console.Error.WriteLine("  Routes:  /health /list /search /warmup /shutdown");

        if (warmOnStart) Warm();

        await using var registration = cancellationToken.Register(() => _shutdown.TrySetResult());

        while (!_shutdown.Task.IsCompleted)
        {
            var contextTask = listener.GetContextAsync();
            var winner = await Task.WhenAny(contextTask, _shutdown.Task);
            if (winner != contextTask) break;

            HttpListenerContext context;
            try
            {
                context = await contextTask;
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => HandleAsync(context), CancellationToken.None);
        }

        Console.Error.WriteLine("Shutting down.");
        listener.Close();
        return 0;
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath.TrimEnd('/');
            if (string.IsNullOrEmpty(path)) path = "/";

            switch (path)
            {
                case "/":
                case "/health":
                    WriteJson(context, 200, HealthJson());
                    break;

                case "/list":
                    HandleList(context);
                    break;

                case "/warmup":
                    await HandleWarmupAsync(context);
                    break;

                case "/search":
                    await HandleSearchAsync(context);
                    break;

                case "/shutdown":
                    WriteJson(context, 200, JsonSerializer.Serialize(new { status = "stopping" }));
                    _shutdown.TrySetResult();
                    break;

                default:
                    WriteJson(context, 404, ErrorJson(
                        $"No route '{path}'. Available: /health, /list, /search, /warmup, /shutdown."));
                    break;
            }
        }
        catch (Exception ex)
        {
            // Never let one bad request take the daemon down.
            try { WriteJson(context, 500, ErrorJson(ex.Message)); }
            catch { /* client disconnected */ }
        }
    }

    private void HandleList(HttpListenerContext context)
    {
        var result = indexManager.ListIndexes();
        if (result.IsFailure)
        {
            WriteJson(context, 500, ErrorJson(result.Error));
            return;
        }

        if (WantsJson(context))
        {
            WriteJson(context, 200, JsonSerializer.Serialize(new { indexesDir, indexes = result.Value }));
            return;
        }

        WriteText(context, 200, result.Value.Count == 0
            ? $"No indexes found in {indexesDir}"
            : string.Join(Environment.NewLine, result.Value));
    }

    private async Task HandleWarmupAsync(HttpListenerContext context)
    {
        await _searchGate.WaitAsync();
        Result<string> result;
        try { result = indexManager.Warmup(); }
        finally { _searchGate.Release(); }

        if (result.IsFailure)
        {
            WriteJson(context, 500, ErrorJson(result.Error));
            return;
        }

        _warmedAt = DateTimeOffset.Now;
        WriteText(context, 200, result.Value);
    }

    private async Task HandleSearchAsync(HttpListenerContext context)
    {
        var parameters = context.Request.QueryString;
        var indexName = parameters["index"] ?? parameters["index_name"];
        var searchText = parameters["query"] ?? parameters["q"];

        if (string.IsNullOrWhiteSpace(indexName) || string.IsNullOrWhiteSpace(searchText))
        {
            WriteJson(context, 400, ErrorJson(
                "Required query parameters: index, query."));
            return;
        }

        var topK = ParseInt(parameters["top_k"], 5);
        var complexity = ParseInt(parameters["complexity"], 32);
        var dedupThreshold = ParseDouble(parameters["dedup_threshold"], 0.95);
        // File paths are the most actionable part of a hit for a calling agent,
        // so metadata is on by default here (the MCP tool defaults it off).
        var showMetadata = ParseBool(parameters["show_metadata"], true);

        var stopwatch = Stopwatch.StartNew();
        await _searchGate.WaitAsync();
        Result<IReadOnlyList<SearchResult>> result;
        try { result = indexManager.Search(indexName, searchText, topK, complexity, dedupThreshold); }
        finally { _searchGate.Release(); }
        stopwatch.Stop();

        if (result.IsFailure)
        {
            WriteJson(context, 400, ErrorJson(result.Error));
            return;
        }

        Interlocked.Increment(ref _searchCount);
        _warmedAt ??= DateTimeOffset.Now;
        Console.Error.WriteLine(
            $"search index={indexName} top_k={topK} hits={result.Value.Count} in {stopwatch.ElapsedMilliseconds}ms");

        if (WantsJson(context))
            WriteJson(context, 200, SearchResultFormatter.ToJson(searchText, result.Value, showMetadata));
        else
            WriteText(context, 200, SearchResultFormatter.ToText(searchText, result.Value, showMetadata));
    }

    private void Warm()
    {
        Console.Error.WriteLine("Warming embedding model...");
        var result = indexManager.Warmup();
        if (result.IsSuccess)
        {
            _warmedAt = DateTimeOffset.Now;
            Console.Error.WriteLine(result.Value);
            return;
        }

        Console.Error.WriteLine($"Warmup skipped: {result.Error}");
    }

    private string HealthJson() =>
        JsonSerializer.Serialize(new
        {
            status = "ok",
            indexesDir,
            warm = _warmedAt is not null,
            warmedAt = _warmedAt?.ToString("o"),
            searches = Volatile.Read(ref _searchCount),
            pid = Environment.ProcessId,
        });

    private static bool WantsJson(HttpListenerContext context) =>
        string.Equals(context.Request.QueryString["format"], "json", StringComparison.OrdinalIgnoreCase);

    private static string ErrorJson(string message) => JsonSerializer.Serialize(new { error = message });

    private static int ParseInt(string? raw, int fallback) =>
        int.TryParse(raw, out var value) ? value : fallback;

    private static double ParseDouble(string? raw, double fallback) =>
        double.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static bool ParseBool(string? raw, bool fallback) =>
        bool.TryParse(raw, out var value) ? value : fallback;

    private static void WriteJson(HttpListenerContext context, int status, string body) =>
        Write(context, status, "application/json; charset=utf-8", body);

    private static void WriteText(HttpListenerContext context, int status, string body) =>
        Write(context, status, "text/plain; charset=utf-8", body);

    private static void Write(HttpListenerContext context, int status, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.Close();
    }
}
