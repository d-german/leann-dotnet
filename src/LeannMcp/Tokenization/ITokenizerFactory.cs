using CSharpFunctionalExtensions;
using Microsoft.ML.Tokenizers;

namespace LeannMcp.Tokenization;

public interface ITokenizerFactory
{
    /// <summary>Matches EmbeddingModelDescriptor.TokenizerType.ToString().</summary>
    string Type { get; }

    Result<Tokenizer> Create(string modelDir);
}
