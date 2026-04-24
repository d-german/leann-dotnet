using CSharpFunctionalExtensions;
using Microsoft.ML.Tokenizers;

namespace LeannMcp.Tokenization;

public sealed class RobertaBpeTokenizerFactory : ITokenizerFactory
{
    public string Type => "RobertaBpe";

    public Result<Tokenizer> Create(string modelDir)
    {
        var vocabPath = Path.Combine(modelDir, "vocab.json");
        var mergesPath = Path.Combine(modelDir, "merges.txt");

        if (!File.Exists(vocabPath))
            return Result.Failure<Tokenizer>($"RoBERTa BPE vocab not found: {vocabPath}");
        if (!File.Exists(mergesPath))
            return Result.Failure<Tokenizer>($"RoBERTa BPE merges not found: {mergesPath}");

        return Result.Try(
            () =>
            {
                // jina-embeddings-v2-base-code uses RoBERTa-style byte-level BPE.
                // tokenizer_config.json sets add_prefix_space=false; the post-processor
                // wraps inputs with <s>...</s>. Microsoft.ML.Tokenizers handles the
                // BOS/EOS wrapping when BeginningOfSentenceToken / EndOfSentenceToken
                // are configured on BpeOptions.
                var options = new BpeOptions(vocabPath, mergesPath)
                {
                    PreTokenizer = PreTokenizer.CreateWhiteSpace(specialTokens: null),
                    ByteLevel = true,
                    UnknownToken = "<unk>",
                    BeginningOfSentenceToken = "<s>",
                    EndOfSentenceToken = "</s>",
                    SpecialTokens = new Dictionary<string, int>
                    {
                        ["<s>"] = 0,
                        ["<pad>"] = 1,
                        ["</s>"] = 2,
                        ["<unk>"] = 3,
                        ["<mask>"] = 50264,
                    },
                };

                return (Tokenizer)BpeTokenizer.Create(options);
            },
            ex => $"Failed to load RoBERTa BPE tokenizer: {ex.Message}");
    }
}
