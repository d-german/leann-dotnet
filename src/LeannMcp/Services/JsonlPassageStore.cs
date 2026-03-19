using System.Text.Json;
using CSharpFunctionalExtensions;
using LeannMcp.Models;

namespace LeannMcp.Services;

/// <summary>
/// Reads all passages from a JSONL file into memory for fast lookup.
/// Files are typically ~5MB / ~7K passages per index — trivial for in-memory storage.
/// </summary>
public sealed class JsonlPassageStore : IPassageStore
{
    private readonly IReadOnlyDictionary<string, PassageData> _passages;

    public int Count => _passages.Count;

    public JsonlPassageStore(string jsonlFilePath)
    {
        _passages = LoadPassages(jsonlFilePath);
    }

    public Result<PassageData> GetPassage(string passageId)
    {
        return _passages.TryGetValue(passageId, out var passage)
            ? Result.Success(passage)
            : Result.Failure<PassageData>($"Passage ID not found: {passageId}");
    }

    private static IReadOnlyDictionary<string, PassageData> LoadPassages(string filePath)
    {
        var passages = new Dictionary<string, PassageData>();

        foreach (var line in File.ReadLines(filePath, System.Text.Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var passage = JsonSerializer.Deserialize<PassageData>(line);
            if (passage?.Id is not null)
                passages[passage.Id] = passage;
        }

        return passages;
    }
}
