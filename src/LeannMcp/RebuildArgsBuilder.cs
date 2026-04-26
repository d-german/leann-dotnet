namespace LeannMcp;

/// <summary>
/// Pure helper for the <c>--rebuild</c> CLI mode. Extracted so the
/// argument-injection logic can be unit tested in isolation from the
/// (host-bound) Phase 1 / Phase 2 dispatch in <c>Program.cs</c>.
/// </summary>
internal static class RebuildArgsBuilder
{
    /// <summary>
    /// Returns a copy of <paramref name="args"/> with <c>--force</c> appended
    /// when not already present. Used by the <c>--rebuild</c> handler so that
    /// BOTH Phase 1 (chunking) and Phase 2 (embedding) regenerate from scratch
    /// — otherwise <c>--rebuild --chunk-size N</c> silently reuses the stale
    /// passages.jsonl from a prior build.
    /// </summary>
    public static string[] WithForce(string[] args)
    {
        if (args.Contains("--force")) return (string[])args.Clone();
        var list = new List<string>(args.Length + 1);
        list.AddRange(args);
        list.Add("--force");
        return list.ToArray();
    }
}
