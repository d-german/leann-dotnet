# Changelog

## 1.0.16 — Jina code-aware embeddings (default model change)

### BREAKING
- **Default embedding model changed** from `facebook/contriever` (768-d, English-prose) to
  `jinaai/jina-embeddings-v2-base-code` (768-d, code-aware, 30 programming languages, 8192 max
  sequence length). New indexes are built with jina by default.
- **Existing indexes are refused at load time** by the new model-compatibility guard.
  The server logs `IndexCompatibility: refusing index ...` and returns no results until you
  either rebuild the index with the active model or set `LEANN_MODEL=facebook/contriever`
  to keep using the model that originally built it.
- The `LEANN_MODEL_DIR` default has changed. The model directory is now derived from
  `LEANN_DATA_ROOT` + the sanitized model id (e.g.
  `<LEANN_DATA_ROOT>/models/jinaai-jina-embeddings-v2-base-code/`) instead of the hard-coded
  `~/.leann/models/contriever-onnx/`. Set `LEANN_MODEL_DIR` explicitly only if you need to
  override this.

### Added
- `--model <id>` flag on `--setup`, `--build-passages`, `--build-indexes`, `--rebuild`, and
  `--watch`. Supported ids today: `jinaai/jina-embeddings-v2-base-code` (default) and
  `facebook/contriever`.
- `LEANN_MODEL` environment variable equivalent to `--model`.
- `EmbeddingModelDescriptor` + `ModelRegistry` (`src/LeannMcp/Models/`): single source of
  truth for model id, dimensions, tokenizer type, download URL, and SHA256.
- `RobertaBpeTokenizerFactory` (jina) and `WordPieceTokenizerFactory` (contriever) — both
  registered as `ITokenizerFactory` and selected by descriptor at runtime.
- SHA256 verification + `.sha256.ok` idempotency marker in `ModelDownloader`. Re-running
  `leann-mcp --setup` is a no-op once the marker exists; `--force` re-downloads.
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
leann-mcp --setup                                          # downloads jina
leann-mcp --rebuild --docs <repo> --index-name <name>     # rebuild each index
```

To stay on contriever:

```bash
$env:LEANN_MODEL = "facebook/contriever"   # PowerShell
# or
export LEANN_MODEL=facebook/contriever      # bash
```
