using CSharpFunctionalExtensions;
using LeannMcp.Models;
using LeannMcp.Tokenization;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;

namespace LeannMcp.Services;

/// <summary>
/// Computes text embeddings using an ONNX model described by an <see cref="EmbeddingModelDescriptor"/>.
/// Platform-aware: DirectML on Windows, CoreML on macOS (Apple Silicon).
/// Mean-pools last_hidden_state over non-padding tokens to produce 768-dim vectors.
/// </summary>
public sealed class OnnxEmbeddingService : IEmbeddingService, IDisposable
{
    private readonly InferenceSession _session;
    private readonly Tokenizer _tokenizer;
    private readonly ILogger<OnnxEmbeddingService> _logger;
    private readonly int _maxLength;
    private readonly object _inferenceGate = new();
    private bool _warmedUp;
    private bool _disposed;

    public OnnxEmbeddingService(
        string modelDirectory,
        EmbeddingModelDescriptor descriptor,
        IEnumerable<ITokenizerFactory> tokenizerFactories,
        ILogger<OnnxEmbeddingService> logger,
        int maxTokens = 512)
    {
        _logger = logger;
        _maxLength = maxTokens;

        var onnxPath = FindOnnxModel(modelDirectory);
        _session = CreateSession(onnxPath);

        var typeName = descriptor.TokenizerType.ToString();
        var factory = tokenizerFactories.SingleOrDefault(f => f.Type == typeName)
            ?? throw new InvalidOperationException($"No ITokenizerFactory registered for type '{typeName}'.");
        var tokResult = factory.Create(modelDirectory);
        if (tokResult.IsFailure)
            throw new InvalidOperationException($"Tokenizer load failed: {tokResult.Error}");
        _tokenizer = tokResult.Value;

        _logger.LogInformation("ONNX session created. Inputs: {Inputs}",
            string.Join(", ", _session.InputMetadata.Keys));
        _logger.LogInformation("Max token length: {MaxTokens}", _maxLength);
    }

    public void Warmup()
    {
        lock (_inferenceGate)
        {
            ThrowIfDisposed();

            if (_warmedUp) return;

            _logger.LogInformation("Warming up embedding model...");

            var result = ComputeEmbeddings(["__LEANN_WARMUP__"]);
            if (result.IsFailure)
                throw new InvalidOperationException($"Embedding warmup failed: {result.Error}");

            _warmedUp = true;
            _logger.LogInformation("Embedding model warmed up.");
        }
    }

    public Result<float[]> ComputeEmbedding(string text)
    {
        return ComputeEmbeddings([text])
            .Map(results => results[0]);
    }

    public Result<float[][]> ComputeEmbeddings(IReadOnlyList<string> texts)
    {
        lock (_inferenceGate)
        {
            try
            {
                ThrowIfDisposed();

                if (texts.Count == 0)
                    return Result.Failure<float[][]>("Cannot compute embeddings for empty text list");

                var (inputIds, attentionMask, seqLen) = Tokenize(texts);
                var batchSize = texts.Count;

                using var inputIdsTensor = OrtValue.CreateTensorValueFromMemory(
                    inputIds, [batchSize, seqLen]);
                using var attMaskTensor = OrtValue.CreateTensorValueFromMemory(
                    attentionMask, [batchSize, seqLen]);

                var inputs = new Dictionary<string, OrtValue>
                {
                    ["input_ids"] = inputIdsTensor,
                    ["attention_mask"] = attMaskTensor,
                };

                // DirectML sessions do not support concurrent Run calls on the same
                // InferenceSession, so tokenization + inference are serialized through
                // _inferenceGate to keep the singleton service safe for concurrent MCP requests.

                // Add token_type_ids if the model expects it
                OrtValue? tokenTypeTensor = null;
                if (_session.InputMetadata.ContainsKey("token_type_ids"))
                {
                    var tokenTypeIds = new long[batchSize * seqLen];
                    tokenTypeTensor = OrtValue.CreateTensorValueFromMemory(
                        tokenTypeIds, [batchSize, seqLen]);
                    inputs["token_type_ids"] = tokenTypeTensor;
                }

                try
                {
                    using var runOptions = new RunOptions();
                    using var outputs = _session.Run(runOptions, inputs, _session.OutputNames);

                    var lastHiddenState = outputs[0]; // (batch, seq_len, hidden_dim)
                    var shape = lastHiddenState.GetTensorTypeAndShape().Shape;
                    var hiddenDim = (int)shape[2];

                    var embeddings = MeanPool(
                        lastHiddenState.GetTensorDataAsSpan<float>(),
                        attentionMask, batchSize, seqLen, hiddenDim);

                    return Result.Success(embeddings);
                }
                finally
                {
                    tokenTypeTensor?.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Embedding computation failed");
                return Result.Failure<float[][]>($"Embedding computation failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Mean pooling: average hidden states over non-padding tokens.
    /// Matches the Python contriever post-processing exactly.
    /// </summary>
    private static float[][] MeanPool(
        ReadOnlySpan<float> hiddenStates,
        long[] attentionMask,
        int batchSize, int seqLen, int hiddenDim)
    {
        var results = new float[batchSize][];

        for (int b = 0; b < batchSize; b++)
        {
            var embedding = new float[hiddenDim];
            float tokenCount = 0;

            for (int s = 0; s < seqLen; s++)
            {
                if (attentionMask[b * seqLen + s] == 0) continue;

                tokenCount++;
                int offset = (b * seqLen + s) * hiddenDim;
                for (int d = 0; d < hiddenDim; d++)
                    embedding[d] += hiddenStates[offset + d];
            }

            if (tokenCount > 0)
            {
                for (int d = 0; d < hiddenDim; d++)
                    embedding[d] /= tokenCount;
            }

            results[b] = embedding;
        }

        return results;
    }

    private (long[] InputIds, long[] AttentionMask, int SeqLen) Tokenize(IReadOnlyList<string> texts)
    {
        var tokenized = new List<IReadOnlyList<int>>(texts.Count);
        int maxLen = 0;

        foreach (var text in texts)
        {
            var result = _tokenizer.EncodeToIds(text, _maxLength, out _, out _);
            tokenized.Add(result);
            if (result.Count > maxLen) maxLen = result.Count;
        }

        var seqLen = Math.Min(maxLen, _maxLength);

        var inputIds = new long[texts.Count * seqLen];
        var attentionMask = new long[texts.Count * seqLen];

        for (int b = 0; b < texts.Count; b++)
        {
            var ids = tokenized[b];
            var len = Math.Min(ids.Count, seqLen);

            for (int i = 0; i < len; i++)
            {
                inputIds[b * seqLen + i] = ids[i];
                attentionMask[b * seqLen + i] = 1;
            }
        }

        return (inputIds, attentionMask, seqLen);
    }

    private InferenceSession CreateSession(string onnxPath)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };

        ConfigureExecutionProvider(options);

        return new InferenceSession(onnxPath, options);
    }

    private void ConfigureExecutionProvider(SessionOptions options)
    {
        if (Environment.GetEnvironmentVariable("LEANN_FORCE_CPU") is "1" or "true")
        {
            _logger.LogInformation("LEANN_FORCE_CPU set — using CPU only");
            return;
        }

        if (OperatingSystem.IsWindows())
            ConfigureWindowsProvider(options);
        else if (OperatingSystem.IsMacOS())
            ConfigureMacOsProvider(options);
        else
            _logger.LogWarning("No GPU execution provider available for this platform. Using CPU (slow).");
    }

    private void ConfigureWindowsProvider(SessionOptions options)
    {
        try
        {
            options.EnableMemoryPattern = false;
            options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;

            var deviceId = ResolveDirectMlDevice();
            options.AppendExecutionProvider_DML(deviceId);
            _logger.LogInformation(
                "Using DirectML execution provider on device {DeviceId} (sequential execution, memory pattern disabled)",
                deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "DirectML not available ({Message}). Falling back to CPU. " +
                "For GPU acceleration, use the self-contained binary from the shared drive or GitHub Releases.",
                ex.Message);
        }
    }

    /// <summary>
    /// Resolves the DirectML device index. Priority:
    /// 1. LEANN_GPU_DEVICE env var (explicit override)
    /// 2. Auto-detect best GPU via DXGI enumeration (prefers discrete over integrated)
    /// 3. Falls back to device 0
    /// </summary>
    private int ResolveDirectMlDevice()
    {
        var envDevice = Environment.GetEnvironmentVariable("LEANN_GPU_DEVICE");
        if (!string.IsNullOrEmpty(envDevice))
        {
            if (int.TryParse(envDevice, out var explicitId))
            {
                _logger.LogInformation("LEANN_GPU_DEVICE={DeviceId} — using explicit GPU selection", explicitId);
                return explicitId;
            }

            _logger.LogWarning("LEANN_GPU_DEVICE={Value} is not a valid integer — ignoring", envDevice);
        }

        _logger.LogInformation("Detecting available GPUs...");
        var adapters = Infrastructure.DxgiAdapterEnumerator.EnumerateAdapters(_logger);

        if (adapters.Count == 0)
        {
            _logger.LogInformation("No DXGI adapters found — defaulting to device 0");
            return 0;
        }

        var best = Infrastructure.DxgiAdapterEnumerator.SelectBestAdapter(adapters, _logger);
        _logger.LogInformation("Auto-selected GPU [{DeviceId}]: {Name}",
            best, adapters.First(a => a.Index == best).Description);

        return best;
    }

    private void ConfigureMacOsProvider(SessionOptions options)
    {
        try
        {
            options.AppendExecutionProvider_CoreML();
            _logger.LogInformation("Using CoreML execution provider (Apple Silicon GPU + Neural Engine)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("CoreML not available ({Message}). Falling back to CPU.", ex.Message);
        }
    }

    private static string FindOnnxModel(string modelDirectory)
    {
        var modelPath = Path.Combine(modelDirectory, "model.onnx");
        if (File.Exists(modelPath)) return modelPath;

        var files = Directory.GetFiles(modelDirectory, "*.onnx");
        return files.Length > 0
            ? files[0]
            : throw new FileNotFoundException($"No ONNX model found in {modelDirectory}");
    }

    public void Dispose()
    {
        lock (_inferenceGate)
        {
            if (_disposed) return;

            _disposed = true;
            _session.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OnnxEmbeddingService));
    }
}

