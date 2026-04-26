using CSharpFunctionalExtensions;
using LeannMcp.Models;
using LeannMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeannMcp.Tests;

/// <summary>
/// Unit tests for OnnxEmbeddingServiceFactory caching, missing-file handling, and load tracking.
/// Uses the internal test-seam constructor with a stub creator delegate so no real ONNX file is required.
/// </summary>
public sealed class OnnxEmbeddingServiceFactoryTests : IDisposable
{
    private readonly string _tempDir;

    public OnnxEmbeddingServiceFactoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "leann-fact-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private sealed class FixedDirResolver : IModelPathResolver
    {
        private readonly string _dir;
        public FixedDirResolver(string dir) => _dir = dir;
        public string GetModelDirectory(EmbeddingModelDescriptor descriptor) => _dir;
    }

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public Result<float[]> ComputeEmbedding(string text) => new float[768];
        public Result<float[][]> ComputeEmbeddings(IReadOnlyList<string> texts)
            => texts.Select(_ => new float[768]).ToArray();
        public void Warmup() { }
    }

    private static EmbeddingModelDescriptor Descriptor(string id) =>
        new(
            Id: id,
            DisplayName: id,
            DownloadUrl: "https://example/" + id,
            ArchiveType: ArchiveType.Zip,
            OnnxFilename: "model.onnx",
            TokenizerType: TokenizerType.WordPiece,
            Dimensions: 768,
            Pooling: Pooling.Mean,
            MaxSequenceLength: 512,
            License: "Apache-2.0",
            Sha256: "");

    private void TouchOnnx(EmbeddingModelDescriptor d) =>
        File.WriteAllBytes(Path.Combine(_tempDir, d.OnnxFilename), Array.Empty<byte>());

    [Fact]
    public void GetOrCreate_SameDescriptorTwice_ReturnsSameInstance()
    {
        var d = Descriptor("test/model-a");
        TouchOnnx(d);
        var stub = new StubEmbeddingService();
        var factory = new OnnxEmbeddingServiceFactory(
            new FixedDirResolver(_tempDir),
            NullLoggerFactory.Instance,
            _ => Result.Success<IEmbeddingService>(stub));

        var a = factory.GetOrCreate(d);
        var b = factory.GetOrCreate(d);

        Assert.True(a.IsSuccess);
        Assert.True(b.IsSuccess);
        Assert.Same(a.Value, b.Value);
    }

    [Fact]
    public void GetOrCreate_MissingOnnxFile_FailsWithSetupHint()
    {
        var d = Descriptor("test/missing");
        // Intentionally do NOT TouchOnnx — file absent.
        var factory = new OnnxEmbeddingServiceFactory(
            new FixedDirResolver(_tempDir),
            NullLoggerFactory.Instance,
            _ => Result.Success<IEmbeddingService>(new StubEmbeddingService()));

        var result = factory.GetOrCreate(d);

        Assert.True(result.IsFailure);
        Assert.Contains("--setup --model", result.Error);
        Assert.Contains(d.Id, result.Error);
    }

    [Fact]
    public void LoadedModelIds_ReflectsInsertions()
    {
        var d1 = Descriptor("test/model-1");
        var d2 = Descriptor("test/model-2");
        TouchOnnx(d1);
        TouchOnnx(d2);
        var factory = new OnnxEmbeddingServiceFactory(
            new FixedDirResolver(_tempDir),
            NullLoggerFactory.Instance,
            _ => Result.Success<IEmbeddingService>(new StubEmbeddingService()));

        Assert.Empty(factory.LoadedModelIds);

        factory.GetOrCreate(d1);
        Assert.Single(factory.LoadedModelIds);
        Assert.Contains(d1.Id, factory.LoadedModelIds);

        factory.GetOrCreate(d2);
        Assert.Equal(2, factory.LoadedModelIds.Count);
        Assert.Contains(d2.Id, factory.LoadedModelIds);
    }
}
