# Changelog

## [2.4.0] — Per-index embedding model selection

### Added
- **Per-index embedding model.** Each index now records its embedding model in its manifest, and the MCP server reads that manifest at load time to embed queries with the correct model. A single MCP server process can now serve multiple indexes built with different models — e.g. a code repo indexed with `jinaai/jina-embeddings-v2-base-code` and a PDF manual indexed with `facebook/contriever` — without any environment-variable juggling.
- **`IEmbeddingServiceFactory` abstraction.** Embedding services are now created (and cached) per-model via `OnnxEmbeddingServiceFactory`, keyed by the model id. Adding a new model requires only registering a descriptor in `ModelRegistry`; no DI plumbing changes.
- **Per-index `IEmbeddingService` instances.** `LeannIndex` now carries its own `IEmbeddingService` and `EmbeddingModelDescriptor`, so search routing is correct by construction — `IndexManager.Search` dispatches the query through the index's own embedding service, not a global singleton.

### Changed
- `IndexCompatibility` no longer fails on a model-id mismatch between the active environment and an index manifest. The previous "refusing index … built with `<other-model>`" error is gone — that scenario now just loads the right model on demand. The compatibility check is reduced to its still-meaningful invariant: dimensions in the manifest must match the resolved descriptor.
- `LEANN_MODEL` semantics narrowed: it now governs only (a) the default model used by `--build-passages`/`--build-indexes`/`--rebuild` when no `--model` flag is given, and (b) the warmup model on MCP server startup. It is **no longer** a per-query override — query-time model selection is automatic per index.
- `IndexManager` constructors now take `IEmbeddingServiceFactory` instead of `IEmbeddingService`. Build-time hosts (`--watch`, `--build-indexes`) are unchanged: those processes still use exactly one model per invocation, registered as a singleton.

### Removed
- The `LEANN_MODEL=<id>` workaround for querying mixed-model workspaces. It is no longer needed and the documentation that mentioned it has been removed.



### Added
- **PDF indexing.** `.pdf` files are now indexed alongside source code and Markdown via a new `IDocumentReader` abstraction backed by `PdfDocumentReader` (UglyToad.PdfPig, pure managed .NET). Pages are joined with `\n\n--- Page N ---\n\n` markers so chunks naturally split on page boundaries and search results stay citeable to a specific page.
- **`source_type` passage metadata.** Every passage now emits `source_type` (`"pdf"` or `"text"`) so MCP search consumers can filter or distinguish PDF hits from code/Markdown.
- **`IDocumentReader` extension point.** Adding new file formats (DOCX, EPUB, etc.) is now a closed change: implement `IDocumentReader.CanHandle/Read` and register it in DI — `FileDiscoveryService` requires no further edits.

### Changed
- `.pdf` removed from the `BinaryExtensions` deny-list and added to `FileExtensions.TextExtensions`.
- `FileDiscoveryService.TryLoadDocument` no longer calls `File.ReadAllText` directly; reader selection is dispatched through the registered `IEnumerable<IDocumentReader>` (most-specific reader wins; `PlainTextReader` is the fallback).

### Limitations
- Scanned / image-only PDFs are NOT supported (no OCR). Encrypted and corrupt PDFs are skipped with a `warn`-level log; the build continues.

## [Unreleased]

### Added
- **Workspace auto-detection for MCP server mode.** A single global server
  registration now resolves its data directory automatically — no per-project
  `cwd` field required in `mcp.json`. Resolution priority:
  `LEANN_DATA_ROOT` env var > MCP client `roots` (via
  `RequestRootsAsync`) > `Directory.GetCurrentDirectory()`. The active
  workspace is re-resolved on every tool call, and `IndexManager`'s in-memory
  cache is invalidated when the resolved path changes (so switching VS Code
  workspaces hot-swaps indexes without a restart). See
  [`docs/workspace-roots-design.md`](docs/workspace-roots-design.md).

### Changed
- `mcp.json.example` no longer requires `"cwd"`. The same global entry now
  works across every workspace.

## 1.0.16 — Jina code-aware embeddings (default model change)

### BREAKING
- **Default embedding model changed** from `facebook/contriever` (768-d, English-prose) to
  `jinaai/jina-embeddings-v2-base-code` (768-d, code-aware, 30 programming languages, 8192 max
  sequence length). New indexes are built with jina by default.
- **Existing indexes are refused at load time** by the new model-compatibility guard.
  The server logs `IndexCompatibility: refusing index ...` and returns no results until you
  either rebuild the index with the active model or set `LEANN_MODEL=facebook/contriever`
  to keep using the model that originally built it.
- The `LEANN_MODEL_DIR` default has changed. The model directory now uses the sanitized
  model id under `~/.leann/models/` (e.g.
  `~/.leann/models/jinaai-jina-embeddings-v2-base-code/`) instead of the hard-coded
  `~/.leann/models/contriever-onnx/`. Set `LEANN_MODEL_DIR` explicitly only if you need to
  override this.

### Added
- `--model <id>` flag on `--setup`, `--build-passages`, `--rebuild`, and `--watch`.
  `--build-indexes` embeds each index with the model recorded in that index's manifest.
  Supported ids today: `jinaai/jina-embeddings-v2-base-code` (default) and
  `facebook/contriever`.
- `LEANN_MODEL` environment variable equivalent to `--model`.
- `EmbeddingModelDescriptor` + `ModelRegistry` (`src/LeannMcp/Models/`): single source of
  truth for model id, dimensions, tokenizer type, download URL, and SHA256.
- `RobertaBpeTokenizerFactory` (jina) and `WordPieceTokenizerFactory` (contriever) — both
  registered as `ITokenizerFactory` and selected by descriptor at runtime.
- SHA256 verification + `.sha256.ok` idempotency marker in `ModelDownloader`. Re-running
  `leann-dotnet --setup` is a no-op once the marker exists; `--force` re-downloads.
- `IndexCompatibility` guard in `IndexManager` — refuses cross-model index loads with a
  clear log message.
- Tests:
  - `RobertaBpeTokenizerFactoryTests` — smoke test on `def hello_world(): pass` validating
    `<s>` BOS, `</s>` EOS, no `<unk>`, byte-level BPE invariants.
  - `IndexManagerModelGuardTests` — 6 tests covering model match / mismatch / legacy-meta /
    dimension mismatch.
  - `IndexMetadataDescriptorTests` — theory test asserting both jina and contriever
    descriptors round-trip through `documents.leann.meta.json`.

### Changed
- `OnnxEmbeddingService` now takes an `EmbeddingModelDescriptor` + `IEnumerable<ITokenizerFactory>`
  instead of hard-coded contriever wiring. Tokenizer is selected by
  `descriptor.TokenizerType.ToString()`.
- `PassageWriter` now writes `embedding_model` and `dimensions` from the descriptor instead
  of a hard-coded `"facebook/contriever"`.
- `ModelDownloader` is now descriptor-aware (`DownloadModelAsync(descriptor, modelDir, ct)`)
  and verifies SHA256 before marking the install complete.
- `Program.cs` registers `EmbeddingModelDescriptor` in all four hosting modes
  (RunMcpServer / RunWatch / RunBuildPassages / RunBuildIndexes).

### Quality validation (T18)
Built a full index of `C:\OnBase.NET` (40,133 files → 801,078 passages, 768d, 44 min on
NVIDIA RTX PRO 1000 / DirectML) and ran 5 baseline queries. Top-10 relevance vs the
contriever baseline (Q1 was 1/10 with contriever):

| Query | Jina top-10 relevance |
|-------|----------------------|
| "how does document scanning work" | **7-8 / 10** (RescanProcess, ScanCommand, ScanAndSweepStorage) |
| "OCR text extraction" | 6-7 / 10 (OmniPageEngine, OCRWorker, OcrWorkerManager) |
| "workflow approval logic" | 3-4 / 10 (WorkflowSOAProvider, Workflow.Cca/LifeCycleAnalyzer) |
| "PDF rendering" | 2-3 / 10 (mostly Web.config — likely reflects sparse PDF rendering code in OnBase) |
| "user authentication and login" | 1-2 / 10 (returned FullText files — OnBase delegates auth externally) |

The dramatic Q1 jump (1 → 7-8) is the headline validation. Q4/Q5 low scores plausibly
reflect content absence, not embedding quality.

### Migration

```bash
dotnet tool update -g leann-dotnet
leann-dotnet --setup                                          # downloads jina
leann-dotnet --rebuild --docs <repo> --index-name <name>     # rebuild each index
```

To stay on contriever:

```bash
$env:LEANN_MODEL = "facebook/contriever"   # PowerShell
# or
export LEANN_MODEL=facebook/contriever      # bash
```
