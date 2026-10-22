# DanceFlow.Unity — Comprehensive Implementation Plan

**Status:** Proposed architecture / implementation backlog  
**Document ID:** DFU-PLAN-001  
**Version:** 0.1.0  
**Date:** 2026-10-22  
**Package ID:** `com.kieransimkin.danceflow.unity`  
**Repository proposal:** `kieransimkin/DanceFlow.Unity`  
**Owner:** DanceFlow ecosystem  
**Canonical upstreams:** [DanceMoves](https://github.com/kieransimkin/DanceMoves), [FrameRudiments](https://github.com/kieransimkin/FrameRudiments), [DanceRudiments](https://github.com/kieransimkin/DanceRudiments), [StemLab](https://github.com/kieransimkin/stemlab), [DanceFlow](https://github.com/kieransimkin/DanceFlow)

> This document is the entry point for development. It defines the package contracts, implementation phases, acceptance gates, dependencies and risks. It is a plan, not a claim that the proposed components already exist. Before implementation, verify the latest stable upstream releases and source schemas against actual repository code and golden fixtures; README-level investigation alone is not sufficient for bit-for-bit compatibility.

## 1. Product objective

Create an installable Unity Package Manager (UPM) package that brings DanceFlow musical timing, procedural movement and N-dimensional motion recordings into Unity. A single authoritative musical transport drives CPU animation, GPU vertex/fragment/compute shaders, Visual Effect Graph, Shader Graph, Unity Animation Playables, Timeline and optional rigged characters. It must faithfully preserve the DanceMoves 16-tick clock, DanceRudiments 64-pip native sampling and StemLab variable-tempo beat anchors, including source-audio identity and transport seeks.

### Required use cases

1. Play music with beat/bar-aligned visualisation and animation at fixed or corrected variable BPM.
2. Expose beat count, local BPM, fractional beat/bar/tick/pip phase and beat pulses to shader stages.
3. Import, validate and sample `dancerudiments.recording` files, including all named N-dimensional channels.
4. Map motion channels to Transform, material, VFX, lights, procedural mesh deformation and custom user properties.
5. Drive compatible skinned rigs with Rigify-derived retargeting, animation blending and IK.
6. Render scenes deterministically at chosen source times, including editor scrubbing and offline output.
7. Stream optional per-song recording/rig packages without bloating the core runtime.
8. Discover compatible DanceFlow tools/assets using optional DanceFlow MCP integration, without requiring network access at runtime.

### Non-goals for initial release

- Implementing beat extraction or audio DSP already owned by StemLab.
- Replacing Unity Animator, SRP, Timeline or the DanceRudiments canonical sampling implementations.
- Automatically inferring trustworthy musical alignment for unannotated motion-capture files.
- Bundling heavy character meshes, copyrighted audio, model weights or motion data in the base UPM package.
- Claiming frame-perfect display-to-speaker synchronisation without platform-specific latency measurement.

## 2. Canonical compatibility contracts

| Source | Contract | Unity consequence |
| --- | --- | --- |
| DanceMoves | 16 musical ticks per quarter-note beat; 64 ticks per four-beat bar | Maintain `Tick16` as a derived index; meter must be configurable |
| DanceRudiments | 64 integer pips per quarter-note beat; looped procedural catalogue has source-specific period | `Pip64` exact sampling API; do not convert pip steps to Unity FPS |
| FrameRudiments | Native integer pip sampling and optional interpolated legacy compositor output | Provide explicit `Exact`, `Linear` and channel-aware interpolation modes |
| StemLab | `stemlab.beat-timeline.v1` corrected source-sample beat anchors, local tempo, downbeat offset | Map original master time to continuous beat position using canonical interpolation semantics |
| DanceRudiments recordings | `dancerudiments.recording` schema 1, float64, 12 decimal places, named channels, BPM provenance, source beat origin and `start_pip` | Strictly validate, preserve metadata, never silently wrap/out-of-range sample |
| Audio/cues/lyrics | Timestamps remain in original master seconds | Transport translates excerpt/clip clocks before mapping source time to beats |
| Rig libraries | Motion source time, rig-specific channel identity and calibration must be retained | Rig profile, channel mapper and retargeting stay separate from raw recording data |

**Upstream inspection gate:** Obtain authoritative format definitions, serialized test fixtures and code paths from all upstream repositories. Pin commit SHAs, release tags and golden-file SHA-256 in `docs/compatibility-matrix.md`. Do not assume docs are sufficient to resolve ambiguous anchor interpolation, signed phase offsets, recording bounds or source-coordinate conventions.

### Time and beat semantics

- Define `SongSeconds` as an original master-source timestamp, with separate `DspSeconds`, `OutputSeconds`, `PlaybackRate`, `SourceOffsetSeconds` and `TransportGeneration`.
- `AbsoluteBeat` is a double-precision continuous quarter-note beat coordinate with an explicitly defined beat-zero anchor and optional negative pre-roll.
- At fixed tempo: `B(t) = B0 + (t - t0) * BPM / 60`.
- At variable tempo: evaluate the canonical beat-anchor interpolation; derive `LocalBpm` from the appropriate segment. Do **not** naively integrate interpolation if upstream defines beat position directly between anchors.
- `BeatIndex = floor(B)`; `BeatPhase = B - floor(B)`, always `[0,1)` even for negative beats.
- `Pip64 = floor(64 * B)`; `PipAlpha = frac(64 * B)`; `Tick16 = floor(16 * B)`.
- `BarIndex`, `BarPhase` and `Downbeat` require meter and bar-origin metadata. `4/4` is only a default for content without a specified meter.
- Separate an explicitly annotated downbeat/Beat 1 from the plain quarter-note beat origin and StemLab's signed 64-pip correction.
- A tempo map may contain gaps, inconsistent/missing anchors, repeats and tempo discontinuities. Validation must either preserve canonical semantics or report a precise error; never invent a beat.
- Distinguish physical time, transport time, source-audio time, musical time and recording-source time. Conversions must be invertible where the source contract permits.

## 3. Package architecture

```text
AudioSource / DSP clock --+               +-- fixed BPM source
External transport -------+--> Transport -+-- validated StemLab beat timeline
Editor/manual source -----+               +-- optional external/MIDI clock
                                            |
                                            v
                                  Authoritative BeatState
                                            |
                +---------------------------+----------------------------+
                |                           |                            |
          CPU Samplers              Shader Clock Bridge          Event/Cue Scheduler
                |                           |                            |
       Bindings / Playables       Shader Graph / HLSL / VFX    Timeline / Gameplay
                |
       Rig Profiles / Retargeters
                ^
                |
        Validated Native Assets <---- Importers / Addressables / optional MCP
```

Architectural invariant: **all consumers calculate effects from the same immutable beat-state snapshot for a given simulation/render timestamp**. They do not accumulate their own frame-delta clocks.

### Assembly and dependencies

- `DanceFlow.Unity.Runtime`: managed C# core with no Unity Editor dependency.
- `DanceFlow.Unity.Editor`: importers, inspectors, preview windows, asset tools.
- `DanceFlow.Unity.Tests.Editor` and `.Tests.Runtime`: Unity Test Framework suites.
- Optional assembly integrations: Animation Rigging, Timeline, Visual Effect Graph, Addressables and input/MIDI connectors, guarded by assembly version defines or independent adapters.
- Use a supported Unity 6 LTS version as the primary CI target; verify precise editor/pipeline compatibility at build time. URP first; HDRP/Built-in through separately tested pipeline adapters. Avoid making SRP package dependencies compulsory for a headless clock consumer.
- Keep native DanceRudiments ABI use optional and behind an interface, with explicit platform support and golden parity tests. The pure managed clock and recording importer must not depend on native plugin binaries.

## 4. Clock and transport subsystem

### 4.1 Interfaces

Proposed public-facing interfaces:

```csharp
public interface IDanceFlowTimeSource {
    double GetClockSeconds();
    bool IsRunning { get; }
}
public interface IBeatMap {
    BeatPosition Evaluate(double sourceSeconds);
    double SourceSecondsAtBeat(double absoluteBeat);
}
public readonly struct BeatState {
    public readonly double SourceSeconds;
    public readonly double AbsoluteBeat;
    public readonly double Bpm;
    public readonly long BeatIndex;
    public readonly long Tick16;
    public readonly long Pip64;
    public readonly float BeatPhase;
    public readonly float BarPhase;
    public readonly float PipAlpha;
    public readonly long TransportGeneration;
}
```

Signatures are provisional; the real contract must include meter, bar/downbeat origin, loop iteration, sync provenance, validation and immutable snapshot metadata. Source time should be available as double; GPU representations are separate.

### 4.2 Implemented time sources

- `AudioDspClockSource`: Unity `AudioSettings.dspTime`, scheduled audio start, clip offset, pause/resume, playback-rate changes and seek epochs. Understand DSP clock suspension and audio configuration resets.
- `ManualClockSource`: explicit deterministic source timestamp, tests, editor scrubbing, offline capture.
- `UnityGameTimeSource`: fallback for silent demos, not claimed audio-sample-accurate.
- `ExternalClockSource` (later): input timestamp and jitter buffering for MIDI clock, Ableton Link or network sync, requiring a clearly defined authority and drift policy.

### 4.3 Transport operations and events

Methods: `Load`, `Prepare`, `PlayScheduled`, `Pause`, `Resume`, `Stop`, `SeekSourceSeconds`, `SeekBeat`, `SetPlaybackRate`, `SetLoopRange`, `SetSourceExcerpt`, `EvaluateAt`, `Dispose`.

Events: `OnBeat`, `OnBar`, `OnDownbeat`, `OnPhrase`, `OnCue`, `OnTransportDiscontinuity`, `OnTempoSegmentChange`, `OnSourceIdentityMismatch`.

Events are scheduled by **crossed musical boundaries**, not `phase < previousPhase` heuristics. Define separate seek policies (`Suppress`, `RebuildState`, `ReplayRange` with bounded cap) and catch-up policies (`DeliverAll`, `AggregateMissed`, `LatestOnly`). Never issue unintended cue bursts on a seek.

### 4.4 Identity and validation

- Beat timelines must reference the canonical master file hash; fail closed on mismatch unless a caller explicitly supplies a documented trusted override mode.
- Decode and validate bounded anchor data; reject NaN, Inf, duplicate/conflicting anchors, invalid ordering, unsafe JSON and unsupported schema versions.
- Audio excerpts are translated to master source time **before** beat mapping.
- Record source identity, beat-map checksum, interpolation version, timestamp conversion and authority in diagnostics.

## 5. GPU clock contract and shader API

### 5.1 Uniform design

Provide both simple global shader properties and a packed versioned state block. Version with `_DanceFlow_ShaderApiVersion`, and use `_DanceFlow_` prefix:

- `_DanceFlow_BPM`, `_DanceFlow_BeatPhase`, `_DanceFlow_BarPhase`, `_DanceFlow_PipPhase`.
- `_DanceFlow_BeatSin`, `_DanceFlow_BeatCos`, `_DanceFlow_BeatPulse`, `_DanceFlow_DownbeatPulse`.
- `_DanceFlow_BeatIndexLow`, `_DanceFlow_BeatIndexHigh` or origin-relative beat count; do not rely on one floating point beat counter for arbitrarily long playtime.
- `_DanceFlow_Meter`, `_DanceFlow_TransportGeneration`, `_DanceFlow_SourceSecondsHighLow` where necessary.
- Optional per-renderer offsets, local subdivisions and motion-source selection using property blocks or instance buffers.

For precise `phase(periodBeats)` over long durations, calculate locally from a bounded origin-relative beat plus explicit modular offset, rather than `frac(largeFloat / period)`.

### 5.2 Shader library

Create `Runtime/GPU/DanceFlowClock.hlsl` with `DF_BeatPhase`, `DF_BarPhase`, `DF_CyclePhase`, `DF_Pulse`, `DF_PulseEnvelope`, `DF_SwingPhase`, `DF_Quantize`, `DF_MotionSample` and precision-safe fixed-period operations. Support both direct HLSL and reusable Shader Graph Custom Function/Sub Graph assets. All functions must be pure and seek-safe.

### 5.3 Render update ordering

- Advance/evaluate one BeatState per render context before draw submission (SRP begin-frame/begin-camera or tested equivalent), with appropriate attention to multiple cameras, editor scene view and render pipeline state.
- Support `AudioAccurate` (DSP-aligned), `VisualPredictive` (optional calibrated output-latency offset), and `OfflineDeterministic` evaluation policies; do not make unmeasured latency claims.
- Global parameters are convenient for the master song; multi-song local timing requires per-material/per-object buffers and scoped state. Never leak values between simultaneous scenes/cameras.
- Validate on Metal, Direct3D, Vulkan and mobile backends where supported. Ensure fallbacks for platforms that cannot bind required structured buffers.

### 5.4 GPU motion data

Upload interleaved channel samples to `GraphicsBuffer` or texture arrays according to target support. Retain float64 on CPU for import/parity; convert to float32 GPU representation with recorded quantisation tolerance. Metadata includes channel-count, sample range, channel mappings, sample stride, source beat origin and strict bounds. Support interpolation per channel (step, linear, spline, quaternion-specific) and explicit sample-out-of-range policy. GPU memory allocations must be bounded, pooled and disposed.

## 6. Motion recording subsystem

### 6.1 Import format

Read `dancerudiments.recording` schema 1 exactly: `format`, `schema_version`, `pips_per_beat:64`, `sample_dtype:float64`, `sample_decimals:12`, unique channel names, finite [-1,1] values, BPM and provenance, `beat_zero_seconds`, signed `start_pip`, `phase_status` and non-looping bounds. Preserve unknown future schemas as unsupported, not silently parsed. Maintain upstream resource caps and optionally tighter Unity project limits.

### 6.2 Storage and performance

- Metadata in ScriptableObject, bulk samples in a versioned little-endian binary asset with documented layout and SHA-256.
- Define a channel table, row count, sample count, source-clock metadata and chunk index for large recordings.
- Lazily load recordings using Addressables (optional adapter) or explicit local references; do not eagerly load full libraries site-wide.
- Use NativeArray/Burst/Jobs for CPU high-throughput sampling where compatible, with a managed reference implementation as the oracle.
- Respect that procedural catalogue motion may loop by its own period while captured recordings do not loop automatically.

### 6.3 Source time and musical retiming

A captured recording's pip coordinate is based on **its own** `beat_zero_seconds`, `start_pip` and BPM. To synchronise with a different master song, specify a `MotionSyncMap` (original-time, stretch-to-beats, anchor-correspondence or explicit tempo retime). Never silently reinterpret 64 recorded pips as the target song's 64 pips without applying a declared mapping. Expose stretch limits, source-phase confidence, endpoint policy and discontinuity warnings.

### 6.4 Binding and mixing

- Target adapters: Transform, MeshRenderer material, shader property block, Light, ParticleSystem/VFX, custom reflection-free property interface, Animation Playables, rig bones/constraints.
- Per-channel curves: `gain`, `bias`, `scale`, `clamp`, `axis`, unit, interpolation, blend weight, optional filter and phase offset.
- Mixed layers: additive, override, masked, weighted and event-triggered; deterministic ordering and clearing on disposal/seek.
- Phase-based sampling should not force continuous transforms to quantise at 64 updates/beat. Exact-pip parity remains testable separately.

## 7. Rigging and character motion

- Add `RigProfile` with versioned bone/channel IDs, source coordinate system, rotation convention, units, reference pose, Rigify rig family and retargeting constraints.
- Research the current DanceRudiments rig type hierarchy and Rigify profiles, then build an explicit compatibility matrix rather than hardcoding a universal rig.
- Convert rig curves to Unity humanoid/generic channels as appropriate; retain original rig data and mappings for round-trip export.
- Use Animation Playables/animation jobs for bones where possible; provide optional Animation Rigging IK and foot-contact correction.
- Implement root-motion extraction, pose continuity, contact stabilization, blend masks, locomotion scaling and skeletal proportions warnings.
- Prioritise lightweight independently loaded Boxbot and Prism demonstrations, retaining source timing and licence/provenance. Do not claim CMU recordings are automatically beat matched.

## 8. Editor, authoring and UX

- A `DanceFlow Setup` window validates package dependencies, import settings, available render pipelines and current source schema support.
- A `Beat Timeline Inspector` shows beat anchors, local BPM, Beat 1 phase, source waveform position, discontinuities, and scrubbable transport.
- A `Recording Inspector` shows channels, min/max values, source BPM confidence, recorded extent, selectable XYZ projection and all-dimension bars.
- A `Rig Mapping Editor` maps source channels to bones/constraints with preview, diff, missing-channel warnings and saved reusable profiles.
- A `Motion Binding Graph` creates typed mappings for Unity targets with presets and reproducible serialization; avoid runtime reflection for untrusted input.
- Timeline tracks/clips: absolute source-time, musical beat-relative and beat-following retimed clip modes; make which time domain a clip uses unambiguous.
- Shader Graph templates: Beat Pulse, Bar Pulse, Phase Gradient, Vertex Bounce, Motion Texture Displacement and multi-channel Emission.
- Editor tools must preview without permanently mutating scenes/assets; Undo/Redo, prefab workflows and deterministic restores required.

## 9. Asset delivery and optional integration

- Base package contains code, tiny synthetic fixtures, lightweight shaders and no heavyweight recordings/models.
- Optional downloadable packs: procedural motion catalogue, motion captures, Rigify profiles, demo meshes, example songs (only where distribution rights permit).
- Each pack must provide manifest ID, semantic version, SHA-256 of payloads, licence, source provenance, compatible rig profiles, schema and install size.
- Project-owned cache under a predictable folder (Unity project's designated asset/cache root); explicit opt-in downloads, progress/cancel/retry and checksum verification. Avoid checking downloaded datasets into the package repository.
- Optional DanceFlow MCP client/editor integration: discovery, capability listing, remote asset metadata and workflows. Credentials remain editor-side in a supported secure store; never put secrets in Shader Graph assets, bundles, logs or player builds.
- Define optional exporter for Unity-authored keyframes/poses into a canonical DanceFlow recording/rig format only after reviewing upstream authoring contracts.

## 10. Security, robustness and privacy

- Treat JSON, motion/rig packages, manifests and remote metadata as untrusted data; strictly validate numeric bounds, dimensions, binary lengths, hashes and asset paths.
- No runtime code evaluation from recording files; no arbitrary executable scripts in manifests.
- Disallow traversal, symlink escapes and untrusted extraction outside sandbox/cache destination.
- Limit decompress size, memory budget, CPU sampling budget, GPU buffer allocations and recursion in imported trees.
- Network calls and downloads require explicit user action; optional MCP integration must not upload song assets, recordings or secrets implicitly.
- Offer fully offline operation for local recordings and beat maps.

## 11. Test strategy and acceptance gates

### 11.1 Canonical golden fixtures

Generate fixtures from **actual upstream implementations** and check in small deterministic manifests with source commit, source hash, input time, expected beat/tick/pip/BPM, recording channels and exact native sample values. Include:

- Fixed tempos including non-integer BPM; negative preroll and nonzero Beat 1 offset.
- StemLab anchor boundaries, tempo accelerations/decelerations, corrected downbeats and gaps.
- Arbitrary seek forward/backward, pause/resume, loop boundaries, excerpt remapping and playback rates.
- Captured recording first/last pip, negative start pip, no auto-wrap and high channel counts.
- Odd time signatures and bar origin transitions.
- Rig calibration, source-coordinate conversion and source-time-preserving playback.

### 11.2 Numerical precision

- Clock CPU parity is double precision against canonical reference with specified absolute/relative tolerance; exact integer pip/tick boundary behavior must agree.
- Float64 source recording values are retained. GPU float32 comparison uses declared tolerance and tests long transport durations with phase splitting.
- Measure jitter, clock drift, render-vs-audio offset and crossing correctness under framerate changes and main-thread stalls; record measured values, not guarantees unsupported by the platform.

### 11.3 CI matrix

- Build UPM package with assembly definitions and Unity package validation.
- EditMode unit tests for validators, maps, time conversions, source-offset and sampler logic.
- PlayMode integration tests for AudioSource scheduled playback, seek, actual shader uniforms, Shader Graph compilation and renderers.
- Render pipeline smoke tests for URP first; planned HDRP/Built-in; GPU screenshots with tolerance and known source timestamp.
- Windows/Linux/macOS editor matrix where build infrastructure permits; platform builds for targets explicitly marked supported.
- Run static checks, package manifest validation, public API compatibility tests and licensing/source-manifest checks.

### 11.4 Exit criteria for v1.0

1. One song can use either fixed BPM or validated StemLab corrected timeline with observed parity to upstream.
2. CPU and GPU expose a coherent musical state with precise beat/bar phase and safe long-duration handling.
3. N-dimensional native recordings import and preserve metadata, boundaries and channel data.
4. At least two independent binding targets work (e.g. Transform and Shader Graph), with representative rigged-character playback.
5. Seek/pause/loop and deterministic editor scrubbing pass golden tests.
6. Example projects demonstrate procedural motion, source recordings, variable-tempo song and synced vertex/fragment shader effects.
7. UPM installation, docs, licensing, tests and release artifacts validated through CI.

## 12. Implementation phases, outputs and dependencies

### Phase 0 — Repository/package bootstrap and upstream contract lock
**Deliver:** UPM skeleton, asmdefs, changelog, licence choice, `.gitignore`, basic CI and docs index; record canonical upstream commit/release pins and fixtures.  
**Tasks:** Validate Unity editor target/version and pipeline; read exact upstream source serializers; define package naming and asset IDs; define public API namespaces; establish mono-repo versus standalone repository decision; generate compatibility matrix.  
**Gate:** Empty package imports cleanly into verified Unity project; upstream schema/provenance fixture hashes pinned; tests execute headlessly.  
**Dependencies:** None.

### Phase 1 — Fixed and variable BPM musical transport
**Deliver:** Managed double-precision beat map, immutable BeatState, fixed BPM/manual/DSP clocks, source and excerpt timestamp mapping, transport controls, event scheduler.  
**Tasks:** Implement signed beat math, multiple meter support, corrected timeline parser, authoritative interpolation, transport discontinuity generations, scheduler replay policies, source hash checks.  
**Gate:** Golden time-position parity for all fixed and variable tempo fixtures; seek and loop tests pass across framerates.  
**Dependencies:** Phase 0; canonical StemLab/DanceMoves anchors.

### Phase 2 — GPU timing and shader tooling
**Deliver:** Global clock shader bridge, packed high/low phase/state, HLSL library, URP Shader Graph examples and GPU tests.  
**Tasks:** Implement render callback ordering, multi-camera support, pulse envelopes, musical subdivisions, shader property IDs, editor preview and explicit latency policy.  
**Gate:** Pixel and vertex shader examples follow beat state under fixed/variable BPM and scrubbing; no visible phase loss in long-duration test.  
**Dependencies:** Phase 1.

### Phase 3 — Recording data and motion playback
**Deliver:** Strict recording importer, binary cache, managed sampler, optional Burst job path, motion target adapters and GPU recording buffers.  
**Tasks:** Non-looping bounds, exact integer pip parity, interpolation profiles, memory management, source-clock remapping, per-object channel bindings and streaming load.  
**Gate:** Sample data agrees with upstream; multiple objects move smoothly and deterministically; invalid assets fail safely.  
**Dependencies:** Phases 1–2; DanceRudiments recording fixtures.

### Phase 4 — Rigging and animation integration
**Deliver:** RigProfile schema, Unity bone mapper, Playables/Animator integration, basic retargeting, rig validation, Boxbot/Prism sample scenes.  
**Tasks:** Rigify mappings, coordinate conversions, quaternion handling, root motion, clipping and blends, optional IK.  
**Gate:** Two different real skinned rigs animate using source recordings, preserve source timing and pass rig/profile tests.  
**Dependencies:** Phase 3; published upstream rig definitions and licensed motion packs.

### Phase 5 — Editor productivity and authoring
**Deliver:** Inspectors, beat/recording/rig visualisation, mapping graph, musical Timeline tracks and authoring UX.  
**Tasks:** Preview/scrub, source diagnostics, Undo/Redo, prefab workflows, configurable motions, provenance UI.  
**Gate:** Non-programmer can import a beat map, attach a recording, map it to a character and produce beat-driven Shader Graph effects without code.  
**Dependencies:** Phases 1–4.

### Phase 6 — Optional ecosystem features and production hardening
**Deliver:** DanceFlow MCP optional integration, separate downloadable motion packs, download progress UI, Addressables adapter, performance baselines, CI release automation and UPM release documentation.  
**Tasks:** Security review, offline mode, platform compatibility, dependency/asset licensing, demo builds, version tags, release artifact verification.  
**Gate:** v1.0 exit criteria met on declared supported Unity versions/platforms; no heavyweight media bundled into core package.  
**Dependencies:** Phases 0–5.

## 13. Critical path and priority

1. **P0:** Determine exact canonical beat-anchor interpolation and sample frame conversion: all downstream fidelity depends on it.
2. **P0:** Separate source time from DSP/audio time and recording time; avoid propagating frame-update drift.
3. **P0:** Define shader precision/offset contract before exposing public shader names.
4. **P0:** Enforce non-looping recording semantics and explicit rig/source retiming policy.
5. **P1:** Build minimal URP proof-of-concept where DSP-timed fixed and variable tempo animate both vertex and fragment stages.
6. **P1:** Build exact source recording sampler parity before higher-order interpolation or rig mixing.
7. **P1:** Retarget two source-licensed character animations with independent optional packages.
8. **P2:** Editor workflow, optional MCP, networked clocks, HDRP and platform expansion.

## 14. Risks and mitigation

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Beat timeline semantics differ from simplified docs | Wrong phase and misaligned cues | Pin upstream reference implementations and golden fixture exports first |
| DSP scheduling vs presentation latency | Audio-visual offset | Explicit timing modes, instrumentation and calibrated compensation |
| GPU float precision at long runtimes | Beat jitter | Split high/low or bounded relative phase; shader-only tests |
| Frame stalls skip beat boundaries | Lost or duplicated triggers | Boundary-range scheduler and transport generation tracking |
| Improperly retimed source recording | Implausible choreography | Source-clock metadata and explicit MotionSyncMap |
| Rigify/Unity skeleton mismatch | Bone distortions, incorrect root motion | Typed rig profiles, conversion rules and validation fixtures |
| Native dependency portability | Incompatible platforms or UPM install | Managed baseline, optional native backend with platform matrix |
| Huge motion assets and GPU buffers | Excessive bundle size and memory | Separately versioned optional packs, streamed chunks and budgets |
| Licence restrictions on motion/audio | Inability to redistribute demos | Asset-by-asset provenance, source hashes and permissions gate |
| Shader Graph/SRP version differences | Broken samples | Versioned pipeline integration and compile smoke tests |

## 15. First development slice / pull request proposal

**PR 1 — `feat: initialise DanceFlow Unity UPM package and clock contracts`**

- [ ] Verify the latest stable releases and source implementations in DanceMoves, FrameRudiments, DanceRudiments and StemLab.
- [ ] Record source commit SHAs and fixture generation instructions in compatibility matrix.
- [ ] Add UPM `package.json`, runtime/editor/test `.asmdef` files, package icon/licence metadata as applicable.
- [ ] Add `IBeatMap`, `BeatState`, `FixedBeatMap`, `ManualClockSource`, mathematical helper primitives.
- [ ] Add strict `.beat-timeline.v1` parser with source-hash checks (once exact source contract is reviewed).
- [ ] Add fixed/variable tempo golden-fixture EditMode tests and boundary/negative-index tests.
- [ ] Add `DanceFlowClock` editor diagnostics that display source seconds, BPM, beat, tick, pip and bar phase.
- [ ] Add Unity CI UPM import/test workflow and minimal reproducible test project.
- [ ] Add documentation links and document what is not yet implemented.

**PR 2 — `feat: shader clock bridge and URP example`**

- [ ] Implement shader clock property contract and precision test.
- [ ] Add Shader Graph and HLSL beat-pulse and vertex-deformation demos.
- [ ] Add manual-time deterministic visual test harness and multi-camera reset tests.
- [ ] Validate fixed/variable tempo timing against the same clock fixture.

## 16. Release and documentation policy

- This plan is the **first file in `docs/`** and the docs index points here.
- Subsequent documents: `01-compatibility-matrix.md`, `02-clock-contract.md`, `03-shader-api.md`, `04-recording-format.md`, `05-rigging.md`, `06-editor-workflows.md`, `07-testing.md`, `08-release-process.md`.
- Every phase updates its relevant spec and API docs before merge, with test evidence.
- SemVer package releases; release notes describe schema/API compatibility and known unsupported platforms.
- No PR should silently introduce a different musical clock or copy upstream DSP/recording extraction logic into Unity.
- Upstream changes requiring contract changes must update fixture pins and explain backwards compatibility.

## 17. Source research and open decisions

### Primary upstream sources

- [DanceMoves README and musical clock](https://github.com/kieransimkin/DanceMoves)
- [FrameRudiments README and variable-tempo integration](https://github.com/kieransimkin/FrameRudiments)
- [DanceRudiments README](https://github.com/kieransimkin/DanceRudiments)
- [DanceRudiments recording specification](https://github.com/kieransimkin/DanceRudiments/blob/main/docs/recordings.md)
- [StemLab](https://github.com/kieransimkin/stemlab)
- [DanceFlow release preflight](https://github.com/kieransimkin/DanceFlow/blob/main/docs/workflows/release-preflight.md)

### Unity APIs to verify against targeted Unity version

- `AudioSettings.dspTime` and `AudioSource.PlayScheduled`.
- `Shader.SetGlobalFloat`, `Shader.SetGlobalVector`, constant buffers and Scriptable Render Pipeline events.
- Shader Graph Custom Function and Sub Graph asset support.
- `GraphicsBuffer`, `ComputeBuffer`, shader model/platform constraints and structured buffer binding.
- `AnimationScriptPlayable`, Animation Jobs, Timeline and optional Animation Rigging integration.

### Explicit decisions still requiring code-level research

1. Exact canonical StemLab schema fields, interpolation at bad/missing anchors and signed downbeat offset.
2. Reference beat-origin/bar-origin semantics across DanceMoves and FrameRudiments.
3. Procedural catalogue C# binding viability under Unity runtime/IL2CPP and target platforms.
4. Native rig/motion schema used by recently imported Rigify examples.
5. Unity 6 LTS exact patch level and graphics pipeline matrix for CI.
6. Binary interchange representation for future generic rig recordings and shader-channel GPU data.

**Decision rule:** Resolve these during Phase 0 and record an ADR, instead of embedding assumptions into public APIs.
