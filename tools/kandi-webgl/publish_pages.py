#!/usr/bin/env python3
"""Publish only a real Unity WebGL build. Refuse empty/placeholder builds."""
from __future__ import annotations
import argparse
from pathlib import Path
import json
import shutil
import html


def create_pages(build: Path, output: Path, receipt: Path) -> Path:
    build = build.resolve()
    output = output.resolve()
    if not (build / "index.html").is_file() or not (build / "Build").is_dir():
        raise FileNotFoundError("Unity WebGL player index and Build payload required; no placeholder deployments")
    files = [p.name for p in (build / "Build").iterdir() if p.is_file()]
    if not any(".wasm" in f for f in files) or not any(".data" in f for f in files) or not any(".loader.js" in f for f in files):
        raise ValueError("Real Unity WebGL WASM/data/loader payload not found; refusing deployment")
    if output.exists():
        raise FileExistsError("Refusing to overwrite an existing Pages output")
    metadata = json.loads(receipt.read_text(encoding="utf-8"))
    if metadata.get("schema") != "danceflow.unity.kandi-asset-receipt.v1" or metadata.get("bone_count") != 24:
        raise ValueError("Verified source character audit receipt required")
    output.mkdir(parents=True)
    shutil.copytree(build, output / "kandi")
    (output / ".nojekyll").write_text("", encoding="utf-8")
    source_commit = html.escape(metadata["source_commit"])
    page = f'''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>DanceFlow.Unity — Kandi Boom WebGL</title>
<style>body{{font-family:system-ui,sans-serif; background:#121625;color:#f6f7fb;max-width:850px;margin:auto;padding:2rem;line-height:1.65}}a{{color:#76dcfa}}.lead{{font-size:1.2rem}}.feature{{padding:1.5rem;background:#242b44;border-radius:1rem;margin-top:2rem}}.launch{{display:inline-block;padding:.75rem 1.4rem;background:#79dced;color:#071220;border-radius:.6rem;font-weight:bold;text-decoration:none}}code{{word-break:break-all}}</style>
</head><body><h1>DanceFlow.Unity</h1><p class="lead">Kandi Boom · Bass Ben &amp; Friends</p>
<p>This is a <strong>real Unity WebGL build</strong> featuring the original, hash-verified 24-bone Kandi Boom FBX and PBR textures from DanceRudiments.</p>
<p><a class="launch" href="./kandi/">Launch Kandi Boom (Unity WebGL)</a></p>
<div class="feature"><h2>What the demo shows</h2><p>Native character skinning with source-verified Kandi Boom assets; shared DanceFlow musical clock; BPM-driven procedural character movement; motion-reactive terrain, trees, grass and shader effects.</p>
<p><strong>Motion provenance:</strong> movement is a synthetic beat preview, not the CMU salsa or ballet source recordings. No Bass Ben &amp; Friends audio is bundled.</p></div>
<p>Original source: <a href="https://github.com/kieransimkin/DanceRudiments/tree/{source_commit}/harness/kandi-boom">DanceRudiments / Kandi Boom</a>.
<a href="https://github.com/kieransimkin/DanceFlow.Unity">Unity package source</a>.</p>
<p>Audit: original FBX and four textures SHA-256 verified during CI; manifest <a href="./kandi-source-receipt.json">asset receipt</a>.</p>
</body></html>'''
    (output / "index.html").write_text(page, encoding="utf-8")
    shutil.copy2(receipt, output / "kandi-source-receipt.json")
    return output


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args()
    print("Verified Unity WebGL Pages payload:", create_pages(args.build, args.output, args.receipt))

if __name__ == "__main__":
    main()
