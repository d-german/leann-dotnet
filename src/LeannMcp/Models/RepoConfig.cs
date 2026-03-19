using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;

namespace LeannMcp.Models;

/// <summary>
/// A single repository entry in the watcher configuration.
/// </summary>
public sealed record RepoEntry(
    [property: JsonPropertyName("folder")] string Folder,
    [property: JsonPropertyName("gitUrl")] string GitUrl,
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("indexName")] string IndexName,
    [property: JsonPropertyName("enabled")] bool Enabled = true);

/// <summary>
/// Top-level watcher configuration loaded from repos.json.
/// </summary>
public sealed record RepoConfig(
    [property: JsonPropertyName("intervalSeconds")] int IntervalSeconds,
    [property: JsonPropertyName("repos")] IReadOnlyList<RepoEntry> Repos);

/// <summary>
/// Loads and validates <see cref="RepoConfig"/> from a JSON file.
/// </summary>
public static class RepoConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Result<RepoConfig> Load(string path)
    {
        if (!File.Exists(path))
            return Result.Failure<RepoConfig>($"Config file not found: {path}");

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<RepoConfig>(json, JsonOptions);

            if (config is null)
                return Result.Failure<RepoConfig>("Failed to deserialize repos.json (null result)");

            if (config.Repos.Count == 0)
                return Result.Failure<RepoConfig>("repos.json contains no repo entries");

            return Result.Success(config);
        }
        catch (JsonException ex)
        {
            return Result.Failure<RepoConfig>($"Invalid JSON in {path}: {ex.Message}");
        }
    }
}