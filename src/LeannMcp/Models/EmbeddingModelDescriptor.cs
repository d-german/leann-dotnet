namespace LeannMcp.Models;

public enum ArchiveType { Zip }
public enum TokenizerType { WordPiece, RobertaBpe }
public enum Pooling { Mean, Cls, LastToken }

public sealed record EmbeddingModelDescriptor(
    string Id,
    string DisplayName,
    string DownloadUrl,
    ArchiveType ArchiveType,
    string OnnxFilename,
    TokenizerType TokenizerType,
    int Dimensions,
    Pooling Pooling,
    int MaxSequenceLength,
    string License,
    string Sha256);
