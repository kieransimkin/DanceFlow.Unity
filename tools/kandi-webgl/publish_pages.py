#!/usr/bin/env python3
"""Publish only a real Unity WebGL build. Refuse empty/placeholder builds."""
from __future__ import annotations
import argparse
from pathlib import Path
import json
import shutil
import html
import hashlib


def create_pages(build: Path, output: Path, receipt: Path, *, require_songs: bool = False) -> Path:
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
    if require_songs:
        songs = build / "StreamingAssets" / "Songs"
        if not (songs / "catalogue.json").is_file() or not (songs / "rudiments.json").is_file():
            raise FileNotFoundError("Real WebGL song catalogue and native rudiment pattern samples required")
        catalogue = json.loads((songs / "catalogue.json").read_text(encoding="utf-8"))
        rudiments = json.loads((songs / "rudiments.json").read_text(encoding="utf-8"))
        if catalogue.get("schema") != "danceflow.unity.webgl-song-gallery.v1" or len(catalogue.get("songs", [])) < 3:
            raise ValueError("Expected Arcadians, Truth is a Virus and Star Lights Glow song entries")
        if rudiments.get("schema") != "danceflow.unity.webgl-rudiments.v1" or len(rudiments.get("patterns", [])) < 4:
            raise ValueError("Expected official DanceRudiments catalogue samples")
        required_ids = {"arcadians", "the_truth_is_a_virus", "star_lights_glow_mystery_liquid_kick"}
        if {song.get("id") for song in catalogue["songs"]} != required_ids:
            raise ValueError("All three exact StemLab song identities are required")
        for song in catalogue["songs"]:
            for key in ("audio_file", "signals_file"):
                name = song[key]
                if not isinstance(name, str) or not name or name != Path(name).name:
                    raise ValueError("Unsafe song asset basename")
                if not (songs / name).is_file():
                    raise FileNotFoundError("Missing real public song asset: " + name)
            if hashlib.sha256((songs / song["audio_file"]).read_bytes()).hexdigest() != song["audio_sha256"]:
                raise ValueError("Public song MP3 has incorrect master SHA-256")
            features = json.loads((songs / song["signals_file"]).read_text(encoding="utf-8"))
            if features.get("source_sha256") != song["audio_sha256"] or features.get("frame_count", 0) < 2:
                raise ValueError("WebGL audio envelopes not bound to verified master")
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
<div class="feature"><h2>What the demo shows</h2><p>Listen to <strong>Arcadians</strong>, <strong>The Truth is a Virus</strong> and <strong>Star Lights Glow (Mystery Liquid Kick)</strong> via the Unity WebGL player. The original Kandi Boom character has twelve independently adjustable musical property bindings controlling grass, trees, terrain, lighting, palette, warp, arms, hips, head and knees.</p>
<p><strong>Motion provenance:</strong> the MP3s and measured sound envelopes are SHA-256 verified; actual DanceRudiments compiled pattern samples are separate candidates, <em>not</em> evidence of confirmed song matching. For <em>Star Lights Glow</em>, BPM and beat phase are not yet confirmed: the song plays at original speed while the procedural patterns run on an explicitly-labelled 120 BPM <strong>motion preview</strong>. The Anthem mix awaits its own master.</p></div>
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
    parser.add_argument("--require-songs", action="store_true")
    args = parser.parse_args()
    print("Verified Unity WebGL Pages payload:",
          create_pages(args.build, args.output, args.receipt, require_songs=args.require_songs))

if __name__ == "__main__":
    main()
