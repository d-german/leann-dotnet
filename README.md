# LEANN .NET MCP Server

[![CI](https://github.com/HylandFoundation/leann-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/HylandFoundation/leann-dotnet/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Native .NET 10 MCP server for semantic code search. GPU-accelerated embedding via ONNX Runtime (DirectML on Windows, CoreML on macOS Apple Silicon).

## Installation

Choose the method that works best for you:

| Method | Platforms | GPU Accel | Auto-Update | Requires |
|--------|-----------|-----------|-------------|----------|
| **dotnet tool** | Win, Mac, Linux | CPU only* | `dotnet tool update` | .NET 10 SDK |
| **GitHub Releases** | Win, Mac | DirectML / CoreML | Manual download | Nothing |
| **Shared Drive** | Win, Mac | DirectML / CoreML | Automatic | VPN / Network |

*\*Self-contained binaries include full GPU support. The dotnet tool falls back to CPU on Windows if DirectML native libs are not present.*

### Option A: dotnet tool (easiest install)

```bash
dotnet tool install --global HylandFoundation.Leann.Mcp \
  --add-source https://proget.onbase.net/nuget/NuGet/v3/index.json
```

Update: `dotnet tool update --global HylandFoundation.Leann.Mcp`

MCP config (`settings.json` or `.vscode/mcp.json`):

```json
{
  "servers": {
    "leann-server": {
      "type": "stdio",
      "command": "leann-mcp",
      "args": [],
      "cwd": "C:\\"
    }
  }
}
```

> **Note:** The dotnet tool requires a `.leann/` data directory (models + indexes) at the `cwd` path. Copy from the shared drive or build locally.

### Option B: GitHub Releases (self-contained, GPU-accelerated)

Download the latest binary from [Releases](https://github.com/HylandFoundation/leann-dotnet/releases):

- **Windows:** `leann-dotnet.exe` (DirectML GPU)
- **macOS:** `leann-dotnet` (CoreML GPU + Neural Engine)

```json
{
  "servers": {
    "leann-server": {
      "type": "stdio",
      "command": "C:\\path\\to\\leann-dotnet.exe",
      "args": [],
      "cwd": "C:\\path\\to\\data-root"
    }
  }
}
```

### Option C: Shared Drive (zero setup, always latest)

**Windows:**

```powershell
net use Z: \\RDV-010751.hylandqa.net\hcw-agent /persistent:yes
```

```json
{
  "servers": {
    "leann-server": {
      "type": "stdio",
      "command": "Z:\\.leann\\bin\\leann-dotnet.exe",
      "args": [],
      "cwd": "Z:\\"
    }
  }
}
```

**macOS:**

```bash
open smb://RDV-010751.hylandqa.net/hcw-agent
```

```json
{
  "servers": {
    "leann-server": {
      "type": "stdio",
      "command": "/Volumes/hcw-agent/.leann/bin/leann-dotnet",
      "args": [],
      "cwd": "/Volumes/hcw-agent"
    }
  }
}
```

### Option D: Local Copy (fastest, works offline)

Copy from the shared drive for best performance without network dependency:

```powershell
mkdir C:\leann-local\.leann
Copy-Item Z:\.leann\bin\leann-dotnet.exe C:\leann-local\.leann\bin\ -Force
Copy-Item Z:\.leann\models C:\leann-local\.leann\models -Recurse -Force
Copy-Item Z:\.leann\indexes C:\leann-local\.leann\indexes -Recurse -Force
```

Refresh later: `robocopy Z:\.leann\indexes C:\leann-local\.leann\indexes /MIR /NJH /NJS /NDL`

## CLI Modes

The exe supports four modes: **MCP server** (default), **Passage Builder**, **Index Builder**, and **Rebuild** (full pipeline).

### MCP Server (default)

```
leann-dotnet.exe
```

Starts the MCP server on stdio. Used by VS Code / Claude Desktop / any MCP client.

### Passage Builder

Chunk source repos into passages (replaces the Python chunking pipeline).

```powershell
# Chunk a single repo
leann-dotnet.exe --build-passages --docs C:\path\to\my-repo --index-name my-repo

# Chunk multiple source directories into one index
leann-dotnet.exe --build-passages --docs C:\repo1 C:\repo2 --index-name combined

# Force overwrite existing passages
leann-dotnet.exe --build-passages --docs C:\repo --index-name my-repo --force

# Custom chunk sizes
leann-dotnet.exe --build-passages --docs C:\repo --chunk-size 512 --chunk-overlap 256
```

**Flags:**

| Flag | Description | Default |
|------|-------------|---------|
| `--build-passages` | Run passage builder mode | — |
| `--docs <paths>` | Source directories to chunk (required) | — |
| `--index-name NAME` | Index name | current dir name |
| `--chunk-size N` | Text chunk size in chars | 256 |
| `--chunk-overlap N` | Text chunk overlap in chars | 128 |
| `--code-chunk-size N` | Code chunk size in chars | 512 |
| `--code-chunk-overlap N` | Code chunk overlap in chars | 64 |
| `--include-hidden` | Include hidden files/directories | false |
| `--force` | Overwrite existing passages | false |

### Rebuild (Full Pipeline)

Chunks source repos **and** builds embeddings in one command.

```powershell
# Full pipeline: chunk + embed
leann-dotnet.exe --rebuild --docs C:\path\to\my-repo --index-name my-repo

# With all options
leann-dotnet.exe --rebuild --docs C:\repo --index-name my-repo --force --batch-size 16
```

### Watch Mode (Auto-Sync)

Continuously monitors git repos for new commits and auto-rebuilds indexes when changes are detected.

```powershell
# Start watcher with default settings (check every 5 minutes)
cd Z:\
leann-dotnet.exe --watch

# Custom interval (check every 2 minutes)
leann-dotnet.exe --watch --interval 120

# Custom config file path
leann-dotnet.exe --watch --repos-config C:\path\to\repos.json
```

Press **Ctrl+C** to stop the watcher.

**Flags:**

| Flag | Description | Default |
|------|-------------|---------|
| `--watch` | Run watch mode | — |
| `--interval N` | Check interval in seconds | 300 |
| `--repos-config PATH` | Path to repos.json config | `.leann/repos.json` |

**How it works:**
1. Loads repo configuration from `repos.json`
2. For each enabled repo: `git fetch` → compare HEAD vs origin → `git pull` if changed
3. If changed: runs full rebuild pipeline (discover → chunk → embed)
4. Stores last-built commit hash in `.leann/indexes/<name>/.git-hash`
5. Sleeps for the configured interval, then repeats

**repos.json format:**
```json
{
  "intervalSeconds": 300,
  "repos": [
    {
      "folder": "C:\\path\\to\\repo",
      "gitUrl": "https://github.com/org/repo.git",
      "branch": "develop",
      "indexName": "my-repo-develop",
      "enabled": true
    }
  ]
}
```
### Index Builder

Pre-compute passage embeddings for all indexes using ONNX Runtime (DirectML GPU or CPU fallback).

```powershell
# Build all indexes
leann-dotnet.exe --build-indexes

# Rebuild all (even if embeddings already exist)
leann-dotnet.exe --build-indexes --force

# Build one specific index
leann-dotnet.exe --build-indexes --index api-server-release-25.2

# Custom batch size (default: 32, lower for less GPU memory)
leann-dotnet.exe --build-indexes --batch-size 16

# Show help
leann-dotnet.exe --help
```

**Flags:**

| Flag | Description | Default |
|------|-------------|---------|
| `--build-indexes` | Run index builder mode | — |
| `--force` | Rebuild even if embeddings exist | false |
| `--index NAME` | Build only this index | all |
| `--batch-size N` | Passages per ONNX inference batch | 32 |
| `--help` | Show usage | — |

**Important:** Run from the shared drive root so it can find `.leann/indexes/` and `.leann/models/`:

```powershell
cd Z:\
C:\path\to\leann-dotnet.exe --build-indexes
```

## Architecture

```
VS Code / Claude → leann-dotnet.exe → ONNX Runtime (DirectML/CPU) → flat cosine search → passages
```

### Components

| Component | Technology | Purpose |
|-----------|-----------|---------|
| MCP Server | [ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk) v1.1.0 | stdio JSON-RPC transport |
| Embedding | ONNX Runtime + DirectML | facebook/contriever model inference |
| Tokenizer | Microsoft.ML.Tokenizers | bert-base-uncased WordPiece tokenizer |
| Vector Search | Flat cosine similarity | SIMD-accelerated brute-force search |
| Passages | JSONL | In-memory passage store |

### MCP Tools

- **`leann_warmup`** — Pre-load embedding model into GPU memory
- **`leann_search`** — Semantic search across indexed codebases
- **`leann_list`** — List available indexes

### File Layout on Shared Drive

```
Z:\
├── .leann/
│   ├── bin/
│   │   └── leann-dotnet.exe          # Published exe
│   ├── models/
│   │   └── contriever-onnx/          # ONNX model + tokenizer
│   │       ├── model.onnx
│   │       ├── vocab.txt
│   │       └── tokenizer_config.json
│   └── indexes/
│       ├── api-server-release-25.2/
│       │   ├── documents.leann.meta.json
│       │   ├── documents.leann.passages.jsonl
│       │   ├── documents.ids.txt
│       │   ├── documents.embeddings.bin    # Pre-computed, L2-normalized
│       │   └── documents.embeddings.meta.json
│       └── ...  (27 indexes)
├── api-server-release-25.2/              # Source repos
└── ...
```

## Building from Source

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download)

```powershell
cd tools/leann-dotnet

# Build
dotnet build

# Run (from the shared drive root)
cd Z:\
dotnet run --project C:\path\to\tools\leann-dotnet\src\LeannMcp

# Publish single-file exe
dotnet publish src/LeannMcp -c Release
# Output: src/LeannMcp/bin/Release/net10.0/win-x64/publish/leann-dotnet.exe
```

## Maintainer Tasks

### 1. Export ONNX Model (one-time, requires WSL + uv)

```bash
# From WSL
cd /mnt/c/rag-chunks-by-section/tools/leann-dotnet/scripts
uv run --with "torch==2.5.1" --with "transformers<4.48" --with onnxruntime --with numpy --with onnx \
    python export-onnx-simple.py /mnt/z/.leann/models/contriever-onnx
```

### 2. Chunk and Build a Single Repo (native .NET, no Python needed)

```powershell
cd Z:\
leann-dotnet.exe --rebuild --docs C:\path\to\my-repo --index-name my-repo
```

### 3. Build All Index Embeddings

```powershell
cd Z:\
leann-dotnet.exe --build-indexes --force
```

This uses ONNX Runtime + DirectML to compute embeddings for all 27 indexes.
On GPU: ~176 passages/second (after JIT warm-up). On CPU: slower but works.

### 4. Publish and Deploy

```powershell
cd C:\path\to\tools\leann-dotnet
dotnet publish src/LeannMcp -c Release
Copy-Item src\LeannMcp\bin\Release\net10.0\win-x64\publish\leann-dotnet.exe Z:\.leann\bin\
```


### 5. Run the Auto-Sync Watcher

```powershell
cd Z:\
leann-dotnet.exe --watch --interval 300
```

This monitors all 26 repos configured in `.leann/repos.json`. When a repo has new commits on its tracked branch, the watcher automatically pulls changes and rebuilds the passage + embedding index. Edit `repos.json` to add/remove repos or change branches.

### Cross-Platform Publishing

```powershell
# Build for both platforms
.\publish-all.ps1

# Build and deploy to shared drive
.\publish-all.ps1 -DeployToSharedDrive

# Or manually:
dotnet publish src/LeannMcp -c Release -r win-x64 -o publish/win-x64
dotnet publish src/LeannMcp -c Release -r osx-arm64 -o publish/osx-arm64
```

| Platform | EP | GPU Support | Binary |
|----------|------|-------------|--------|
| Windows x64 | DirectML | NVIDIA, AMD, Intel Arc | `leann-dotnet.exe` |
| macOS ARM64 | CoreML | Apple Silicon GPU + Neural Engine | `leann-dotnet` |
## Troubleshooting

| Problem | Solution |
|---------|----------|
| "No ONNX model found" | Run the export script (see Maintainer Tasks #1) |
| "Pre-computed embeddings not found" | Run `leann-dotnet.exe --build-indexes` |
| "DirectML not available" | Falls back to CPU automatically. Ensure GPU drivers are updated. |
| Slow first search | Call `leann_warmup` first to pre-load the model. |
| "Index not found" | Run `leann_list` to see available indexes. Check `cwd` in mcp.json. |
| Out of GPU memory | Use `--batch-size 8` or lower when building indexes. |

## Comparison: Python vs .NET

| Aspect | Python (WSL) | .NET (Native) |
|--------|-------------|---------------|
| Setup | WSL + Python + cifs + fstab | `net use Z:` + copy mcp.json |
| GPU | CUDA only (NVIDIA) | DirectML (NVIDIA, AMD, Intel) |
| Cold start | ~30s (PyTorch load) | ~5s (ONNX session) |
| Per-search | ~200ms | ~50ms (SIMD flat search) |
| Distribution | WSL image | Single 113MB exe |
| Index build | Python script | `--build-indexes` (native) |
| Chunking | Python (leann build) | `--build-passages` (native) |
