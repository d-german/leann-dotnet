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
2. **Embed** — computes 768-dim vectors using facebook/contriever via ONNX Runtime (DirectML on Windows, CoreML on macOS)
3. **Index** — stores embeddings in a flat vector index with L2-normalized cosine similarity
4. **Search** — any MCP client (VS Code Copilot, Claude Desktop, etc.) queries the index via semantic search

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (for building from source)
- GPU recommended but not required (falls back to CPU)

### 1. Build

```bash
git clone https://github.com/d-german/leann-dotnet.git
cd leann-dotnet
dotnet publish src/LeannMcp -r win-x64 --self-contained -c Release -o publish/win-x64
```

### 2. Set Up the Model

The contriever ONNX model is included in the repo (via Git LFS). Copy it to your data root:

```bash
cp -r models/contriever-onnx <data-root>/.leann/models/contriever-onnx
```

> **Note:** If you cloned without LFS, run `git lfs pull` first to download `model.onnx` (418 MB).

### 3. Index Your Code

```bash
cd <data-root>

# Step 1: Chunk source code into passages
leann-dotnet --build-passages --docs /path/to/my-repo --index-name my-repo

# Step 2: Compute embeddings (GPU-accelerated)
leann-dotnet --build-indexes --index my-repo

# Or do both in one command:
leann-dotnet --rebuild --docs /path/to/my-repo --index-name my-repo
```

This creates `<data-root>/.leann/indexes/my-repo/` with passages + embeddings.

### 4. Connect an MCP Client

Add to your MCP client config (e.g., `.vscode/mcp.json`):

```json
{
  "servers": {
    "leann": {
      "type": "stdio",
      "command": "/path/to/leann-dotnet",
      "args": [],
      "cwd": "<data-root>"
    }
  }
}
```

The `cwd` must point to the directory containing `.leann/`. The server auto-discovers all indexes at startup.

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
    │   └── contriever-onnx/       # ONNX model + tokenizer
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
| **MCP Server** | `leann-dotnet` | Default. Starts MCP server on stdio |
| **Chunk** | `leann-dotnet --build-passages` | Split source files into passages |
| **Embed** | `leann-dotnet --build-indexes` | Compute embeddings for passages |
| **Full Pipeline** | `leann-dotnet --rebuild` | Chunk + embed in one step |
| **Watch** | `leann-dotnet --watch` | Auto-sync git repos and rebuild on changes |

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

## Examples

```bash
# Index a single repo
leann-dotnet --rebuild --docs ~/projects/my-app --index-name my-app

# Index multiple directories into one index
leann-dotnet --build-passages --docs ~/proj/frontend ~/proj/backend --index-name my-app

# Rebuild all embeddings with smaller batches (low VRAM GPU)
leann-dotnet --build-indexes --force --batch-size 8

# Rebuild everything except one large index
leann-dotnet --build-indexes --exclude large-mono-repo

# Use shorter token sequences for faster indexing (slight quality trade-off)
leann-dotnet --build-indexes --force --max-tokens 256

# Auto-watch repos for changes
leann-dotnet --watch --interval 120
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
| "No ONNX model found" | Place model at `<cwd>/.leann/models/contriever-onnx/` |
| "Pre-computed embeddings not found" | Run `leann-dotnet --build-indexes` |
| "DirectML not available" | Falls back to CPU automatically. Update GPU drivers. |
| Slow first search | Call `leann_warmup` to pre-load the model |
| Out of GPU memory | Use `--batch-size 8` or lower |
| Network drive writes slow | Already fixed — uses bulk writes. Update to latest build. |

## License

MIT — see [LICENSE](LICENSE)