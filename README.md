# LEANN .NET

[![CI](https://github.com/d-german/leann-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/d-german/leann-dotnet/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A native .NET 10 MCP server for semantic code search. Chunks source repositories, computes embeddings via ONNX (GPU-accelerated), and serves results over the [Model Context Protocol](https://modelcontextprotocol.io/).

> **Ported from [LEANN](https://github.com/yichuan-w/LEANN)** by Yichuan Wang — an innovative Python-based vector
> database and MCP server for personal AI. The original project pioneered graph-based selective recomputation for
> ultra-compact vector indexes (97% less storage than traditional solutions) and supports indexing everything from
> codebases to emails to browser history — all locally with zero cloud costs. This .NET port focuses on the
> semantic code search and MCP server pipeline, rebuilt natively for Windows/macOS with GPU acceleration via
> ONNX Runtime.
### Why a .NET port?

The original LEANN requires Python, WSL (on Windows), PyTorch, and several other dependencies — a
significant setup burden, especially on corporate machines with limited install permissions. This port
is a single self-contained executable with **zero external dependencies**. No Python, no WSL, no pip,
no virtual environments. Just download and run.

It works natively on **Windows** (DirectML GPU acceleration) and **macOS** (CoreML on Apple Silicon),
with automatic CPU fallback on any platform.
## How It Works

```
Source Code → Chunk → Embed (GPU) → Index → MCP Search
```

1. **Chunk** — splits source files into overlapping passages (code-aware: respects functions, classes, blocks)
2. **Embed** — computes 768-dim vectors using **jinaai/jina-embeddings-v2-base-code** (default; code-aware, 30 programming languages, 8192 max sequence length) via ONNX Runtime (DirectML on Windows, CoreML on macOS). The legacy facebook/contriever model is still selectable with `--model facebook/contriever` or `LEANN_MODEL=facebook/contriever`.
3. **Index** — stores embeddings in a flat vector index with L2-normalized cosine similarity
4. **Search** — any MCP client (VS Code Copilot, Claude Desktop, etc.) queries the index via semantic search

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- GPU recommended but not required (falls back to CPU)
- **macOS**: Apple Silicon (M1/M2/M3/M4) required — Intel Macs are not supported

### Option A: Install from NuGet (recommended)

```bash
dotnet tool install -g LeannMcp
leann-mcp --setup                                  # downloads the default jina-embeddings-v2-base-code model (~282 MB zip → ~306 MB ONNX)
# or explicitly:
leann-mcp --setup --model jinaai/jina-embeddings-v2-base-code
leann-mcp --setup --model facebook/contriever     # legacy 418 MB English-prose model
```

The model is extracted to `<LEANN_DATA_ROOT>/models/<sanitized-id>/` and is idempotent (re-running `--setup` is a no-op once the SHA256 marker is present; pass `--force` to re-download).

### Option B: Build from source

```bash
# Git LFS required — the repo includes the 418 MB ONNX model
git lfs install
git clone https://github.com/d-german/leann-dotnet.git
cd leann-dotnet
dotnet publish src/LeannMcp -r win-x64 --self-contained -c Release -o publish/win-x64
```

> **Forgot `git lfs install`?** Run `git lfs pull` to download `model.onnx`.
>
> **Note:** When built from source the executable is named `leann-dotnet` (the `AssemblyName`).
> All command examples in this README use `leann-mcp`, which is the command installed by
> `dotnet tool install -g LeannMcp`. Substitute `leann-dotnet` (or the full path to your
> published binary) if you are running a source build.

### Index Your Code

```bash
cd <data-root>

# Step 1: Chunk source code into passages
leann-mcp --build-passages --docs /path/to/my-repo --index-name my-repo

# Step 2: Compute embeddings (GPU-accelerated)
leann-mcp --build-indexes --index my-repo

# Or do both in one command:
leann-mcp --rebuild --docs /path/to/my-repo --index-name my-repo

# Restrict to specific file types (whitelist; comma- or space-separated):
leann-mcp --rebuild --docs /path/to/my-repo --index-name my-repo \
  --file-types .cs,.csproj,.sln,.props,.targets
```

This creates `<data-root>/.leann/indexes/my-repo/` with passages + embeddings.

> **Tip:** Use `--file-types` to dramatically shrink the index on large polyglot
> repos. Restricting a 2.4M-passage C#/JS/CSS mono-repo to `.cs,.csproj,.sln`
> typically cuts the embeddings file from ~7 GB down to ~1–2 GB and removes
> noise (minified JS, vendored libraries, test fixtures) from search results.

### 4. Connect an MCP Client

Add to your MCP client config (e.g., `.vscode/mcp.json`):

```json
{
  "servers": {
    "leann": {
      "type": "stdio",
      "command": "leann-mcp",
      "args": ["--mcp"],
      "env": {
        "LEANN_DATA_ROOT": "/path/to/your/data"
      }
    }
  }
}
```

`LEANN_DATA_ROOT` points to the directory containing `.leann/indexes/` and `models/`. To switch models for an MCP session, add `"LEANN_MODEL": "facebook/contriever"` (or any registered id) to the `env` block.

> **Index compatibility:** every index records the embedding model + dimensions used to build it. Loading an index that was built with a different model than the currently active one is **refused at load time** (the server logs `IndexCompatibility: refusing index ...`). Either rebuild with the new model, or set `LEANN_MODEL` back to the original.

### 5. Search

From your MCP client, use these tools:
- **`leann_search`** — semantic search across all indexed repos
- **`leann_list`** — list available indexes
- **`leann_warmup`** — pre-load embedding model (faster first search)

## Data Layout

```
<data-root>/
└── .leann/
    ├── models/
    │   ├── jinaai-jina-embeddings-v2-base-code/   # default code-aware model
    │   │   ├── model.onnx
    │   │   ├── tokenizer.json
    │   │   ├── vocab.json
    │   │   ├── merges.txt
    │   │   └── .sha256.ok                          # idempotency marker
    │   └── facebook-contriever/                    # legacy English-prose model (optional)
    │       ├── model.onnx
    │       └── vocab.txt
    └── indexes/
        ├── my-repo/
        │   ├── documents.leann.meta.json
        │   ├── documents.leann.passages.jsonl
        │   ├── documents.ids.txt
        │   ├── documents.embeddings.bin
        │   └── documents.embeddings.meta.json
        └── another-repo/
            └── ...
```

## CLI Reference

### Modes

| Mode | Command | Description |
|------|---------|-------------|
| **MCP Server** | `leann-mcp` | Default. Starts MCP server on stdio |
| **Chunk** | `leann-mcp --build-passages` | Split source files into passages |
| **Embed** | `leann-mcp --build-indexes` | Compute embeddings for passages |
| **Full Pipeline** | `leann-mcp --rebuild` | Chunk + embed in one step |
| **Watch** | `leann-mcp --watch` | Auto-sync git repos and rebuild on changes |
| **Setup** | `leann-mcp --setup [--model ID] [--force]` | Download ONNX model (~282 MB jina, ~418 MB contriever; one-time per model) |

### Passage Builder Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--docs <path> [...]` | Source directories to chunk (required) | — |
| `--index-name NAME` | Index name | cwd directory name |
| `--chunk-size N` | Text chunk size in chars | 256 |
| `--chunk-overlap N` | Text chunk overlap | 128 |
| `--code-chunk-size N` | Code chunk size in chars | 512 |
| `--code-chunk-overlap N` | Code chunk overlap | 64 |
| `--include-hidden` | Include hidden files/dirs | false |
| `--file-types EXT [EXT...]` | Whitelist of extensions (e.g. `.cs .csproj` or `.cs,.csproj`). When set, overrides the built-in extension defaults | (built-in defaults) |
| `--exclude-paths PAT [PAT...]` | Gitignore-style globs to skip (e.g. `"**/Tests/**" "**/Mocks/**"`). Supports `**`, `*`, `?`. Combined with any `.gitignore` files found in the tree | (none) |
| `--no-ast` | Disable AST-aware code chunking (Roslyn for C#, brace-balanced for TS/JS/Java/C-family) and fall back to the legacy line-based sliding-window chunker | false (AST enabled) |
| `--force` | Overwrite existing passages | false |

### Index Builder Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--force` | Rebuild even if embeddings exist | false |
| `--index NAME` | Build only this index | all |
| `--exclude NAME [...]` | Skip specified indexes | — |
| `--batch-size N` | Passages per GPU batch | 32 |
| `--max-tokens N` | Max token sequence length | 512 |

### Watch Mode Flags

| Flag | Description | Default |
|------|-------------|---------|
| `--interval N` | Check interval in seconds | 300 |
| `--repos-config PATH` | Path to repos.json config | `.leann/repos.json` |
| `--force` | Force a full rebuild on the first sweep, then resume normal change-detection. Useful for one-shot reindex of every repo without changing the daemon model | false |

#### Per-repo settings in `repos.json`

Each entry supports optional per-repo filters and chunking overrides:

```jsonc
{
  "intervalSeconds": 300,
  "repos": [
    {
      "folder": "C:\\OnBase.NET",
      "gitUrl": "git@github.com:org/OnBase.NET.git",
      "branch": "main",
      "indexName": "onbase-dotnet",
      "enabled": true,

      // Optional — same semantics as the CLI flags
      "fileTypes":        [".cs", ".props", ".targets", ".json"],
      "excludePaths": [
        "**/Tests/**", "**/*.Tests/**", "**/*Tests.cs",
        "**/Mocks/**", "**/third-party-assemblies/**",
        "**/project.assets.json", "**/*.deps.json"
      ],
      "codeChunkSize":    1024,   // overrides default 512
      "codeChunkOverlap": 128,    // overrides default 64
      "useAst":           true    // AST-aware chunking (default true)
    },
    {
      "folder": "C:\\angular-app",
      "gitUrl": "git@github.com:org/angular-app.git",
      "branch": "main",
      "indexName": "angular-app",
      "enabled": true,
      "fileTypes": [".ts", ".html", ".scss"],
      "excludePaths": ["**/node_modules/**", "**/dist/**", "**/*.spec.ts"]
    }
  ]
}
```

All per-repo fields are optional — existing `repos.json` files keep working unchanged.

## AST-Aware Code Chunking

Starting in **1.0.15**, code files are chunked by language structure rather than by raw character windows. This dramatically improves search relevance by ensuring each passage is a complete logical unit (a method, a property, a function) instead of a half-method that happens to start mid-line.

| Language(s) | Strategy | Notes |
|-------------|----------|-------|
| C# (`.cs`) | **Roslyn AST** (`Microsoft.CodeAnalysis.CSharp`) | One chunk per method, constructor, property, indexer, operator, event, field, enum, delegate. Nested types and file-scoped namespaces supported. Each chunk is prefixed with a `// {namespace}.{type}.{member}` context comment so embeddings carry symbolic context. |
| TypeScript, JavaScript, Java, C/C++, Go, Rust, Kotlin, Scala, Swift, PHP | **Brace-balanced walker** | A small state machine that tracks strings, line/block comments, and template-literal interpolation (`${...}`) to split on top-level `{...}` blocks without being confused by braces inside strings. No native dependencies. |
| Markdown, JSON, YAML, plain text, etc. | **Sliding-window character chunker** (legacy) | Unchanged from previous releases. |

After chunking, **every** passage (regardless of strategy) goes through a quality filter that drops:

- Trimmed length < 20 chars (e.g. lone `}` lines)
- Punctuation ratio > 70% (e.g. `} } } } }` or `;;; ; ;;;`)
- A run of ≥ 200 base64 characters (`[A-Za-z0-9+/]{200,}`) — strips PDF/image blobs accidentally embedded in source
- An underscore run > 10 (e.g. `__________`)

The filter is the reason that, after upgrading, you'll typically see fewer passages in the index than before (1-3% drop is normal) — the eliminated chunks were noise that previously dominated unrelated queries.

### Opting out

If a Roslyn parse error or an exotic file format causes problems on your repo, fall back to the legacy chunker:

```bash
# Per-build (CLI flag)
leann-dotnet --build-passages --docs C:\repo --no-ast

# Per-repo (repos.json)
{
  "folder": "C:\\repo",
  "useAst": false
}
```

The CLI also prints which mode is active in its startup banner:

```
LEANN Passage Builder
  ...
  Code chunk: 512 (overlap 64)
  AST chunk:  enabled (Roslyn for C#, brace-balanced for C-family)
  File types: ...
```

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `LEANN_DATA_ROOT` | Directory containing `.leann/indexes/` and `models/` | Current working directory |
| `LEANN_MODEL` | Active embedding model id (e.g. `jinaai/jina-embeddings-v2-base-code`, `facebook/contriever`). Equivalent to passing `--model` on the command line | `jinaai/jina-embeddings-v2-base-code` |
| `LEANN_MODEL_DIR` | Override the model directory location (rarely needed; computed from `LEANN_DATA_ROOT` + sanitized model id by default) | `<LEANN_DATA_ROOT>/models/<sanitized-id>` |
| `LEANN_FORCE_CPU` | Set to `1` or `true` to disable GPU acceleration | (GPU enabled) |

### GPU Acceleration

| Platform | Provider | Automatic |
|----------|----------|-----------|
| **Windows** | DirectML (any GPU) | ✅ Yes |
| **macOS** | CoreML (Apple Silicon GPU + Neural Engine) | ✅ Yes |
| **Linux** | CPU only | — |

GPU is used for both indexing and search query embedding. For MCP server use (search only),
you can set `LEANN_FORCE_CPU=1` to free your GPU — single query embedding is fast on CPU.
## Examples

```bash
# Index a single repo
leann-mcp --rebuild --docs ~/projects/my-app --index-name my-app

# Index multiple directories into one index
leann-mcp --build-passages --docs ~/proj/frontend ~/proj/backend --index-name my-app

# Index only specific file types (e.g. C# source only)
leann-mcp --rebuild --docs ./my-repo --index-name my-repo \
  --file-types .cs,.csproj,.sln,.props,.targets

# Skip test projects, mocks, fixtures (gitignore-style globs)
leann-mcp --rebuild --docs ./my-repo --index-name my-repo \
  --file-types .cs,.csproj,.sln,.props,.targets \
  --exclude-paths "**/Tests/**" "**/*.Tests/**" "**/Mocks/**" "**/*Test.cs" "**/*Tests.cs"

# Rebuild all embeddings with smaller batches (low VRAM GPU)
leann-mcp --build-indexes --force --batch-size 8

# Rebuild everything except one large index
leann-mcp --build-indexes --exclude large-mono-repo

# Use shorter token sequences for faster indexing (slight quality trade-off)
leann-mcp --build-indexes --force --max-tokens 256

# Auto-watch repos for changes
leann-mcp --watch --interval 120

# One-shot full reindex of every watched repo, then keep watching incrementally
leann-mcp --watch --force
```

## Tuning Guide

### Chunk Size vs. Batch Size vs. Max Tokens

These three settings control different parts of the pipeline:

| Setting | What It Controls | CPU/GPU | When to Change |
|---------|-----------------|---------|----------------|
| `--chunk-size` | Characters per text passage | CPU (chunking) | Adjust search granularity |
| `--code-chunk-size` | Characters per code passage | CPU (chunking) | Adjust code search granularity |
| `--batch-size` | Passages processed per GPU call | GPU (embedding) | Match to your VRAM |
| `--max-tokens` | Token sequence length per passage | GPU (embedding) | Trade speed vs. context |

### Chunk Size (search quality)

Chunk size controls how much context each passage contains. **This is independent of GPU power.**

- **Smaller chunks** (128-256 chars) → more precise search hits, less context per result
- **Larger chunks** (512-1024 chars) → more context per result, but may dilute relevance
- **Code chunks** default larger (512) because functions/methods need more context than prose

> **Note:** Passages longer than `--max-tokens` (default 512 tokens ≈ ~2000 chars) are truncated
> during embedding. Making chunks larger than ~2000 chars wastes disk without improving search.

### Batch Size (GPU utilization)

Batch size determines how many passages are embedded simultaneously. **This is where GPU VRAM matters.**

| GPU VRAM | Recommended `--batch-size` |
|----------|---------------------------|
| 4 GB | 32 (default) |
| 8 GB | 64 |
| 12+ GB | 128 |

```bash
# RTX 3500 Ada (12 GB) — crank up the batch size
leann-mcp --rebuild --docs ./my-repo --index-name my-repo --batch-size 128

# Low-VRAM GPU or integrated graphics
leann-mcp --rebuild --docs ./my-repo --index-name my-repo --batch-size 8
```

### Max Tokens (speed vs. context)

The contriever model processes up to 512 tokens per passage. Lowering this speeds up embedding
(attention is O(n²)) at the cost of truncating longer passages.

```bash
# Faster indexing, slight quality trade-off on long passages
leann-mcp --build-indexes --max-tokens 256

# Full context (default)
leann-mcp --build-indexes --max-tokens 512
```
## Performance

Embedding throughput on NVIDIA RTX A1000 (4GB VRAM):

| Passage Type | Avg Tokens | Throughput |
|---|---|---|
| Short text/docs | ~100 | 150-175 passages/s |
| Long code (C#) | ~300 | 8-20 passages/s |

**Optimizations included:**
- Length-sorted batching (groups similar-length passages to minimize padding waste)
- Bulk file writes (single I/O call for embedding output, critical for network drives)
- Configurable `--max-tokens` to reduce O(n²) attention cost on long passages

## Platform Support

| Platform | GPU Provider | Accelerator |
|----------|-------------|-------------|
| Windows x64 | DirectML | NVIDIA, AMD, Intel Arc |
| macOS ARM64 | CoreML | Apple Silicon GPU + Neural Engine |
| Linux x64 | CPU | (GPU EPs can be added) |

GPU support is automatic with graceful fallback — if the GPU provider isn't available, it logs a warning and uses CPU.

## Building & Publishing

```bash
# Build
dotnet build

# Test
dotnet test

# Publish self-contained binary
dotnet publish src/LeannMcp -r win-x64 --self-contained -c Release -o publish/win-x64

# macOS
dotnet publish src/LeannMcp -r osx-arm64 --self-contained -c Release -o publish/osx-arm64

# Install as dotnet tool (framework-dependent)
dotnet pack src/LeannMcp -c Release
dotnet tool install --global --add-source src/LeannMcp/bin/Release LeannMcp
```

## Troubleshooting

| Problem | Solution |
|---------|----------|
| "No ONNX model found" | Run `leann-mcp --setup` (downloads the active model). The model directory is `<LEANN_DATA_ROOT>/models/<sanitized-model-id>/`. |
| "Pre-computed embeddings not found" | Run `leann-mcp --build-indexes` |
| `IndexCompatibility: refusing index ... built with <other-model>` | Either rebuild with the active model (`leann-mcp --rebuild ...`) or set `LEANN_MODEL` to the model that built the index. |
| "DirectML not available" | Falls back to CPU automatically. Update GPU drivers. |
| Slow first search | Call `leann_warmup` to pre-load the model |
| Out of GPU memory (4 GB VRAM) | Use `--batch-size 8` and/or `--max-tokens 256`. Set `LEANN_FORCE_CPU=1` if it still OOMs — single-query search is fast on CPU. |
| Network drive writes slow | Already fixed — uses bulk writes. Update to latest build. |

### Migrating from 1.0.x (contriever-only) to 1.0.16+ (jina default)

1. `dotnet tool update -g LeannMcp`
2. `leann-mcp --setup` (downloads jina; existing contriever model is **not** deleted).
3. Rebuild your indexes: `leann-mcp --rebuild --docs <repo> --index-name <name>` — required because index files record the model that built them and the new compatibility guard refuses cross-model loads.
4. Optional: keep using contriever by setting `LEANN_MODEL=facebook/contriever` (env var) or passing `--model facebook/contriever` to `--setup`/`--rebuild`.

## License

MIT — see [LICENSE](LICENSE)