# Changelog

## [2.9.0] - 2026-10-07

Rebuild C# indexes after upgrading (`--rebuild`): passages are shaped differently.

### Changed
- **C# passages are packed and split to fit the embedding window.** The Roslyn chunker
  emitted one passage per member, so half of all C# passages were a single field or
  property, each costing a full embedding, while long methods stayed one passage whose
  text past the model's 512-token window was never embedded (29% of all code in a 792-file
  OnBase project). Consecutive members of one type are now packed into a passage up to
  `--code-chunk-size`, longer members are split at line boundaries into overlapping parts
  labelled with the member, and members of different types are never mixed.
- **`--code-chunk-size` now defaults to 1536 and `--code-chunk-overlap` to 192** (12.5%),
  and the C# chunker honours them; it previously ignored both. The default lives in one
  place (`ChunkingOptions.DefaultCodeChunkSize`) for the CLI, the record and `--watch`.
- **Code that only .NET Framework compiles is chunked.** C# files whose conditionals test
  target frameworks are parsed as modern .NET and as .NET Framework 4.8, so a file wrapped
  in `#if NETFRAMEWORK` yields member passages instead of one raw blob.

  Measured: an OnBase project went from 10,378 to 5,448 passages and from 40 MB to 24 MB
  with no code left unembedded, at the same embedding time; two HCW repositories shrank by
  53% and 57%. Retrieval held or improved on every question tested, and a blind-graded
  troubleshooting benchmark on HCW 25.2 scored 84 against 83 with the previous indexes.
  The non-code share of code indexes rose from 8.4% to 14.2%.

### Also in this release (2.8.x, previously unrecorded)
- Text chunks default to 1024 characters with 128 overlap, so prose and configuration no
  longer out-vote code.
- `--build-indexes` and `--rebuild` honour `--data-root` instead of writing under the
  working directory.
- The ONNX session falls back to a lower graph optimization level when a fusion fails
  (macOS arm64), instead of leaving semantic search unavailable.

## [2.7.1] - 2026-08-18

### Added
- **Diagram-as-code files are indexed**: `.puml`, `.plantuml`, `.iuml`, `.mmd`,
  `.mermaid`, `.dot`, `.gv`. These are plain text describing how services fit
  together, which answers architecture questions better than most source files.
  Previously a repository of PlantUML diagrams indexed almost nothing.

## [2.7.0] - 2026-08-18

### Fixed
- **Bracket expressions in ignore patterns.** The glob matcher handled only `*`,
  `**` and `?`, so `[Oo]bj/` was compared as the literal characters `[`, `O`, `o`,
  `]` and never matched. Since that is the pattern the stock Visual Studio
  `.gitignore` ships, every .NET repository was indexing its own build output.
  `[...]` now supports members, `a-z` ranges, `!`/`^` negation, a leading `]` as a
  literal member, and falls back to literal matching for an unmatched `[`.
- **`.leannignore` was never read.** Only `.gitignore` was loaded. In-tree
  `.leannignore` files are now honoured at every directory level, so an ignore
  list written for search does not have to be smuggled into version control rules.

### Added
- **`--ignore-file PATH`** applies a workspace-wide ignore list to every indexed
  repository, defaulting to `<data-root>/.leannignore` when present. Patterns are
  repo-relative and take precedence over in-tree rules, so a multi-repo workspace
  excludes noise once instead of editing each repository. Also threaded through
  watch mode.

### Changed
- **An `LEANN_MODEL` override is now reported on stderr.** The registry default is
  the code model, but a persistent environment variable could silently redirect
  every build to a general-purpose text model. Because both models are 768
  dimensions, the index compatibility guard could not detect it and indexes were
  internally consistent while being uniformly wrong. Overrides and unknown model
  ids now announce themselves at the point where indexes are built.

## [2.5.7]

### Fixed
- **PDF parsing failures are isolated to the affected file**, including failures raised
  lazily while materializing pages and fonts, so one malformed document no longer aborts
  an index build.
- **File discovery and the repository watch loop isolate unexpected failures**, so the
  remaining files and repositories continue processing.
- **Watch-mode pulls are fast-forward-only.** Diverged or dirty checkouts are reported
  instead of creating an implicit merge or indexing an unsynchronized checkout.

## [2.6.0] - 2026-08-18

### Added
- **Search without an MCP client.** Three new CLI modes expose the existing
  search engine directly, so callers that cannot run an MCP server (agent
  skills, scripts, CI) get the same results:
  - `--list` prints available index names. Never loads a model, so it is instant.
  - `--search --index NAME --query TEXT` runs a one-shot search. Supports
    `--top-k`, `--complexity`, `--dedup-threshold`, `--show-metadata` and `--json`.
  - `--serve` hosts a resident localhost HTTP daemon on port 57391 (`--port` to
    override) with routes `/health`, `/list`, `/search`, `/warmup` and `/shutdown`.
    Add `format=json` for machine-readable output.
- **`--indexes-dir` / `--data-root`** pin the index location for the three modes
  above, independent of `LEANN_DATA_ROOT` and the working directory.

### Why --serve exists
`IndexManager` caches each loaded index together with its ONNX session, so the
cost of model load is paid per process, not per search. A one-shot `--search`
therefore pays it every time (measured 5.0s), while a warm daemon answers in
0.2 to 0.3s. That warmth was previously reachable only by keeping the MCP server
resident; `--serve` provides it over plain HTTP.

`HttpListener` was chosen over Kestrel deliberately: it is in the BCL, so the
packaged dotnet tool remains a plain console app with no `FrameworkReference` on
`Microsoft.AspNetCore.App` and therefore no ASP.NET Core runtime prerequisite.
Binding `http://localhost:PORT/` requires neither elevation nor a netsh URL
reservation.

### Changed
- Search result rendering moved to a shared `SearchResultFormatter`. The MCP
  `leann_search` tool now delegates to it, so MCP, CLI and daemon output cannot
  drift apart. Empty result sets now report `No results for '<query>'.` instead
  of a `(top 0)` header.

## [2.5.6] — 2026-05-08

### Added
- **Indentation-aware chunker for Python** — new `IndentationChunker` detects
  top-level blocks by tracking indentation changes (column 0 → indented → back
  to column 0). Keeps complete `def`/`class` bodies together, groups decorators
  with their targets, and handles triple-quoted strings. Oversized blocks are
  split at dedent points. Previously Python files fell back to the legacy
  line-numbered sliding-window chunker, which could split functions mid-body.

## [2.5.5] — 2026-04-26

### Fixed
- **Mixed-model rebuild corruption** — `--build-indexes` now embeds each
  index with the model recorded in its manifest via the new
  `IEmbeddingServiceFactory` injection into `IndexBuilder`. Previously
  the active descriptor was used for every index regardless of manifest,
  silently overwriting embeddings in mixed-model workspaces (the
  dimension-only integrity check could not detect 768-vs-768 collisions).
- **`IndexCompatibility`** now also validates `model_id` in
  `documents.embeddings.meta.json` against the resolved descriptor,
  closing the same-dimension loophole.
- **`.gitignore` scoping** — `GitIgnoreFilter.LoadFromFile` now uses
  `baseDir` to anchor patterns to each `.gitignore` file's directory
  instead of rewriting bare patterns as `**/pattern` globally.
  `FileDiscoveryService.LoadGitIgnoreRecursive` now genuinely recurses
  past depth 1.
- **Concurrent ONNX session leak** — `OnnxEmbeddingServiceFactory.GetOrCreate`
  uses a per-model lock with double-checked TryGetValue so two simultaneous
  first-load requests no longer construct (and silently discard) duplicate
  sessions.
- **Stale `ids.txt` no longer becomes empty embeddings** — `IndexBuilder`
  now fails fast on count mismatches, duplicate IDs, or IDs missing from
  the passage store, instead of writing zero-vectors for `""`.
- **`complexity` MCP parameter** is now actually applied: it flows through
  `IndexManager.Search` → `ComputeFetchK` as
  `clamp(complexity, topK, 1000)` and sets the candidate depth used
  before BM25 fusion and de-duplication.
- `scripts/install-local.ps1` verifies `leann-dotnet --help` instead of
  the stale `leann-mcp --help`.

### Changed
- `Program.cs` centralizes `--model` parsing in `ResolveDescriptor` and
  wires it through `--setup`, `--build-passages`, and `--watch`
  (previously only `--setup` honored `--model`).
- `RunWatch` now returns `Task<int>` so descriptor-resolution failures
  exit non-zero.

### Added
- 7 new tests (`FileDiscoveryServiceGitIgnoreTests`, `IndexBuilderTests`,
  embeddings-meta mismatch, concurrent factory load). Total: 140 passing.
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
Built a full index of a large .NET monorepo (40,133 files → 801,078 passages, 768d, 44 min on
NVIDIA RTX PRO 1000 / DirectML) and ran 5 baseline queries. Top-10 relevance vs the
contriever baseline (Q1 was 1/10 with contriever):

| Query | Jina top-10 relevance |
|-------|----------------------|
| "how does document scanning work" | **7-8 / 10** (RescanProcess, ScanCommand, ScanAndSweepStorage) |
| "OCR text extraction" | 6-7 / 10 (OmniPageEngine, OCRWorker, OcrWorkerManager) |
| "workflow approval logic" | 3-4 / 10 (WorkflowSOAProvider, Workflow.Cca/LifeCycleAnalyzer) |
| "PDF rendering" | 2-3 / 10 (mostly Web.config — likely reflects sparse PDF rendering code in that repo) |
| "user authentication and login" | 1-2 / 10 (returned FullText files — that product delegates auth externally) |

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
