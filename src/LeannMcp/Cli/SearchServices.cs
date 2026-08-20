using LeannMcp.Services;
using LeannMcp.Tokenization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LeannMcp.Cli;

/// <summary>
/// Builds the minimal object graph needed to answer searches: model resolution,
/// tokenizers, the ONNX embedding factory and an <see cref="IndexManager"/>.
/// <para/>
/// Deliberately uses the <see cref="IndexManager"/> constructor that takes an
/// explicit indexes directory rather than the MCP <c>WorkspaceResolver</c>
/// overload -- CLI and daemon modes have no MCP roots to negotiate, so the
/// directory is pinned once at startup and never re-resolved.
/// </summary>
internal static class SearchServices
{
    public static ServiceProvider Build(string indexesDir, int maxTokens, LogLevel minimumLevel)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            // Diagnostics go to stderr so stdout stays a clean result channel.
            builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.SetMinimumLevel(minimumLevel);
        });

        services.AddSingleton<IModelPathResolver, DefaultModelPathResolver>();
        services.AddSingleton<ITokenizerFactory, WordPieceTokenizerFactory>();
        services.AddSingleton<ITokenizerFactory, RobertaBpeTokenizerFactory>();
        services.AddSingleton<IEmbeddingServiceFactory>(sp =>
            new OnnxEmbeddingServiceFactory(
                sp.GetRequiredService<IModelPathResolver>(),
                sp.GetServices<ITokenizerFactory>(),
                sp.GetRequiredService<ILoggerFactory>(),
                maxTokens));

        services.AddSingleton(sp =>
            new IndexManager(
                sp.GetRequiredService<IEmbeddingServiceFactory>(),
                sp.GetRequiredService<ILogger<IndexManager>>(),
                indexesDir));

        return services.BuildServiceProvider();
    }
}
