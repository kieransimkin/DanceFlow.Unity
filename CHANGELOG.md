# Changelog

## 0.5.0-preview.1 (source-only, not released) — Stage 4 rig playback foundation
- Canonical DanceRudiments Rigify/custom rig-profile and rig-motion import, exact SHA fingerprint, ordered channel identity and physical-value decoding.
- Native 64-pip rig poses, step controls, shortest-path quaternion SLERP, bounded crossfades, reviewed skeleton mappings, root-motion policy and transform/Playable backends.
- Opt-in two-skinned-character synthetic Unity sample and SHA-pinned optional Kandi Boom target audit (no bundled FBX/audio/CMU).
- Unit and source-parity fixture checks; Unity Editor / GPU hardware / real character retargeting acceptance remains outstanding.

## 0.4.0 (work in progress, unreleased) — Phase 3 complete motion layer
- Completed the Phase 3 recording subsystem with strict canonical DanceRudiments JSON import, lossless DFR1 float64 cache generation, exact/linear bounded sampling and explicit recording-to-master time mapping.
- Added typed motion adapters for Transform local position, Light intensity, ParticleSystem emission and Renderer float/colour properties.
- Added a global four-slot motion signal bus plus optional bounded GPU recording mirror and shader-side sampling helpers for render effects.
- Added the Song Reactive Landscape showcase sample: procedural trees, grass, stars, terrain ripples, palette-shift and geometry-warp effects, with manifests/presets for StemLab example songs and Bass Ben & Friends' Star Lights Glow.
- Added static showcase validation, runtime/editor tests and recording verification tools.
- Unity Editor, GPU and hardware-run acceptance remain pending before final release sign-off.

## 0.3.0 (unreleased) — Phase 2 shader clock implementation
- Finalisation: use a matched URP Unlit baseline rather than URP Lit, and ensure all benchmark objects have unique visible XY positions at high counts.
- Restore VSync and target frame rate after a local benchmark session; add matching-shader static contract checks.
- Extended opt-in profiling with bounded raw observations, percentile summaries, GC/Unity video-memory counters and unavailable-value reporting.
- Added a generated URP benchmark scene, six paired A/B/C/D/M scenarios, real GPU readback acceptance test, hardware runner scripts, Python analysis and manual GPU CI workflow.
- Performance evidence is still pending real Unity Editor and GPU execution; synthetic analyser tests are not performance measurements.
- Added authoritative render-frame global shader bridge and deterministic Editor/offline publishing.
- Added float-precision-safe C# encoding, HLSL helpers and Shader Graph File-mode custom functions.
- Added opt-in URP vertex/fragment demo generator, shader diagnostics and bounded opt-in performance observations.
- Added Unity NUnit fixtures, managed .NET parity tests and dependency-free shader contract checks.
- Unity GPU compilation, licensed Editor tests and hardware benchmark evidence remain pending before release sign-off.

## 0.2.0 (unreleased) — Phase 1 managed musical transport
- Implemented deterministic managed fixed-BPM and strict StemLab variable-tempo beat maps, audio identity validation and signed tick/pip phase.
- Implemented manual/DSP clock, excerpt-aware transport with seek, pause, rate, loops, meter, epoch generation and bounded beat/bar/phrase event scheduling and inert source-time cues.
- Added Unity clock component, diagnostics, tests and source-level standalone .NET 8 parity CI.
- Added upstream compatibility lock and precise clock contract documentation.
- Phase 2 shader bridge, Phase 3 recording playback and Phase 4 rigging remain unimplemented.

## 0.1.0 — planning skeleton
- Added comprehensive implementation plan and initial Unity Package Manager manifest.
- No runtime implementation in the original baseline.
