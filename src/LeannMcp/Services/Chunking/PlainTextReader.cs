using CSharpFunctionalExtensions;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Fallback <see cref="IDocumentReader"/> that reads any file as UTF-8
/// text via <see cref="File.ReadAllText(string)"/>. Used for source code
/// and prose formats not handled by a more specific reader.
/// </summary>
public sealed class PlainTextReader : IDocumentReader
{
    /// <summary>
    /// Always returns true; <see cref="PlainTextReader"/> is the
    /// fallback reader and selection logic must consult it last.
    /// </summary>
    public bool CanHandle(string extension) => true;

    public Result<string> Read(string filePath) =>
        Result.Try(() => File.ReadAllText(filePath), ex => ex.Message);
}
