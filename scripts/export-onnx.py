#!/usr/bin/env python3
"""Export facebook/contriever to ONNX format for .NET ONNX Runtime inference.

This is a one-time export script. The resulting files are distributed via the
shared drive at .leann/models/contriever-onnx/.

Usage:
    python export-onnx.py [--output-dir OUTPUT_DIR] [--validate]
"""

from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path

import numpy as np
import torch


def export_model(output_dir: Path) -> None:
    """Export contriever to ONNX using optimum."""
    from optimum.exporters.onnx import main_export

    print(f"Exporting facebook/contriever to {output_dir} ...")
    main_export(
        "facebook/contriever",
        output=str(output_dir),
        task="feature-extraction",
        opset=17,
        device="cpu",
    )
    print("ONNX export complete.")


def copy_tokenizer_files(output_dir: Path) -> None:
    """Ensure tokenizer files are present alongside the ONNX model."""
    from transformers import AutoTokenizer

    tokenizer = AutoTokenizer.from_pretrained("facebook/contriever")
    tokenizer.save_pretrained(str(output_dir))

    required = ["vocab.txt", "tokenizer_config.json", "special_tokens_map.json"]
    for name in required:
        path = output_dir / name
        if not path.exists():
            print(f"WARNING: {name} not found in {output_dir}", file=sys.stderr)
        else:
            print(f"  ✓ {name}")


def validate(output_dir: Path) -> bool:
    """Validate ONNX model produces same embeddings as sentence-transformers."""
    import onnxruntime as ort
    from sentence_transformers import SentenceTransformer
    from transformers import AutoTokenizer

    test_texts = [
        "How does authentication work?",
        "Error handling patterns in C#",
        "Database connection setup",
    ]

    # Reference: sentence-transformers embeddings
    print("Computing reference embeddings with sentence-transformers ...")
    st_model = SentenceTransformer("facebook/contriever", device="cpu")
    st_model.eval()
    with torch.inference_mode():
        ref_embeddings = st_model.encode(test_texts, convert_to_numpy=True, normalize_embeddings=False)

    # ONNX: load model and tokenizer
    print("Computing ONNX embeddings ...")
    onnx_path = output_dir / "model.onnx"
    if not onnx_path.exists():
        onnx_path = next(output_dir.glob("*.onnx"), None)
        if onnx_path is None:
            print("ERROR: No .onnx file found", file=sys.stderr)
            return False

    session = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
    tokenizer = AutoTokenizer.from_pretrained(str(output_dir))

    encoded = tokenizer(test_texts, padding=True, truncation=True, max_length=512, return_tensors="np")
    input_ids = encoded["input_ids"].astype(np.int64)
    attention_mask = encoded["attention_mask"].astype(np.int64)

    inputs = {"input_ids": input_ids, "attention_mask": attention_mask}

    # Check if model needs token_type_ids
    input_names = [inp.name for inp in session.get_inputs()]
    if "token_type_ids" in input_names:
        inputs["token_type_ids"] = np.zeros_like(input_ids)

    outputs = session.run(None, inputs)
    last_hidden_state = outputs[0]  # (batch, seq_len, 768)

    # Mean pooling (matching sentence-transformers / contriever behavior)
    mask_expanded = attention_mask[:, :, np.newaxis].astype(np.float32)
    summed = (last_hidden_state * mask_expanded).sum(axis=1)
    counts = mask_expanded.sum(axis=1).clip(min=1)
    onnx_embeddings = summed / counts

    # Compare
    print("\nValidation results:")
    print(f"  Reference shape: {ref_embeddings.shape}")
    print(f"  ONNX shape:      {onnx_embeddings.shape}")

    all_pass = True
    for i, text in enumerate(test_texts):
        cos_sim = np.dot(ref_embeddings[i], onnx_embeddings[i]) / (
            np.linalg.norm(ref_embeddings[i]) * np.linalg.norm(onnx_embeddings[i])
        )
        status = "✓" if cos_sim > 0.999 else "✗"
        print(f"  {status} '{text[:40]}...' cosine_sim={cos_sim:.6f}")
        if cos_sim <= 0.999:
            all_pass = False

    return all_pass


def write_model_info(output_dir: Path) -> None:
    """Write a model-info.json for .NET consumption."""
    info = {
        "model_name": "facebook/contriever",
        "dimensions": 768,
        "max_length": 512,
        "tokenizer": "bert-base-uncased",
        "vocab_size": 30522,
        "inputs": ["input_ids", "attention_mask"],
        "output": "last_hidden_state",
        "post_processing": "mean_pooling",
    }
    info_path = output_dir / "model-info.json"
    with open(info_path, "w", encoding="utf-8") as f:
        json.dump(info, f, indent=2)
    print(f"  ✓ model-info.json")


def main() -> None:
    parser = argparse.ArgumentParser(description="Export contriever to ONNX")
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path(__file__).resolve().parent.parent / "models" / "contriever-onnx",
        help="Output directory for ONNX model",
    )
    parser.add_argument("--validate", action="store_true", help="Validate ONNX vs sentence-transformers")
    parser.add_argument("--skip-export", action="store_true", help="Skip export, only validate")
    args = parser.parse_args()

    output_dir = args.output_dir
    output_dir.mkdir(parents=True, exist_ok=True)

    if not args.skip_export:
        export_model(output_dir)

    copy_tokenizer_files(output_dir)
    write_model_info(output_dir)

    if args.validate:
        ok = validate(output_dir)
        if not ok:
            print("\nValidation FAILED!", file=sys.stderr)
            sys.exit(1)
        print("\nValidation PASSED!")

    print(f"\nONNX model ready at: {output_dir}")


if __name__ == "__main__":
    main()
