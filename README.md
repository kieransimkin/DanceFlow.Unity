# DanceFlow for Unity

**Status:** Phases 1–3 implemented in source; **Stage 4 preview** adds validated rigged-character playback. Final Unity Editor/GPU and real-character rigging acceptance remains outstanding.

UPM package: `com.kieransimkin.danceflow.unity` · Unity 6+ (`6000.0`). Current repository manifest is `0.5.0-preview.1` (not a published release tag).

[Implementation plan](docs/00-implementation-plan.md) · [Timing contracts](docs/02-clock-contract.md) · [GPU shader API](docs/03-shader-api.md) · [Recording format](docs/04-recording-format.md) · [Rigging guide](docs/06-rigging.md) · [Song showcase](docs/09-song-reactive-showcase.md)

## Install in Unity

In Package Manager choose **Add package from git URL**, enter:

```
https://github.com/kieransimkin/DanceFlow.Unity.git
```

For reproducible builds, pin a reviewed release tag or commit rather than using an unpinned branch. Rigged demos are opt-in under Package Manager → Samples; no heavyweight audio, external character FBX, model weights or CMU recordings ship with the runtime package.

The package declares its built-in Animation, Audio, JSON Serialization and Particle System module dependencies. They are required by rig playback, the DSP transport, metrics export and motion-driven particle emission even when a minimal consumer template omits them. URP remains an optional sample dependency. Test the complete root Git package in a fresh consumer project; copying selected runtime folders can hide missing package requirements.

## Features

- **Phase 1:** Fixed and source-verified StemLab variable BPM, native 16-tick/64-pip timing, audio DSP/manual transport, seeks, loops, excerpt source identity, cue scheduler.
- **Phase 2:** BPM, beat/bar/pip phases and pulses available to Unity vertex/fragment shaders via `DanceFlowShaderBridge` and `DanceFlowClock.hlsl`; deterministic GPU time packing and URP sample.
- **Phase 3:** Strict `dancerudiments.recording` source validation, byte-verified DFR1 float64 cache, explicit motion-source clocks, channel bindings, GPU recording windows and song-reactive procedural landscapes. Presets include StemLab examples and Bass Ben & Friends’ *Star Lights Glow* (no audio included).
- **Stage 4 preview:** Versioned `dancerudiments.rig-profile` and `rig-motion` validation; typed rig channel decoding; native pip pose sampling and quaternion interpolation; reviewed Unity skinned bone mappings; direct transform or Animation Playables job playback, blends and root translation policy.

## Stage 4 demo and import

Import **Rigged Character Demos** from Package Manager, then choose **Tools → DanceFlow → Stage 4 → Create Two Synthetic Skinned Rig Demos**. It builds two distinct SkinnedMeshRenderer robots with a silent 120-BPM synthetic clock. Kandi Boom/CMU clips require a separate verified target FBX, legal assets and explicit retargeting; see [rigging documentation](docs/06-rigging.md).

To import a compatible real rig motion, choose **Assets → DanceFlow → Import DanceRudiments Rig Motion…** and supply a canonical `.json`. The importer preserves profile SHA-256 and the native recording's independent clock. Playback refuses unreviewed skeletons and never loops captures automatically.

## Checks

```powershell
dotnet run --project tools/clock-checks/ClockChecks.csproj -c Release
dotnet run --project tools~/recording-checks/RecordingChecks.csproj -c Release
dotnet run --project tools~/rigging-checks/RiggingChecks.csproj -c Release
python tools~/rigging-checks/generate_fixtures.py --verify
python -m unittest discover -s tools~/rigging-checks -p test_*.py -v
python -m unittest discover -s tools~/showcase-checks -p test_*.py -v
```

Automated managed checks and static tests do not demonstrate actual Unity skinning, framebuffer rendering or audio-to-photon timing. Unity Editor tests, PlayMode and licensed GPU acceptance are separate release gates. Preserve the existing SHA-pinned recording fixtures and the repository’s `.gitattributes` rules; never normalize their bytes when hashing.
