using CSharpFunctionalExtensions;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Reads the textual content of a source file. Implementations are
/// responsible for one or more file extensions and decide whether they
/// can handle a given path via <see cref="CanHandle"/>.
/// </summary>
public interface IDocumentReader
{
    /// <summary>
    /// Returns true if this reader can produce text for the given file
    /// extension (including the leading dot, e.g. ".pdf").
    /// </summary>
    bool CanHandle(string extension);

    /// <summary>
    /// Reads the file at <paramref name="filePath"/> and returns its text
    /// content. Failures (missing file, unsupported encoding, encrypted
    /// PDF, etc.) are returned as <see cref="Result"/> failures rather
    /// than thrown.
    /// </summary>
    Result<string> Read(string filePath);
}
