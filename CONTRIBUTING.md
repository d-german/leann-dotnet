# Contributing to LEANN .NET MCP Server

## Dev Setup

```bash
git clone https://github.com/HylandFoundation/leann-dotnet.git
cd leann-dotnet
dotnet restore
dotnet build
dotnet test
```

**Prerequisites:** .NET 10 SDK

## Architecture

```
src/LeannMcp/
  Program.cs                          CLI entry point (5 modes: MCP, build-passages, build-indexes, rebuild, watch)
  Tools/LeannTools.cs                 MCP tool definitions (search, list-indexes, index-stats)
  Services/
    OnnxEmbeddingService.cs           ONNX Runtime embedding (DirectML/CoreML/CPU)
    IndexManager.cs                   Index lifecycle (load, search, reload)
    IndexBuilder.cs                   Batch embedding computation
    FlatVectorIndex.cs                Brute-force cosine similarity search
    JsonlPassageStore.cs              JSONL passage storage
    Chunking/
      FileDiscoveryService.cs         Recursive file discovery with .gitignore
      DocumentChunker.cs              Section-aware document chunking
      TextChunker.cs                  Token-bounded text splitting
      PassageWriter.cs                JSONL output writer
    Watching/
      RepoWatcherService.cs           BackgroundService: git poll + auto-rebuild
      GitService.cs                   Git CLI wrapper (fetch, pull, hash)
  Models/
    PassageData.cs, SearchResult.cs, SourceDocument.cs, RepoConfig.cs, etc.
  Infrastructure/
    VectorMath.cs                     SIMD-accelerated cosine similarity
```

**Key patterns:**
- Railway-oriented programming with `Result<T>` (CSharpFunctionalExtensions)
- Interface-based DI (IEmbeddingService, IFileDiscovery, IDocumentChunker, etc.)
- Platform-aware EP selection in OnnxEmbeddingService.CreateSession()

## Creating a Release

Releases are fully automated via GitHub Actions. To publish a new version:

1. **Bump version** in `Directory.Build.props`:
   ```xml
   <Version>1.1.0</Version>
   ```

2. **Commit and tag:**
   ```bash
   git add Directory.Build.props
   git commit -m "Release v1.1.0"
   git tag v1.1.0
   git push origin main --tags
   ```

3. **GitHub Actions will automatically:**
   - Build self-contained binaries (win-x64 + osx-arm64)
   - Create a GitHub Release with both binaries as download assets
   - Pack a NuGet dotnet tool package
   - Push the package to ProGet

4. **Verify** at:
   - https://github.com/HylandFoundation/leann-dotnet/releases
   - https://proget.onbase.net/feeds/NuGet (search for HylandFoundation.Leann.Mcp)

## ProGet Setup (Maintainers)

The release workflow needs a `PROGET_API_KEY` GitHub secret to push NuGet packages.

1. Generate an API key at https://proget.onbase.net (your profile > API Keys)
2. Go to repo Settings > Secrets and variables > Actions
3. Add secret: `PROGET_API_KEY` = your key

Manual push (if needed):
```bash
dotnet pack src/LeannMcp/LeannMcp.csproj -c Release -o nupkg
dotnet nuget push nupkg/*.nupkg \
  --source https://proget.onbase.net/nuget/NuGet/v3/index.json \
  --api-key YOUR_KEY
```

## Adding New MCP Tools

1. Open `src/LeannMcp/Tools/LeannTools.cs`
2. Add a new method with `[McpServerTool]` attribute:
   ```csharp
   [McpServerToolType]
   public static class LeannTools
   {
       [McpServerTool, Description("My new tool description")]
       public static async Task<string> MyNewTool(
           IndexManager indexManager,
           [Description("Parameter description")] string param1)
       {
           // Implementation using injected services
       }
   }
   ```
3. Services are injected via DI (registered in Program.cs)
4. Return a string (MCP tools return text content)
5. Add tests in `tests/LeannMcp.Tests/`