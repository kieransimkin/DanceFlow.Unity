# Changelog

## 0.3.0 (unreleased) — Phase 2 shader clock implementation
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
