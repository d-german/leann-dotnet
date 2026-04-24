using System.IO.Compression;
using System.Security.Cryptography;
using CSharpFunctionalExtensions;
using LeannMcp.Models;

namespace LeannMcp.Services;

/// <summary>
/// Downloads an ONNX embedding model archive described by an <see cref="EmbeddingModelDescriptor"/>,
/// verifies its SHA256 (when configured), extracts it into the model directory, and writes a
/// <c>.sha256.ok</c> marker so future calls short-circuit without re-downloading.
/// </summary>
public static class ModelDownloader
{
    private const string MarkerFileName = ".sha256.ok";

    public static async Task<Result<string>> DownloadModelAsync(
        EmbeddingModelDescriptor descriptor,
        string modelDir,
        CancellationToken ct)
    {
        var onnxPath = Path.Combine(modelDir, descriptor.OnnxFilename);
        var markerPath = Path.Combine(modelDir, MarkerFileName);

        if (File.Exists(onnxPath) && File.Exists(markerPath))
        {
            Console.Error.WriteLine($"Model already present at {onnxPath} (marker found, skipping download)");
            return Result.Success(onnxPath);
        }

        Directory.CreateDirectory(modelDir);

        var zipPath = Path.Combine(
            Path.GetTempPath(),
            $"{descriptor.Id.Replace('/', '_')}-{Guid.NewGuid():N}.zip");

        try
        {
            var downloadResult = await DownloadAsync(descriptor, zipPath, ct);
            if (downloadResult.IsFailure)
                return Result.Failure<string>(downloadResult.Error);

            var verifyResult = await VerifySha256Async(descriptor, zipPath, ct);
            if (verifyResult.IsFailure)
            {
                TryDelete(zipPath);
                return Result.Failure<string>(verifyResult.Error);
            }

            try
            {
                Console.Error.WriteLine($"  Extracting to {modelDir}...");
                ZipFile.ExtractToDirectory(zipPath, modelDir, overwriteFiles: true);
            }
            catch (Exception ex)
            {
                return Result.Failure<string>($"Extraction failed: {ex.Message}");
            }

            await File.WriteAllTextAsync(markerPath, verifyResult.Value, ct);
            Console.Error.WriteLine($"Model ready at {onnxPath}");
            return Result.Success(onnxPath);
        }
        finally
        {
            TryDelete(zipPath);
        }
    }

    private static async Task<Result> DownloadAsync(
        EmbeddingModelDescriptor descriptor,
        string zipPath,
        CancellationToken ct)
    {
        Console.Error.WriteLine($"Downloading {descriptor.DisplayName} ({descriptor.Id})...");
        Console.Error.WriteLine($"  Source: {descriptor.DownloadUrl}");

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        try
        {
            using var response = await client.GetAsync(
                descriptor.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(
                zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

            var buffer = new byte[81920];
            long downloaded = 0;
            long lastLoggedBytes = 0;
            int lastLoggedPercent = -1;
            const long ProgressByteInterval = 5L * 1024 * 1024;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloaded += bytesRead;

                if (totalBytes is > 0)
                {
                    var percent = (int)(downloaded * 100 / totalBytes.Value);
                    if (percent != lastLoggedPercent && percent % 10 == 0)
                    {
                        Console.Error.WriteLine(
                            $"  {percent}% ({downloaded / (1024 * 1024)} / {totalBytes.Value / (1024 * 1024)} MB)");
                        lastLoggedPercent = percent;
                    }
                }
                else if (downloaded - lastLoggedBytes >= ProgressByteInterval)
                {
                    Console.Error.WriteLine($"  {downloaded / (1024 * 1024)} MB downloaded");
                    lastLoggedBytes = downloaded;
                }
            }

            Console.Error.WriteLine($"  Download complete ({downloaded / (1024 * 1024)} MB)");
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result.Failure($"Download failed: {ex.Message}");
        }
    }

    private static async Task<Result<string>> VerifySha256Async(
        EmbeddingModelDescriptor descriptor,
        string zipPath,
        CancellationToken ct)
    {
        await using var stream = new FileStream(
            zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hashBytes = await SHA256.HashDataAsync(stream, ct);
        var actual = Convert.ToHexString(hashBytes);

        if (string.IsNullOrWhiteSpace(descriptor.Sha256))
        {
            Console.Error.WriteLine(
                $"  WARN: skipping SHA256 verification (no expected hash configured). Actual: {actual}");
            return Result.Success(actual);
        }

        if (!string.Equals(actual, descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<string>(
                $"SHA256 mismatch for {descriptor.Id}: expected {descriptor.Sha256.ToUpperInvariant()}, actual {actual}");
        }

        Console.Error.WriteLine($"  SHA256 OK ({actual})");
        return Result.Success(actual);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
