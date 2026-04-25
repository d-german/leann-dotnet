# Workspace-Roots Auto-Detection (MCP Server Mode)

## Goals

Allow LEANN to be installed once globally (e.g. `dotnet tool install -g LeannMcp`)
and registered **once** in an MCP client (no per-project `cwd` or env vars), with
the server automatically resolving its data directory to the workspace the
client is currently using.

## Resolution Priority

The MCP server resolves its data root at **every tool invocation** in this order:

1. **`LEANN_DATA_ROOT` env var** — explicit override (highest precedence).
2. **MCP client roots** — fetched via `McpServer.RequestRootsAsync()`. If the
   client advertises multiple roots, the tiebreaker is:
   1. The first root containing a `.leann/` directory, otherwise
   2. The first non-empty root in `ListRoots`, otherwise
   3. The first non-empty root in `InitializeRoots` (cached from initialize handshake).
3. **`Directory.GetCurrentDirectory()`** — fallback (CLI compatibility, and clients
   that don't advertise roots but launch with a per-workspace cwd, e.g. VS Code).

CLI modes (`--watch`, `--build-passages`, `--build-indexes`, `--rebuild`,
`--setup`) keep the existing `LEANN_DATA_ROOT || cwd` resolution unchanged —
they're invoked per-project from a shell.

## MCP Roots Flow

```
client                        server
  | --- initialize ----------> |
  | <-- initialized ---------- |
  | --- call tool (search) --> |
  |                            | resolver.EnsureResolvedAsync(server, ct)
  |                            |   ↳ if env set, skip
  |                            |   ↳ else server.RequestRootsAsync()
  |                            |   ↳ store.SetListRoots(paths)
  |                            | indexManager.Search(...) uses fresh path
  | <-- result --------------- |
  | --- notifications/roots/list_changed -->
  |                            | (next tool call re-fetches via RequestRootsAsync)
```

## Multi-root Tiebreaker

```csharp
roots.FirstOrDefault(r => Directory.Exists(Path.Combine(r, ".leann")))
  ?? roots.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));
```

An existing `.leann/` directory is the strongest signal that a root is the
project the user wants indexed.

## Cache Invalidation

`IndexManager` stores `_lastResolvedIndexesDir`. On every tool call it asks the
resolver for the current `IndexesDir`; if it differs from the last value, the
in-memory `_cache` (`ConcurrentDictionary<string, LeannIndex>`) is cleared so
no stale FAISS / embeddings handle from a previous workspace leaks across.

## CLI vs MCP-Server Modes

| Mode             | Path resolution                                                  |
| ---------------- | ---------------------------------------------------------------- |
| MCP server       | `WorkspaceResolver` (env > MCP roots > cwd), per tool call       |
| `--watch`        | `GetDataRoot()` (env > cwd), set once at startup                 |
| `--build-*`      | `GetDataRoot()` (env > cwd), set once at startup                 |
| `--setup`        | `GetDataRoot()` (env > cwd), set once at startup                 |

## Edge Cases

| Situation                                  | Behavior                                            |
| ------------------------------------------ | --------------------------------------------------- |
| Client doesn't support `roots` capability  | `RequestRootsAsync` throws → caught → fall to cwd   |
| Client returns empty roots list            | Store stays empty → fall to cwd                     |
| `list_changed` arrives mid-tool-call       | Next call re-fetches; in-flight call uses old root  |
| `LEANN_DATA_ROOT` set                      | Always wins; roots & cwd ignored                    |
| Multiple workspaces, no `.leann/` in any   | First non-empty root selected                       |
