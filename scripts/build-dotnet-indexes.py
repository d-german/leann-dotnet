#!/usr/bin/env python3
"""Pre-compute passage embeddings for all LEANN indexes in .NET-readable format.

For each index, produces:
  - documents.embeddings.bin  (raw float32, N x 768, L2-normalized)
  - documents.embeddings.meta.json

Usage:
    python build-dotnet-indexes.py [--indexes-dir DIR] [--force] [--batch-size N]
"""

from __future__ import annotations

import argparse
import json
import struct
import sys
import time
from pathlib import Path

import numpy as np


def load_passages(jsonl_path: Path) -> list[dict]:
    """Load passages from JSONL, preserving order."""
    passages = []
    with open(jsonl_path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                passages.append(json.loads(line))
    return passages


def load_ids(ids_path: Path) -> list[str]:
    """Load document IDs from ids.txt."""
    with open(ids_path, encoding="utf-8") as f:
        return [line.rstrip("\n") for line in f if line.strip()]


def compute_passage_embeddings(
    texts: list[str],
    model_name: str = "facebook/contriever",
    batch_size: int = 48,
    device: str = "auto",
) -> np.ndarray:
    """Compute embeddings using sentence-transformers."""
    import torch
    from sentence_transformers import SentenceTransformer

    if device == "auto":
        if torch.cuda.is_available():
            device = "cuda"
        elif hasattr(torch.backends, "mps") and torch.backends.mps.is_available():
            device = "mps"
        else:
            device = "cpu"

    print(f"  Using device: {device}, batch_size: {batch_size}")
    model = SentenceTransformer(model_name, device=device)
    model.eval()

    with torch.inference_mode():
        embeddings = model.encode(
            texts,
            batch_size=batch_size,
            show_progress_bar=True,
            convert_to_numpy=True,
            normalize_embeddings=False,
        )

    return embeddings.astype(np.float32)


def l2_normalize(embeddings: np.ndarray) -> np.ndarray:
    """L2-normalize each row so cosine similarity = dot product."""
    norms = np.linalg.norm(embeddings, axis=1, keepdims=True)
    norms = np.maximum(norms, 1e-12)
    return embeddings / norms


def write_embeddings_bin(embeddings: np.ndarray, output_path: Path) -> None:
    """Write embeddings as raw float32 binary (row-major)."""
    embeddings.tofile(str(output_path))


def write_meta(count: int, dimensions: int, output_path: Path) -> None:
    """Write embeddings metadata JSON."""
    meta = {"count": count, "dimensions": dimensions, "normalized": True}
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)


def process_index(
    index_dir: Path,
    batch_size: int,
    force: bool,
    device: str,
) -> bool:
    """Process a single index. Returns True if successful."""
    name = index_dir.name
    meta_path = index_dir / "documents.leann.meta.json"

    if not meta_path.exists():
        return False

    emb_bin = index_dir / "documents.embeddings.bin"
    emb_meta = index_dir / "documents.embeddings.meta.json"

    if emb_bin.exists() and not force:
        print(f"  SKIP {name} (embeddings exist, use --force to rebuild)")
        return True

    # Load passages
    jsonl_path = index_dir / "documents.leann.passages.jsonl"
    ids_path = index_dir / "documents.ids.txt"

    if not jsonl_path.exists():
        print(f"  ERROR: {jsonl_path} not found", file=sys.stderr)
        return False

    passages = load_passages(jsonl_path)
    if not passages:
        print(f"  ERROR: No passages in {jsonl_path}", file=sys.stderr)
        return False

    # Build id -> text mapping
    passage_map = {p["id"]: p["text"] for p in passages}

    # Use ids.txt ordering if available, otherwise passage order
    if ids_path.exists():
        ids = load_ids(ids_path)
    else:
        ids = [p["id"] for p in passages]

    # Get texts in ID order
    texts = []
    valid_ids = []
    for doc_id in ids:
        if doc_id in passage_map:
            texts.append(passage_map[doc_id])
            valid_ids.append(doc_id)

    print(f"  {len(texts)} passages to embed")

    # Compute embeddings
    embeddings = compute_passage_embeddings(texts, batch_size=batch_size, device=device)

    # L2 normalize
    embeddings = l2_normalize(embeddings)

    # Validate
    norms = np.linalg.norm(embeddings, axis=1)
    assert np.allclose(norms, 1.0, atol=1e-5), f"Normalization failed: min={norms.min()}, max={norms.max()}"

    # Write output
    write_embeddings_bin(embeddings, emb_bin)
    write_meta(len(texts), embeddings.shape[1], emb_meta)

    expected_size = len(texts) * embeddings.shape[1] * 4
    actual_size = emb_bin.stat().st_size
    assert actual_size == expected_size, f"Size mismatch: {actual_size} != {expected_size}"

    print(f"  ✓ Wrote {emb_bin.name} ({actual_size:,} bytes) + {emb_meta.name}")
    return True


def main() -> None:
    parser = argparse.ArgumentParser(description="Pre-compute embeddings for .NET LEANN")
    parser.add_argument(
        "--indexes-dir",
        type=Path,
        default=Path.cwd() / ".leann" / "indexes",
        help="Path to .leann/indexes/ directory",
    )
    parser.add_argument("--force", action="store_true", help="Rebuild existing embeddings")
    parser.add_argument("--batch-size", type=int, default=48, help="Embedding batch size")
    parser.add_argument("--device", type=str, default="auto", help="Device: auto/cuda/mps/cpu")
    parser.add_argument("--index", type=str, default=None, help="Process single index by name")
    args = parser.parse_args()

    indexes_dir = args.indexes_dir
    if not indexes_dir.is_dir():
        print(f"ERROR: Indexes directory not found: {indexes_dir}", file=sys.stderr)
        sys.exit(1)

    if args.index:
        dirs = [indexes_dir / args.index]
    else:
        dirs = sorted(d for d in indexes_dir.iterdir() if d.is_dir())

    total = len(dirs)
    success = 0
    start = time.time()

    for i, index_dir in enumerate(dirs, 1):
        print(f"\n[{i}/{total}] Processing {index_dir.name} ...")
        try:
            if process_index(index_dir, args.batch_size, args.force, args.device):
                success += 1
        except Exception as e:
            print(f"  ERROR: {e}", file=sys.stderr)

    elapsed = time.time() - start
    print(f"\n{'='*60}")
    print(f"Done: {success}/{total} indexes processed in {elapsed:.1f}s")


if __name__ == "__main__":
    main()
