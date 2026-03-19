namespace LeannMcp.Services.Chunking;

public enum SplitMode
{
    /// <summary>Split at paragraph, then sentence, then word boundaries.</summary>
    Sentence,

    /// <summary>Prepend line numbers and split at newline boundaries.</summary>
    CodeLine,
}

/// <summary>
/// Splits text into overlapping chunks using configurable strategies.
/// </summary>
public interface ITextChunker
{
    IReadOnlyList<string> ChunkText(string text, int chunkSize, int chunkOverlap, SplitMode mode);
}
