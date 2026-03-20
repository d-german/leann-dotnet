using LeannMcp.Models;
using LeannMcp.Services;
using LeannMcp.Services.Chunking;
using LeannMcp.Services.Watching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// Route based on CLI args
if (args.Contains("--help") || args.Contains("-h"))
{
    PrintUsage();
    return 0;
}

if (args.Contains("--build-passages"))
{
    return RunBuildPassages(args);
}

if (args.Contains("--build-indexes"))
{
    return await RunBuildIndexes(args);
}

if (args.Contains("--rebuild"))
{
    Console.Error.WriteLine("=== Phase 1: Build Passages ===");
    Console.Error.WriteLine();
    var passageResult = RunBuildPassages(args);
    if (passageResult != 0) return passageResult;

    Console.Error.WriteLine();
    Console.Error.WriteLine("=== Phase 2: Build Indexes ===");
    Console.Error.WriteLine();

    var indexArgs = args.ToList();
    if (!indexArgs.Contains("--force")) indexArgs.Add("--force");
    var nameIdx = indexArgs.IndexOf("--index-name");
    if (nameIdx >= 0 && nameIdx + 1 < indexArgs.Count && !indexArgs.Contains("--index"))
    {
        indexArgs.Add("--index");
        indexArgs.Add(indexArgs[nameIdx + 1]);
    }
    return await RunBuildIndexes(indexArgs.ToArray());
}

if (args.Contains("--watch"))
{
    await RunWatch(args);
    return 0;
}

// Default: MCP server mode
await RunMcpServer(args);
return 0;
// -- MCP Server Mode --

static async Task RunMcpServer(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });

    var dataRoot = GetDataRoot();
    var modelsDir = GetModelDir(dataRoot);
    var indexesDir = Path.Combine(dataRoot, ".leann", "indexes");

    builder.Services.AddSingleton<IEmbeddingService>(sp =>
        new OnnxEmbeddingService(modelsDir, sp.GetRequiredService<ILogger<OnnxEmbeddingService>>()));
    builder.Services.AddSingleton(sp =>
        new IndexManager(sp.GetRequiredService<IEmbeddingService>(), sp.GetRequiredService<ILogger<IndexManager>>(), indexesDir));

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}

// -- Watch Mode --

static async Task RunWatch(string[] args)
{
    var intervalSeconds = ParseIntArg(args, "--interval", 300);
    var dataRoot = GetDataRoot();
    var configPath = ParseStringArg(args, "--repos-config")
                     ?? Path.Combine(dataRoot, ".leann", "repos.json");
    var indexesDir = Path.Combine(dataRoot, ".leann", "indexes");
    var modelsDir = GetModelDir(dataRoot);

    var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Trace);

    builder.Services.AddSingleton<ITextChunker, TextChunker>();
    builder.Services.AddSingleton<IFileDiscovery, FileDiscoveryService>();
    builder.Services.AddSingleton<IDocumentChunker, DocumentChunker>();
    builder.Services.AddSingleton<IPassageWriter, PassageWriter>();
    builder.Services.AddSingleton<IEmbeddingService>(sp =>
        new OnnxEmbeddingService(modelsDir, sp.GetRequiredService<ILogger<OnnxEmbeddingService>>()));
    builder.Services.AddSingleton<IndexBuilder>();

    builder.Services.AddHostedService(sp => new RepoWatcherService(
        sp.GetRequiredService<IFileDiscovery>(),
        sp.GetRequiredService<IDocumentChunker>(),
        sp.GetRequiredService<IPassageWriter>(),
        sp.GetRequiredService<IndexBuilder>(),
        sp.GetRequiredService<ILogger<RepoWatcherService>>(),
        configPath,
        intervalSeconds,
        indexesDir));

    await builder.Build().RunAsync();
}
// -- Build Passages Mode --

static int RunBuildPassages(string[] args)
{
    var force = args.Contains("--force");
    var indexName = ParseStringArg(args, "--index-name")
                    ?? Path.GetFileName(GetDataRoot())
                    ?? "default";

    var docPaths = ParseListArg(args, "--docs");
    if (docPaths.Count == 0)
    {
        Console.Error.WriteLine("ERROR: --build-passages requires --docs <path1> [<path2>...]");
        return 1;
    }

    var options = new ChunkingOptions
    {
        ChunkSize = ParseIntArg(args, "--chunk-size", 256),
        ChunkOverlap = ParseIntArg(args, "--chunk-overlap", 128),
        CodeChunkSize = ParseIntArg(args, "--code-chunk-size", 512),
        CodeChunkOverlap = ParseIntArg(args, "--code-chunk-overlap", 64),
        IncludeHidden = args.Contains("--include-hidden"),
    };

    var dataRoot = GetDataRoot();
    var indexesDir = Path.Combine(dataRoot, ".leann", "indexes");
    var indexDir = Path.Combine(indexesDir, indexName);

    var passagesPath = Path.Combine(indexDir, "documents.leann.passages.jsonl");
    if (!force && File.Exists(passagesPath))
    {
        Console.Error.WriteLine($"[{indexName}] Passages already exist, skipping (use --force to overwrite)");
        return 0;
    }

    var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Trace);

    builder.Services.AddSingleton<ITextChunker, TextChunker>();
    builder.Services.AddSingleton<IDocumentChunker, DocumentChunker>();
    builder.Services.AddSingleton<IPassageWriter, PassageWriter>();

    var host = builder.Build();

    Console.Error.WriteLine("LEANN Passage Builder");
    Console.Error.WriteLine($"  Index name: {indexName}");
    Console.Error.WriteLine($"  Doc paths:  {string.Join(", ", docPaths)}");
    Console.Error.WriteLine($"  Chunk size: {options.ChunkSize} (overlap {options.ChunkOverlap})");
    Console.Error.WriteLine($"  Code chunk: {options.CodeChunkSize} (overlap {options.CodeChunkOverlap})");
    Console.Error.WriteLine($"  Force:      {force}");
    Console.Error.WriteLine($"  Output:     {indexDir}");
    Console.Error.WriteLine();

    var fsLogger = host.Services.GetRequiredService<ILogger<FileDiscoveryService>>();
    var fileDiscovery = new FileDiscoveryService(fsLogger);
    var allDocuments = new List<SourceDocument>();
    foreach (var docPath in docPaths)
    {
        var discoverOne = fileDiscovery.DiscoverFiles(docPath, options);
        if (discoverOne.IsFailure)
        {
            Console.Error.WriteLine($"ERROR discovering {docPath}: {discoverOne.Error}");
            return 1;
        }
        allDocuments.AddRange(discoverOne.Value);
    }
    Console.Error.WriteLine($"Discovered {allDocuments.Count} files");

    var chunker = host.Services.GetRequiredService<IDocumentChunker>();
    var chunkResult = chunker.ChunkDocuments(allDocuments, options);
    if (chunkResult.IsFailure)
    {
        Console.Error.WriteLine($"ERROR: {chunkResult.Error}");
        return 1;
    }

    var passages = chunkResult.Value;
    Console.Error.WriteLine($"Created {passages.Count} passages");

    var writer = host.Services.GetRequiredService<IPassageWriter>();
    var syncRoots = docPaths.Select(Path.GetFullPath).ToList();
    var writeResult = writer.WritePassages(indexDir, indexName, passages, syncRoots);
    if (writeResult.IsFailure)
    {
        Console.Error.WriteLine($"ERROR: {writeResult.Error}");
        return 1;
    }

    Console.Error.WriteLine($"Done! Passages written to {indexDir}");
    return 0;
}

// -- Build Indexes Mode --

static async Task<int> RunBuildIndexes(string[] args)
{
    var force = args.Contains("--force");
    var batchSize = ParseIntArg(args, "--batch-size", 32);
    var maxTokens = ParseIntArg(args, "--max-tokens", 512);
    var singleIndex = ParseStringArg(args, "--index");
    var excludeIndexes = ParseListArg(args, "--exclude").ToHashSet(StringComparer.OrdinalIgnoreCase);

    var builder = Host.CreateApplicationBuilder(Array.Empty<string>());

    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });

    var dataRoot = GetDataRoot();
    var modelsDir = GetModelDir(dataRoot);
    var indexesDir = Path.Combine(dataRoot, ".leann", "indexes");

    builder.Services.AddSingleton<IEmbeddingService>(sp =>
        new OnnxEmbeddingService(modelsDir, sp.GetRequiredService<ILogger<OnnxEmbeddingService>>(), maxTokens));
    builder.Services.AddSingleton<IndexBuilder>();

    var host = builder.Build();
    var indexBuilder = host.Services.GetRequiredService<IndexBuilder>();

    Console.Error.WriteLine("LEANN Index Builder");
    Console.Error.WriteLine($"  Indexes:    {indexesDir}");
    Console.Error.WriteLine($"  Model:      {modelsDir}");
    Console.Error.WriteLine($"  Batch size: {batchSize}");
    Console.Error.WriteLine($"  Max tokens: {maxTokens}");
    Console.Error.WriteLine($"  Force:      {force}");
    if (singleIndex is not null)
        Console.Error.WriteLine($"  Index:      {singleIndex}");
    if (excludeIndexes.Count > 0)
        Console.Error.WriteLine($"  Exclude:    {string.Join(", ", excludeIndexes)}");
    Console.Error.WriteLine();

    var result = indexBuilder.BuildAll(indexesDir, batchSize, force, singleIndex, excludeIndexes);

    if (result.IsFailure)
    {
        Console.Error.WriteLine($"ERROR: {result.Error}");
        return 1;
    }

    return 0;
}

// -- Argument Helpers --

static int ParseIntArg(string[] args, string flag, int defaultValue)
{
    var idx = Array.IndexOf(args, flag);
    if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out var value))
        return value;
    return defaultValue;
}

static string? ParseStringArg(string[] args, string flag)
{
    var idx = Array.IndexOf(args, flag);
    if (idx >= 0 && idx + 1 < args.Length)
        return args[idx + 1];
    return null;
}

static List<string> ParseListArg(string[] args, string flag)
{
    var result = new List<string>();
    var idx = Array.IndexOf(args, flag);
    if (idx < 0) return result;

    var knownFlags = new HashSet<string>
    {
        "--index-name", "--chunk-size", "--chunk-overlap",
        "--code-chunk-size", "--code-chunk-overlap", "--index", "--batch-size", "--docs",
        "--interval", "--repos-config", "--max-tokens", "--exclude"
    };

    for (var i = idx + 1; i < args.Length; i++)
    {
        if (args[i].StartsWith("--"))
        {
            if (knownFlags.Contains(args[i]))
            {
                i++;
                continue;
            }
            continue;
        }
        result.Add(args[i]);
    }

    return result;
}

static string GetDataRoot() =>
    Environment.GetEnvironmentVariable("LEANN_DATA_ROOT")
    ?? Directory.GetCurrentDirectory();

static string GetModelDir(string dataRoot) =>
    Environment.GetEnvironmentVariable("LEANN_MODEL_DIR")
    ?? GetModelDir(dataRoot);

static void PrintUsage()
{
    Console.WriteLine("""
        LEANN .NET MCP Server -- Chunk, Embed & Search

        Usage:
          leann-dotnet                                 Start MCP server (default)
          leann-dotnet --build-passages [options]       Chunk source repos into passages
          leann-dotnet --build-indexes [options]        Compute passage embeddings
          leann-dotnet --rebuild [options]              Chain: build-passages then build-indexes
          leann-dotnet --watch [options]                Auto-sync repos and rebuild on changes
          leann-dotnet --help                           Show this help

        Passage Builder Options:
          --docs <path1> [<path2>...]   Source directories to chunk (required)
          --index-name NAME             Index name (default: current directory name)
          --chunk-size N                Text chunk size in chars (default: 256)
          --chunk-overlap N             Text chunk overlap in chars (default: 128)
          --code-chunk-size N           Code chunk size in chars (default: 512)
          --code-chunk-overlap N        Code chunk overlap in chars (default: 64)
          --include-hidden              Include hidden files/directories
          --force                       Overwrite existing passages

        Index Builder Options:
          --force            Rebuild embeddings even if they already exist
          --index NAME       Build only the specified index
          --batch-size N     Batch size for embedding computation (default: 32)
          --max-tokens N     Max token sequence length (default: 512, try 256 for faster indexing)
          --exclude NAME [NAME...]  Skip specified indexes

        Rebuild Mode:
          Accepts all options from both passage and index builders.
          Runs --build-passages first, then --build-indexes.

        Watch Mode:
          --interval N           Check interval in seconds (default: 300)
          --repos-config PATH    Path to repos.json config (default: .leann/repos.json)

          Periodically fetches each repo, detects new commits, and auto-rebuilds
          the passage + embedding index. Uses git to track changes. Press Ctrl+C to stop.

        The tool looks for indexes in <cwd>/.leann/indexes/ and the ONNX model
        in <cwd>/.leann/models/contriever-onnx/.
        """);
}
