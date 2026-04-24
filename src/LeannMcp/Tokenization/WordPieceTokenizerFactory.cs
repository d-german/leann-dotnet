using CSharpFunctionalExtensions;
using Microsoft.ML.Tokenizers;

namespace LeannMcp.Tokenization;

public sealed class WordPieceTokenizerFactory : ITokenizerFactory
{
    public string Type => "WordPiece";

    public Result<Tokenizer> Create(string modelDir)
    {
        var vocabPath = Path.Combine(modelDir, "vocab.txt");
        if (!File.Exists(vocabPath))
            return Result.Failure<Tokenizer>($"WordPiece vocab not found: {vocabPath}");

        return Result.Try(
            () => (Tokenizer)WordPieceTokenizer.Create(vocabPath, new WordPieceOptions { UnknownToken = "[UNK]" }),
            ex => $"Failed to load WordPiece tokenizer: {ex.Message}");
    }
}
