# DanceFlow.Unity — Phase 3 cumulative patch

**Published baseline:** `kieransimkin/DanceFlow.Unity` `main` commit `9e1a02fba4a81e7cf770cd0d5f18bd55b4c7e394` (22 Oct 2026).

The **one canonical cumulative patch** is `DanceFlow.Unity-Phase3-Cumulative.patch`. It adds the Phase 3 recording foundation, song-reactive samples, source-verified local song asset profiles, motion bindings and seek-safe 4 MiB GPU recording windows. It does not include proprietary songs/audio, copyright-restricted motion, Blender files, or model weights. The optional song samples remain under `Samples~/` until explicitly imported in Unity.

`Phase3-From-Foundation.patch` and `Phase3-From-Showcase.patch` are **compatibility deltas only** for users who already applied a previously supplied partial Phase 3 slice. The installer checks for the cleanly applicable variant, so users do not stack patches manually.

## Installation

Extract this ZIP, open a clean `DanceFlow.Unity` Git checkout, and from PowerShell run:

```powershell
& "<extracted bundle path>\Apply-Phase3.ps1" -RepositoryPath . -RunChecks
```

Or from Bash:

```bash
bash '/path/to/bundle/apply-phase3.sh' /path/to/DanceFlow.Unity --checks
```

A successful application leaves changes **uncommitted**, with no forced resets or overwrites. Run `git diff --check` and inspect before `git add -A && git commit`.

## Actual song integration

- Import exact local song master and full StemLab beat timeline where available, and import a matching native `dancerudiments.recording` via **Assets > DanceFlow**.
- In Unity, import **Song Reactive Landscape** sample from Package Manager; use **Tools > DanceFlow > Samples > Create Source-Verified Song Profile...**. This hashes original master bytes, validates the corrected map, and checks any asserted recording-provenance source hash.
- Select that profile and use **Tools > DanceFlow > Samples > Create Scene From Selected Song Profile**. The generated scene drives procedural trees, grass, fog, stars, palette and vertex warps from selected recorded channels.
- Star Lights Glow *Mystery Liquid Kick* and *Anthem* are separate profiles; neither has an invented BPM. Synthetic preview is labelled as unmatched.
- Run `python tools~/showcase-checks/prepare_song_assets.py --help` to audit `.json` and `.json.gz` recordings and output an evidence receipt without copying audio. Use `export_catalogue_match.py` only with the actual native DanceRudiments Python package and a separately measured candidate pattern.

## Verification boundary

Standalone Python fixtures and shader contract checks passed at patch preparation time. **.NET / Unity Editor compilation, ShaderLab compilation, real audio sync and GPU readback were not executed** here; the Unity and hardware workflows remain release gates. Version stays 0.3.0 until further acceptance; the Phase 3 release is not claimed.
