using System.Collections.Concurrent;
using CSharpFunctionalExtensions;
using LeannMcp.Models;
using LeannMcp.Tokenization;
using Microsoft.Extensions.Logging;

namespace LeannMcp.Services;

/// <summary>
/// Lazily constructs and caches one <see cref="OnnxEmbeddingService"/> per
/// <see cref="EmbeddingModelDescriptor"/> id. Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Logs a warning when 3+ models are loaded simultaneously (VRAM pressure).
/// </summary>
public sealed class OnnxEmbeddingServiceFactory : IEmbeddingServiceFactory
{
    private const int VramPressureThreshold = 3;

    private readonly IModelPathResolver _pathResolver;
    private readonly Func<EmbeddingModelDescriptor, Result<IEmbeddingService>> _serviceCreator;
    private readonly ILogger<OnnxEmbeddingServiceFactory> _logger;
    private readonly ConcurrentDictionary<string, IEmbeddingService> _services =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, object> _modelLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public OnnxEmbeddingServiceFactory(
        IModelPathResolver pathResolver,
        IEnumerable<ITokenizerFactory> tokenizerFactories,
        ILoggerFactory loggerFactory,
        int maxTokens = 512)
        : this(
            pathResolver,
            loggerFactory,
            descriptor => CreateOnnxService(pathResolver, tokenizerFactories, loggerFactory, descriptor, maxTokens))
    {
    }

    internal OnnxEmbeddingServiceFactory(
        IModelPathResolver pathResolver,
        ILoggerFactory loggerFactory,
        Func<EmbeddingModelDescriptor, Result<IEmbeddingService>> serviceCreator)
    {
        _pathResolver = pathResolver;
        _serviceCreator = serviceCreator;
        _logger = loggerFactory.CreateLogger<OnnxEmbeddingServiceFactory>();
    }

    public IReadOnlyCollection<string> LoadedModelIds => _services.Keys.ToArray();

    public Result<IEmbeddingService> GetOrCreate(EmbeddingModelDescriptor descriptor)
    {
        if (_services.TryGetValue(descriptor.Id, out var cached))
            return Result.Success(cached);

        var gate = _modelLocks.GetOrAdd(descriptor.Id, _ => new object());
        lock (gate)
        {
            if (_services.TryGetValue(descriptor.Id, out cached))
                return Result.Success(cached);

            return EnsureOnnxFileExists(descriptor)
                .Bind(_ => _serviceCreator(descriptor))
                .Tap(svc => InsertLoaded(descriptor, svc));
        }
    }

    private Result<string> EnsureOnnxFileExists(EmbeddingModelDescriptor descriptor)
    {
        var modelDir = _pathResolver.GetModelDirectory(descriptor);
        var onnxPath = Path.Combine(modelDir, descriptor.OnnxFilename);
        return File.Exists(onnxPath)
            ? Result.Success(modelDir)
            : Result.Failure<string>(
                $"Model files for '{descriptor.Id}' not found at {onnxPath}. " +
                $"Run: leann-dotnet --setup --model {descriptor.Id}");
    }

    private static Result<IEmbeddingService> CreateOnnxService(
        IModelPathResolver pathResolver,
        IEnumerable<ITokenizerFactory> tokenizerFactories,
        ILoggerFactory loggerFactory,
        EmbeddingModelDescriptor descriptor,
        int maxTokens)
    {
        return Result.Try(
            () => (IEmbeddingService)new OnnxEmbeddingService(
                pathResolver.GetModelDirectory(descriptor),
                descriptor,
                tokenizerFactories,
                loggerFactory.CreateLogger<OnnxEmbeddingService>(),
                maxTokens),
            ex => $"Failed to construct embedding service for '{descriptor.Id}': {ex.Message}");
    }

    private void InsertLoaded(EmbeddingModelDescriptor descriptor, IEmbeddingService service)
    {
        _services[descriptor.Id] = service;

        _logger.LogInformation("Loaded embedding model '{Id}' ({Count} loaded).",
            descriptor.Id, _services.Count);

        if (_services.Count >= VramPressureThreshold)
        {
            _logger.LogWarning(
                "{Count} embedding models now loaded simultaneously ({Ids}). " +
                "Each ONNX session occupies ~400MB VRAM; consider unloading unused indexes if memory is constrained.",
                _services.Count, string.Join(", ", _services.Keys));
        }
    }
}
