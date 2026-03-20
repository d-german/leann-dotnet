using CSharpFunctionalExtensions;

namespace LeannMcp.Services;

/// <summary>
/// Downloads the contriever ONNX model to the local model directory.
/// Invoked by `leann-mcp --setup`. Skips if the model already exists.
/// </summary>
public static class ModelDownloader
{
    private const string ModelUrl =
        "https://github.com/d-german/leann-dotnet/releases/download/model-v1/contriever-onnx.zip";

    private const string ModelFileName = "model.onnx";

    public static async Task<Result> EnsureModelAsync(string modelDir, bool force = false)
    {
        var modelPath = Path.Combine(modelDir, ModelFileName);
        if (!force && File.Exists(modelPath))
        {
            Console.Error.WriteLine($"Model already exists at {modelDir}");
            Console.Error.WriteLine("Use --force to re-download.");
            return Result.Success();
        }

        Directory.CreateDirectory(modelDir);
        var zipPath = Path.Combine(Path.GetTempPath(), "contriever-onnx.zip");

        try
        {
            return await DownloadModel(zipPath)
                .Bind(() => ExtractModel(zipPath, modelDir))
                .Tap(() => Console.Error.WriteLine($"Model ready at {modelDir}"));
        }
        finally
        {
            TryDelete(zipPath);
        }
    }

    private static async Task<Result> DownloadModel(string zipPath)
    {
        Console.Error.WriteLine($"Downloading contriever ONNX model...");
        Console.Error.WriteLine($"  Source: {ModelUrl}");

        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(30);

        try
        {
            using var response = await client.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

            var buffer = new byte[81920];
            long downloaded = 0;
            int bytesRead;
            var lastProgress = -1;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloaded += bytesRead;

                if (totalBytes > 0)
                {
                    var percent = (int)(downloaded * 100 / totalBytes.Value);
                    if (percent != lastProgress && percent % 5 == 0)
                    {
                        Console.Error.WriteLine(
                            $"  {percent}% ({downloaded / (1024 * 1024)} / {totalBytes.Value / (1024 * 1024)} MB)");
                        lastProgress = percent;
                    }
                }
            }

            Console.Error.WriteLine($"  Download complete ({downloaded / (1024 * 1024)} MB)");
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Download failed: {ex.Message}");
        }
    }

    private static Result ExtractModel(string zipPath, string modelDir)
    {
        try
        {
            Console.Error.WriteLine("  Extracting...");
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, modelDir, overwriteFiles: true);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Extraction failed: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
