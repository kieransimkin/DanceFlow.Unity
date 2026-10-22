# Phase 2 — Detailed integration, benchmarking and resource-monitoring plan

| Metadata | Value |
| --- | --- |
| Document ID | `DFU-PHASE2-001` |
| Status | Historical implementation design and current acceptance/benchmark checklist; **Phase 2 source is committed, GPU hardware validation outstanding** |
| Created | 2026-10-22 |
| Repository | [`kieransimkin/DanceFlow.Unity`](https://github.com/kieransimkin/DanceFlow.Unity) |
| Original Phase 1 base | [`0d7328cec185494cbed891e301c223df617d1849`](https://github.com/kieransimkin/DanceFlow.Unity/commit/0d7328cec185494cbed891e301c223df617d1849) |
| Phase 2 published source | [`70e0866c3bf602d4771a50ea47f7939d344e2f2a`](https://github.com/kieransimkin/DanceFlow.Unity/commit/70e0866c3bf602d4771a50ea47f7939d344e2f2a) |
| Current UPM manifest | `0.3.0` on `main`; release acceptance **not established** by source commit alone |
| Unity baseline | Unity 6 (`6000.0` minimum declaration), URP first; pin exact Editor/URP/Shader Graph versions before acceptance |

**Related specifications:** [00 implementation plan](00-implementation-plan.md) · [01 compatibility matrix](01-compatibility-matrix.md) · [02 clock contract](02-clock-contract.md) · [03 shader implementation reference](03-shader-api.md).

> **Important distinction (22 October 2026):** This document preserves the detailed Phase 2 design, benchmark matrix, resource-monitoring criteria, and acceptance checklist. The original document was drafted against the Phase 1 baseline. Since then, Phase 2 *source code* was committed on `main`. The authoritative description of the implemented APIs is `docs/03-shader-api.md`; where its code-level design differs from a proposal here, follow the implemented API, update this checklist, and benchmark the difference. Do not interpret completed source changes as validated real-device GPU measurements.

> The primary outcome is a deterministic, GPU-visible **render snapshot derived exclusively from the Phase 1 clock**. Benchmarking and monitoring are first-class deliverables, not follow-up optimisation. All performance budgets below are proposed gates for pilot measurement, **not measurements of an existing implementation**. Hardware-dependent thresholds must be calibrated and approved against same-machine paired baselines.

## 1. Goals, boundaries and release definition

### Goals

1. Expose fixed- and variable-BPM position, local tempo, beat/bar/pip/tick phase, musical cycles and analytic beat pulses to Unity vertex and fragment shaders.
2. Implement a render-pipeline-independent, allocation-free CPU encoding of the current immutable Phase 1 `BeatState`; add a minimal URP publisher and Unity Shader Graph/HLSL adapters.
3. Preserve source-time beat alignment, signed pre-roll, meter/bar origin, pause/seek/restart, loop source phase versus continuous phase, and manual deterministic preview.
4. Provide explicit lifetime and ownership rules for a global master clock, per-render-context snapshots, multiple cameras and independently scoped clocks.
5. Deliver reproducible clock-vs-GPU parity tests, automated frame timing/performance test scenes and machine-readable benchmark records.
6. Monitor time and memory **overhead of the plugin itself**, along with overall CPU/GPU frame times, GC pressure, available graphics-resource estimates and dropped/stalled frames.
7. Make observability available to later movement-buffer, rigging and VFX phases without importing those features in Phase 2.

### Deliberately out of scope

- Audio analysis, beat detection, CPU motion-recording import, 3D rig retargeting, GPU motion recordings and full Visual Effect Graph integration (Phases 3–4).
- A universal per-pixel audio-accurate display claim: on-device audio-to-photon calibration is a separate measurement programme.
- A universal hardware GPU-utilisation or VRAM-free counter: Unity has no reliable, portable API for these across all supported graphics platforms.
- Silent retiming or repairing of a source-audio master when the current Phase 1 audio adapter cannot implement loop/rate changes on the actual `AudioSource`.
- A new musical-clock implementation, timeline detector, or allocation-heavy always-on analytics layer.

**Definition of Phase 2 done:** URP pixel and vertex shaders visibly respond to **the same authoritative clock** on fixed and source-verified variable BPM, pass deterministic GPU parity/seek/loop tests, and produce reproducible benchmark bundles proving bounded incremental CPU/GPU/resource cost on declared hardware. Non-Unity managed correctness runs in existing .NET CI, while actual shader tests run in licensed Unity with a graphics device.

## 2. Verified Phase 1 integration points and prerequisite gates

This plan is grounded in the current source: `Runtime/Clock/BeatState.cs`, `BeatMaps.cs`, `MusicalTransport.cs`, `MusicalScheduler.cs`, `DanceFlowClockBehaviour.cs`, `TimeSources.cs`, `UnityDspClockSource.cs`, and `Runtime/DanceFlow.Unity.Runtime.asmdef`.

| Current API/source | Confirmed behaviour | Phase 2 integration requirement |
| --- | --- | --- |
| `BeatState` | Immutable `SourceSeconds`, `AbsoluteBeat`, `ContinuousBeat`, `Bpm`, integer `Tick16`/`Pip64`, all fractional phases, meter/bar origin, `TransportGeneration`, `ContinuityGeneration`, loop iteration, playing flag | Encode, **do not recalculate**, source and continuous beat coordinates; retain CPU double precision |
| `MusicalTransport.Evaluate()` | Calculates a state from an external clock; no event emission; main-thread only | Safe entry for an independent render-time evaluation; **do not** advance schedulers here |
| `MusicalTransport.EvaluateSourceSeconds(t)` | Deterministic evaluation at explicit original source seconds | Build offline/manual screenshot tests and shader preview; no real audio required |
| `DanceFlowClockBehaviour.Current` | Recomputed in `Update()`, then boundary/cue schedulers run | Treat as gameplay snapshot; render publisher creates one newer render-context snapshot to avoid stale CPU update data |
| `DanceFlowClockBehaviour.Transport` | Readable live transport; no single global clock authority | Add explicit `DanceFlowShaderClockBinding` selection and ambiguity diagnostics, not a first-found singleton |
| Phase 1 tests | Synthetic fixed/irregular beat anchors, negative phase, source identity validation and boundary scheduling | Use unchanged fixtures; add shader-encoding golden outputs and renderback fixtures |
| Phase 1 audio | Scheduled straight playback only; excerpt looping and rate changes of actual audio not fully implemented | GPU reflects *musical transport* accurately; demos cannot claim rescheduled audio is proven for every transport mode |

**Preflight gate P2-G0:** Pull the latest `main`, compare with the base commit above, and rerun `dotnet run --project tools/clock-checks/ClockChecks.csproj -c Release`. Check actual GitHub Actions and Unity Test Runner results. Pin the source SHA and Unity versions used for new fixture generation. If Phase 1 tests or UPM import fail, fix those **before** measuring performance; do not substitute assumptions.

### Concrete hazard: update time versus render time

`DanceFlowClockBehaviour` currently evaluates `Current` during `MonoBehaviour.Update()`. Rendering happens later and may execute for more than one camera; simply uploading `Current` can introduce a within-frame lag. A new `RenderClockSampler` should evaluate the **existing** transport at the chosen render timestamp once per render context. It returns a `RenderBeatSnapshot` with `BeatState`, frame identifier, transport/continuity generation, monotonic capture timestamp and clock identifier. It **never** dispatches boundary/cue events; these remain owned by Phase 1 `Update()`.

Choose and test one deterministic frame policy:

- **Runtime audio-authoritative:** sample the existing DSP-backed musical transport at render-context entry; do not accumulate Unity `deltaTime`.
- **Offline/Editor:** accept an explicit source timestamp via `EvaluateSourceSeconds`, independent of wall time.
- **Optional predictive:** add a separately configured display offset only when measured on target hardware; identify this policy in diagnostics and reports. Default compensation is zero.

Two cameras in the **same** render context must see a stable snapshot unless a camera-specific clock is expressly bound. Paused transport remains paused; variable BPM remains source-frame-authoritative.

## 3. Integration architecture and assembly boundaries

```text
                   existing Phase 1 (do not duplicate)
   ManualClockSource / DSP --> MusicalTransport --> BeatState
                                                   |
                                   RenderClockSampler (main thread)
                                                   |
                               RenderBeatSnapshot (immutable, versioned)
                                       /                      \
                              PhaseEncoder                BenchCounters
                                  |                         (opt-in)
                      RenderGlobalClockPublisher                 |
                       (one explicit master/context)       Profiler / export
                                  |
                          packed GPU state v1
                           /             \
                HLSL include        URP Shader Graph wrapper
                  /     \                  /           \
             fragment   vertex      pulse material   deformation material
```

### Proposed modules and file ownership

```text
Runtime/
  GPU/
    RenderBeatSnapshot.cs
    RenderClockSampler.cs
    ShaderClockEncoding.cs             # pure C# core; no UnityEngine dependency if practical
    DanceFlowShaderClockBinding.cs     # component, explicit clock + mode
    RenderGlobalClockPublisher.cs      # URP/SRP adapter; no separate time source
    DanceFlowShaderGlobals.cs          # cached IDs and declared v1 layout
    DanceFlowClock.hlsl                # shared, pure HLSL phase/cycle/pulse functions
  Performance/
    DanceFlowProfilerMarkers.cs        # scoped markers in normal player code
    PerformanceCounters.cs             # optional bounded sampler + capability flags
    PerformanceSampleBuffer.cs         # bounded ring, no hot-path formatting/logging
    PerformanceReport.cs               # output schema; exporter separately invoked
Editor/
  ShaderClockDiagnosticsWindow.cs      # read-only snapshots, sampler status, capability reports
  PerformanceBenchmarkWindow.cs        # opt-in start/stop/config, not benchmark timing source
Tests/
  Editor/ShaderClockEncodingTests.cs
  Runtime/ShaderClockPlayModeTests.cs
  Runtime/RenderContextLifecycleTests.cs
  Fixtures/shader-clock-golden-v1.json
Samples~/ShaderClock/
  BeatPulse/                         # source-only URP shader + material
  VertexDeformation/
  VariableTempo/
  ShaderGraph/                       # generated/import-verified graph files
  BenchmarkScenes/                   # generated procedural scenes, no large meshes/audio
  README.md
tools/benchmarks/
  config/                            # deterministic scenario JSON
  harness/                           # Unity test project and Player runner
  analysis/                           # regression/summary scripts
  baselines/README.md                # methodology; no fictional baseline numbers
.github/workflows/
  clock-contracts.yml                # preserve existing .NET parity job
  shader-import-tests.yml            # Unity license/editor availability required
  benchmark-correctness.yml          # GPU-capable runner, artifact producing
```

**Package hygiene:** Keep `com.kieransimkin.danceflow.unity` free of mandatory URP/HDRP/Shader Graph dependencies in its runtime assembly. Tests and samples may use a separately pinned URP fixture project. `UnityEditor` code belongs only in editor assemblies. Avoid Unity Editor imports in pure clock and encoding tests. Do not ship benchmarking image/video outputs inside the UPM package or commit generated reports to the runtime tree.

## 4. Shader state contract (proposed v1)

### 4.1 Public naming and data

The public prefix is `_DanceFlow_`; HLSL functions use `DF_`. GPU fields are **derived data**, not a second independent clock. Proposed **semantic interface**, to be frozen before implementation:

| Field | Domain and required meaning |
| --- | --- |
| `_DanceFlow_ShaderApiVersion` | ABI identifier; unsupported shader ABI must not look like a valid playing clock |
| `_DanceFlow_Valid`, `_DanceFlow_IsPlaying` | Explicit state; zero/unbound means no authoritative clock |
| `_DanceFlow_BPM` | Local BPM of the selected Phase 1 map, never nominal BPM when a corrected map is active |
| `_DanceFlow_BeatPhase`, `_DanceFlow_BarPhase` | `[0,1)` from `AbsoluteBeat`, correct for negative times and non-4/4 meter |
| `_DanceFlow_TickPhase`, `_DanceFlow_PipPhase` | Fractional position within one 16-tick or 64-pip interval |
| `_DanceFlow_BeatPulse`, `_DanceFlow_DownbeatPulse` | Documented analytic envelope(s), not frame-triggered impulses |
| `_DanceFlow_Meter`, `_DanceFlow_BarOriginPhase` | Explicit meter and bar-origin semantics; no default downbeat claim |
| `_DanceFlow_AbsoluteBeat*` | Precision-safe song/source-relative beat representation (loop returns to source phase) |
| `_DanceFlow_ContinuousBeat*` | Precision-safe cumulative beat representation across source loops |
| `_DanceFlow_TransportGeneration*` | Discontinuity indication; exact 64-bit transport generation retained only on CPU |
| `_DanceFlow_SourceSeconds*` | Optional split/modular time; no single unbounded float timestamp for shader phase |

**Allocation-minimising packing:** Start with a small, versioned set of `float4` vectors (`Phase`, `Tempo`, `Pulse`, `ClockFlags`) and phase-safe high/low or modular integer beat coordinates. Publish via cached `Shader.PropertyToID` IDs. Prefer a fixed shader-facing layout over many individually mutated arbitrary properties. Only include compatibility aliases if measured and necessary.

**Precision design ADR (mandatory):** A single `float` beat counter fails for long sessions because its fractional resolution degrades. Implement and compare a high/low beat representation and CPU-precomputed cycle residues; select one layout that supports arbitrary authoring periods in the declared range without losing correctness. Explicitly test 24 hours at 20/120/400 BPM, negative pre-roll, 3/4 and 7/4 meters, periods of 1/16, 1/2, 1, 3, 4, 7, 16, 64 and 256 beats. If an HLSL variant lacks the required integer operations, expose supported-period limits and a tested fallback; never silently substitute `frac((float)AbsoluteBeat / period)`.

**Critical distinction:** A physically displayed frame can skip over several 64-pip steps at high BPM. Shader phase must remain correct *at its sampled frame time*; discrete beat and cue events belong to the CPU scheduler. Do not promise that every pip receives its own displayed frame.

### 4.2 Pure HLSL functions and Shader Graph wrappers

Proposed `Runtime/GPU/DanceFlowClock.hlsl` functions:

- `DF_GetBeatPhase()`, `DF_GetBarPhase()`, `DF_GetPipPhase()`, `DF_GetLocalBpm()`.
- `DF_CyclePhase(periodBeats, phaseOffsetBeats, sourceOrContinuous)` with tested modular arithmetic and documented period limits.
- `DF_Pulse(phase, width, shape)` and `DF_DownbeatPulse(width, shape)`; all equations deterministic, no frame-delta state or implicit smoothing.
- `DF_QuantizeBeat`, `DF_RaisedCosine`, `DF_SwingPhase` with stable boundary behaviour.
- A no-clock fallback returning disabled/static values, not silently acting as a guessed 120 BPM track.

Shader Graph adapters must be **actual importable and compiled `.shadergraph`/subgraph assets**, generated or validated in the pinned Unity editor, not hand-authored plausible JSON. They expose named beat/pip/bar/tempo and pulse outputs. Include examples in both vertex and fragment stages. Ensure matching globals are read from HLSL and not overridden by material properties declared on a Shader Graph blackboard. Add graph preview defaults that are labelled *preview only*, never mistaken for live source timing.

### 4.3 Ownership, scoping and cleanup

- `DanceFlowShaderClockBinding` references an explicit `DanceFlowClockBehaviour` or a caller-supplied `MusicalTransport`/manual snapshot. It must never pick an arbitrary object returned by `FindObjectOfType`.
- **Exactly one master publisher per render context** in the minimal global path. Duplicate masters cause a clear conflict diagnostic and a safe disabled state, rather than last-writer-wins.
- Scene unload, `OnDisable`, device reset, domain reload and Play Mode exit remove registrations/callbacks and clear global validity so shaders cannot keep stale last-frame values.
- Separate clocks on individual materials/objects should use an optional scoped API. Benchmark material variants, instancing and render-path scoping before choosing per-renderer `MaterialPropertyBlock` because it can disable SRP Batcher optimisation. Do **not** assume per-renderer blocks are free.
- Multiple cameras/stacking/shadows/reflections and XR must be included in lifecycle tests. If globals cannot be isolated correctly across concurrent render passes, require an explicit pipeline/pass-scoped binding rather than globally mutating conflicting values.

## 5. Render pipeline lifecycle and publication strategy

**Preferred URP integration experiment:** Subscribe/unsubscribe to `RenderPipelineManager.beginContextRendering` with one long-lived delegate. Unity's older `beginFrameRendering` callback is retained for compatibility but is documented as allocation-producing. At context entry, create one immutable render snapshot, pack it and upload global shader values. Publish per camera only if a tested camera-specific binding calls for it; do not evaluate/double-upload for each camera by default.

Alternative path: explicit URP `ScriptableRendererFeature` / render pass at an identified point, using `CommandBuffer.SetGlobalVector` (or Render Graph compatible global state, after testing). Compare timing/ownership implications rather than assuming the earlier callback always executes before every relevant shader pass. Documentation should name the supported pipeline and render order.

Maintain a clean boundary:

1. `RenderClockSampler` is main-thread only and samples the **same** `MusicalTransport` used by gameplay.
2. `ShaderClockEncoding` is a deterministic pure function: `BeatState + published options -> ShaderClockStateV1`.
3. `RenderGlobalClockPublisher` is the only component allowed to publish master `_DanceFlow_` globals.
4. `DanceFlowClockBehaviour.Update()` continues to own scheduler advancement; shader publication is read-only and does not affect cues.
5. Render callbacks have explicit registration lifetimes, no closure/`List<>`/string creation per frame, no log traffic, and no leaked `CommandBuffer` or `GraphicsBuffer` resources.

**Fallback:** A documented legacy pipeline path may publish from `Camera.onPreRender` or a suitable component callback only after actual testing; do not claim SRP support from a `Camera` legacy hook. The minimal release target is URP, with an adapter seam ready for HDRP/Built-in.

## 6. Instrumentation and performance telemetry subsystem

### 6.1 Core principle

Deliver **two** opt-in paths:

- **Always-available lightweight instrumentation:** a small set of named `ProfilerMarker` scopes inside Phase 2 code (generally inactive/minimal overhead when not profiled).
- **Opt-in benchmark/resource recorder:** bounded preallocated sample rings, sampling rate control, machine-readable exports and report metadata. Disabled by default in published games. No network upload, unbounded logs, per-frame `Debug.Log`, string formatting or disk writes in measured loops.

Counters need provenance: `{value, unit, collection_source, coverage, available, reason_if_unavailable, sampling_period}`. Distinguish **hardware utilisation** from **frame time** and **memory owned by this package** from **process-level reserved memory**.

### 6.2 CPU counters

Use `ProfilerMarker` for the following named scopes, and `ProfilerRecorder`/Unity Profiler where supported:

| Marker | What it isolates |
| --- | --- |
| `DanceFlow.Clock.RenderEvaluate` | `MusicalTransport.Evaluate()` cost inside the render publisher |
| `DanceFlow.GPU.EncodeState` | double-to-GPU precision packing |
| `DanceFlow.GPU.PublishGlobals` | shader property/global upload CPU overhead |
| `DanceFlow.GPU.RenderContext` | complete callback and ownership resolution |
| `DanceFlow.Perf.Sample` | monitoring overhead itself |
| `DanceFlow.Perf.Export` | data export; must run outside timing window |

Collect main-thread CPU frame time, render-thread CPU frame time and total frame time independently of marker times. Capture per-frame GC allocation bytes and event counts **where available**, not inferred from a total heap delta. Use `ProfilerRecorderHandle.GetAvailable` or recorder validity to detect supported counters on each build. Named `ProfilerRecorder` values may use nanoseconds for frame-time markers; normalise all units into milliseconds in reports.

For a **CPU utilisation percentage**, use an optional target-specific process sampler (e.g. OS process CPU time / elapsed wall time / documented core normalisation) and label its scope explicitly. On devices where unavailable, show `unavailable`. Main-thread frame time is **not** whole-process CPU utilisation.

### 6.3 GPU frame timing and usage

- Primary portable timing source: `FrameTimingManager.CaptureFrameTimings()` plus `GetLatestTimings()`, on **supported** platforms and with required Frame Timing Stats enabled. Associate delayed results with appropriate completed frames; Unity documents a roughly four-frame delay.
- Store `gpuFrameTime`, `cpuFrameTime` and any supported render-thread/present-wait metrics separately. A returned zero GPU time can mean *unavailable*, not an idle GPU; do not accept or chart it as 0 ms. Record missing/unsupported samples and the graphics API.
- Use Unity Profiler GPU markers and RenderDoc/PIX/Xcode/vendor tooling only as **optional diagnostic follow-up**, not as a mandatory generic runtime dependency.
- Distinguish `gpu_time_ms / target_frame_budget_ms` (a **budget fraction**, potentially greater than 1) from actual GPU core utilisation. Never label it `GPU % usage`.
- Report draw calls, batches, shader variants, overdraw and SRP Batcher behaviour where observable. If a counter is not available in release players, retain that exact limitation in the report.

### 6.4 Memory, GPU resources, GC and lifecycle

| Metric | Collection mechanism | Correct interpretation |
| --- | --- | --- |
| Managed used/reserved | `ProfilerRecorder`, `GC.GetTotalMemory` for controlled snapshots, Unity Profiler/Memory Profiler | Heap use is **not** process RAM or per-package allocation |
| Unity native allocated/reserved | Profiler APIs/counters where available | Unity-tracked resources, not necessarily all native allocations |
| Process resident/working set | Optional OS-specific collector or Unity Memory Profiler snapshot | May require permissions/platform-specific code |
| GPU resource bytes owned by DanceFlow | Explicit ledger at every owned `GraphicsBuffer`, `RenderTexture`, texture and material creation/dispose | Exact request sizes/estimates of package-owned resources; **not** driver VRAM usage |
| Graphics driver memory | Unity estimated graphics memory counters / Memory Profiler snapshot when supported | May be unavailable and is not reliable as total VRAM on all devices |
| GC bytes/frame | Supported Unity allocation counter and profiler captures | 0 bytes steady-state expected in production hot path |
| Persistent leak | Warm baseline vs post-disposal stable-state snapshots (multiple cycles) | Control for Unity caches/allocator high-water marks; compare retained object references and owned-buffer ledger |

`SystemInfo.graphicsMemorySize` describes reported graphics memory capacity and must **not** be used as a live utilisation counter. Reserve an optional adapter seam for vendor telemetry, but require user opt-in and never ship vendor software in the base package.

**Sampling:** frame-time series every rendered frame during short benchmarks; slow native/managed/process memory counters at configurable 1–2 Hz; buffer/allocation ledger on lifecycle transitions. Memory Profiler snapshots are **outside** the timing interval because capture perturbs performance. An overlay is disabled during official runs; self-instrumentation cost is measured separately.

### 6.5 Recorder lifecycle, budgets and privacy

- Preallocate a bounded ring (`maxFrames`, default proposed 8192); automatically stop or overwrite with explicit `dropped_samples`/`wrapped` metadata.
- No per-frame LINQ, allocations, serialized JSON, reflection or disk I/O. No driver-query synchronous stalls without an explicit benchmark mode.
- Dispose `ProfilerRecorder` instances; remove SRP/event callbacks and native resource handles on domain reload, scene unload and stop.
- Export only to a user-selected local folder or CI artifact path, never automatically send to a server. Do not include original audio, waveform, lyrics, paths containing secrets, personal project asset data or auth tokens.
- Record `telemetry_enabled`, `instrumentation_overhead_ms` and collector capabilities for every run.

## 7. Benchmark design: isolate added cost

### 7.1 Controlled A/B/C/D experiments

Benchmark *the same generated scene, player binary, resolution and device* under the following modes:

| Mode | Change from baseline | Purpose |
| --- | --- | --- |
| **A: scene only** | No DanceFlow component, same materials/geometry where possible | Establish engine and scene floor |
| **B: Phase 1 only** | `MusicalTransport` + ordinary Update clock, no shader bridge | Attribute pre-existing musical-clock cost |
| **C: publish only** | Phase 1 + render publisher and uniforms, shader operations disabled | Isolate bridge CPU/native submission cost |
| **D: shader active** | Phase 1 + publisher + beat pulse/deformation HLSL | Measure actual shader computation cost |
| **E: optional scoped** | D plus per-object clocks/overrides using alternative binding methods | Expose batching and scaling trade-offs |
| **M: monitoring on** | Repeat C/D with counters enabled | Measure measurement perturbation; not used as unqualified baseline |

Cost attribution example: `BridgeCPU = CPU_p95(C) - CPU_p95(B)`; `ShaderGPU = GPU_p95(D) - GPU_p95(C)`; `RecorderOverhead = CPU_p95(M) - CPU_p95(corresponding mode)`. Negative differences are possible due to noise and must not be clipped. Compare paired runs and report spread and confidence, not a single cherry-picked frame or fps number.

### 7.2 Scenario matrix

| Dimension | Pilot values | Reason |
| --- | --- | --- |
| Tempo | Fixed 60, 120, 145, 240, 400 BPM; irregular StemLab map | Catch timing frequency assumptions |
| Duration | 10 s smoke; 3 min steady test; 30 min soak; simulated 24 h source timestamp | Distinguish throughput, leaks and float precision |
| Meter | 3, 4, 7 beats/bar; nonzero bar origin | Avoid hard-coded 4/4 and wrap errors |
| Transport | scheduled start, pause/resume, seek backwards/forwards, looping and rate changes (clock-only when audio not retimed) | Validate generation and source/continuous phase semantics |
| Objects | 1, 100, 1000, 10000 (generated meshes) | Quantify scalability and CPU submission cost |
| Cameras | 1, 2, 4 incl. stacking, Scene view or reflection where supported | Detect redundant publication and state leakage |
| Resolution | 1280×720, 1920×1080, 2560×1440; 4K optional | Separate fragment fill from constant upload overhead |
| Shader workload | minimal unlit, heavy vertex, heavy fragment, full-screen pulse, Shader Graph | Identify ALU/vertex/fill bottlenecks |
| Platform | Windows DX11/DX12 and Linux Vulkan first where hardware exists; macOS Metal and mobile selectively | Driver/graphics backend variability |
| Build | Release/Development Player separately, Editor for correctness only | Avoid confusing Editor overhead with player costs |

For each **paired** A–D scenario use a predetermined seed and deterministic camera path. Warm for **600 frames** (or until shader compilation/pipeline warmup is complete), collect **at least 1800 stable frames** on the target player, and run **three or more paired repetitions** in alternating A/B/B/A order to reduce thermal drift. For 30-minute soak runs, log start/end memory and regular slow samples, not unbounded per-frame histories. State if the exact requested protocol could not be completed.

Report **median, p95, p99 and maximum frame time**; per-scope CPU p50/p95/p99, long-frame rate above 16.67 and 8.33 ms budgets, GC bytes/frame, memory high-water/delta, object count, draw calls and resource ledger. Export raw samples for reproducibility in bounded CSV or compressed formats, alongside JSON summary. Treat 60 FPS = 16.67 ms and 120 FPS = 8.33 ms as **frame budgets**, not claimed measured performance.

### 7.3 Correctness and visual quality benchmarks

- **Analytic parity:** compare GPU-rendered encoded phase/tempo against Phase 1 CPU golden values at representative source times, especially variable-anchor boundaries and extrapolation.
- **Long-runtime precision:** cover simulated 24 h at 400 BPM and negative source time, with multiple odd-length cycle periods. Compare phases using circular distance `min(abs(a-b),1-abs(a-b))`.
- **Seek/pause/loop:** pixel readback of source/continuous phase before and after discontinuities, including a loop that does not start at beat zero; prove Shader Graph and raw HLSL agree.
- **No update-order race:** two cameras in one render context must see a coherent snapshot; multiple scene instances must trigger explicit ownership conflict handling.
- **Binning/quantisation:** reference `Tick16` and `Pip64` must obey floor semantics at negative beat boundaries; GPU pulse logic should not invent extra trigger events.
- **Screenshots:** create fixed-time images without camera jitter/TAA/automatic exposure; compare numeric phase channels first, visual image/SSIM second with hardware-specific tolerances.
- **Shader correctness regression:** shader compilation/warning checks, no fallback pink materials, graph port stability and at least one real Player rendering test.

GPU readback introduces synchronisation cost; use a separate correctness mode and never count readback in throughput performance benchmarks.

### 7.4 Reproducibility and environment capture

Each report records: repository SHA/dirty state, package version, Unity full editor version, URP/Shader Graph versions, platform/API/backend, build type, scripting backend, resolution and render scale, vsync/fps cap, quality and dynamic-resolution settings, anti-aliasing, CPU/GPU model, OS/driver, memory, power profile, thermal state if measurable, duration, seed, sample count, scene variant hashes, profiling settings, missing metrics and limitations. A scenario must be directly rerunnable from its metadata. Disable vsync/caps when measuring raw throughput; document separate capped real-world frame pacing runs.

A/B test results should run on the same physical device and graphics API and be evaluated **per device class**. Do not compare GitHub-hosted virtual-machine graphics data against a physical gaming PC baseline.

## 8. Preliminary resource targets and regression policy

These remain **proposed acceptance targets for the initial hardware pilot**, not measured performance or currently established pass results:

| Measure | Proposed provisional gate | Qualification |
| --- | --- | --- |
| Steady-state managed allocations, monitoring OFF | **0 B/frame** in render clock encoding and publication path after warmup | Validate with supported allocation counters and detailed profiler capture; don’t infer from constant heap size |
| CPU extra overhead of publisher C–B | **p95 ≤ 0.15 ms** on a named desktop reference machine | Absolute provisional; also record % of 8.33/16.67 ms frame budget |
| CPU extra overhead on a named mobile device | Initial target **p95 ≤ 0.35 ms** | Only gate after mobile reference hardware identified |
| Persistent memory held by clock publisher alone | Initial design target **≤ 512 KiB** excluding Unity-owned shader compilation caches and benchmark fixtures | Attribute via owned allocations and snapshots, not process heap alone |
| GPU timing | Shader D–C incremental p95 plus scaling curve | Do not set a universal hard ms limit before pilot hardware/fragment-workload baselines |
| Clock correctness | GPU phase circular error **≤ 1×10⁻⁴ cycles** for designated float32-compatible path over declared test horizon | Must prove in actual shaders; tighter internal pure CPU checks expected |
| Lifecycle | No live subscriptions/registrations/owned GPU resources after unload; zero stale valid clock | Hard gate |
| Monitoring overhead | Measure M–C/M–D; strive for **≤ 0.1 ms p95** at default collection settings | Pilot-derived gate; separately report each telemetry collector |
| Long soak | No monotonic growth of **retained DanceFlow-owned allocations** after cleanup cycles | Unity process reservation may remain high due to caching and is not automatically a leak |

Gate policy: run a pilot across stable machines, publish measured baselines per device/driver/graphics API, then ratify thresholds. Detect regression using matched A/B trials, effect size and confidence intervals; fail on hard correctness, unexpected GC allocations and definite resource leaks. Flag noisy GPU/performance changes for review rather than failing randomly on unstable or virtualised runners. No-success-by-missing-data: required GPU metrics must be available on the designated graphics runner, otherwise **inconclusive/block** its performance gate; CPU-only CI can still pass independently.

## 9. Benchmark report schema and analysis

Proposed JSON contract `danceflow.unity.performance.v1`:

```json
{
  "schema": "danceflow.unity.performance.v1",
  "run_id": "reproducible-scenario-id",
  "package_sha": "FULL_COMMIT_SHA",
  "scenario": "urp_1080p_1000objects_145bpm_shader",
  "mode": "D",
  "environment": {
    "unity": "EXACT_VERSION",
    "render_pipeline": "URP",
    "render_pipeline_version": "EXACT_VERSION",
    "graphics_api": "Vulkan",
    "gpu_name": "REFERENCE_HARDWARE",
    "platform": "REFERENCE_PLATFORM",
    "development_build": false,
    "vsync": 0,
    "resolution": [1920, 1080]
  },
  "sampling": {
    "warmup_frames": 600,
    "measured_frames": 1800,
    "dropped_samples": 0,
    "monitoring_enabled": false
  },
  "metrics": {
    "cpu_main_thread_ms": {"available": false, "reason": "not-yet-measured", "source": "ProfilerRecorder", "p50": null, "p95": null, "p99": null},
    "gpu_frame_ms": {"available": false, "reason": "not-yet-measured", "p95": null},
    "gc_alloc_bytes_per_frame": {"available": false, "reason": "not-yet-measured"},
    "danceflow_owned_gpu_bytes": {"available": false, "reason": "not-yet-measured", "value": null, "source": "owned-resource-ledger"}
  },
  "correctness": {
    "max_circular_beat_phase_error": null,
    "shader_readback_tested": false
  }
}
```

Nulls here are **schema examples**, not real measurements. Actual output must include source/units for each metric and must not claim `available: true` for a metric whose value is unknown. Require schema validation, percentile computation, baseline comparison, budget-table/Markdown generation and trend plots from raw artifacts. Store bounded developer fixtures in repository; keep full large raw reports as CI artifacts with retention. Export a human-readable summary with clear `PASS`, `FAIL`, `INCONCLUSIVE`, `NOT SUPPORTED` per metric and explicit reasons.

## 10. CI, build and benchmark automation

### 10.1 Keep and extend existing Phase 1 CI

Existing `.github/workflows/clock-contracts.yml` must remain green, continue running the standalone .NET 8 harness and validate fixtures and package manifests. Add a pure managed shader encoder parity harness to this path so most fixed/variable/negative/long-timestamp checks run without Unity or a GPU.

### 10.2 Unity import and compile checks

Create a **minimal pinned Unity test project** outside the distributable UPM runtime that imports the package locally through a `file:` package reference (or pinned Git URL for release smoke). Build/EditMode Test Runner on a licensed Unity Editor using a matrix with the precise chosen Unity 6 patch, URP package/Shader Graph version and Editor platform. Keep editor and runtime assembly boundaries clean. Test `.hlsl` includes and Shader Graph imports/compilation in the editor; software/headless `-nographics` is **not** proof of GPU shader execution.

If a Unity licence/graphics runner is not available, report the job as `not executed` with a clear missing prerequisite. Do not declare the GPU integration verified because only the pure .NET harness passed.

### 10.3 Hardware-backed GPU checks and sampling

Dedicated GPU-capable runners or real target devices build a standalone Player and exercise fixed scripted scenarios. CPU and GPU clocks/labels should be recorded, as well as run logs, render screenshots/readback results, optional Profiler captures and JSON benchmark report. Require a report attachment with SHA provenance to each PR. Use GitHub CI artifact retention, not tracked large files. Do not require privileged vendor telemetry in shared runners.

### 10.4 Release gate and deterministic repetition

Pipeline stages:

```text
static/format/schema checks -> Phase 1 .NET parity -> pure GPU encoding parity
  -> licensed Unity import/EditMode -> URP shader compile and Player smoke
  -> GPU visual/readback parity -> paired benchmark collection -> regression assessment
  -> documentation/manifest/release checks -> reviewer approval
```

Run short correctness gates on every PR and scheduled fuller 30-minute soak/resource audits on dedicated hardware. A CPU-only PR CI may validate managed code but **must not mark GPU gates passed**. Pin actions/dependencies/Unity versions after initial bootstrap; use least privilege and keep licence secrets out of logs.

## 11. Concrete milestones and ordered pull requests

### PR P2-1 — Contract and pure shader encoding

**Files:** `RenderBeatSnapshot.cs`, `ShaderClockEncoding.cs`, golden fixture and pure encoder tests, `docs/03-shader-api.md`. Define semantic ABI, source/continuous loop choice, modular phase precision, aliases and missing-clock semantics. Deliver CPU-only fixture parity with negative beats, irregular anchors, 24-hour simulated time, 3/4/7/4 and odd periods. **Exit:** no Unity graphics dependency required for encoding tests; exact policy documented and frozen as shader API v1.

### PR P2-2 — SRP publisher and lifecycle

**Files:** `DanceFlowShaderClockBinding.cs`, `RenderGlobalClockPublisher.cs`, cached shader property IDs, lifecycle tests. Integrate `beginContextRendering`, sample once/context, set globals with no hot allocations, clear validity/unsubscribe on unload, reject duplicate global masters. Confirm Scene view, camera stacking and render callbacks. **Exit:** a debug shader can read clock state, multiple cameras agree, zero allocations in steady-state publisher code and cleanup works.

### PR P2-3 — HLSL, URP materials and Shader Graph

**Files:** shared HLSL include; compiled basic URP shaders; generated Shader Graph/subgraph assets; tiny demo scenes. Add beat emission, vertex pulse/deformation, variable-tempo scrub and explicit source-vs-continuous loop demo. **Exit:** vertex and fragment output match CPU oracle at fixed source timestamps; importable package samples work without paid assets.

### PR P2-4 — Monitoring foundation and benchmark harness

**Files:** `Performance/` counters/markers, capability registry, resource ledger, bounded ring and exporter; scenarios/config; separate Unity Player benchmark runner. Record CPU/GPU/GC/memory/draw calls and overhead of measurement, unsupported metrics flagged. Create A/B/C/D/M paired-run scripts, baseline report schema, raw samples and summary analyzer. **Exit:** a repeatable local run exports valid results with provenance and explicit unavailable fields; no benchmark output in distributable package.

### PR P2-5 — Validation, thresholds, CI and release readiness

Run full synthetic and real-device smoke matrices, add Unity import/URP Shader Graph CI where licensed; onboard first stable GPU hardware runner and execute pilot. Publish per-device baselines and adjusted performance budgets. Run leak/soak and scene unload tests. Repair batching or precision regressions. **Exit:** tests and performance evidence attached to PR; documentation/README/sample instructions updated; no unverified claims of audio-to-photon latency; issue tracked for Phase 3 GPU motion recording buffer integration.

Each PR must include source/tests/docs, a `Known limitations` update, and performance evidence proportional to its changes. PR P2-4 monitoring infrastructure must be in place **before** treating any P2-2/P2-3 performance claim as a release gate; minimal marker names can land earlier to avoid refactoring.

## 12. Work tickets and dependencies

| Ticket | Owner module | Dependency | Deliverable / test |
| --- | --- | --- | --- |
| P2-T01 | Clock/Encoding | Phase 1 base | Immutable render snapshot, `Evaluate` non-event path, timestamps and generation tests |
| P2-T02 | GPU/ABI | T01 | v1 packed field layout, source/continuous contract, >=24h phase precision parity |
| P2-T03 | Render callbacks | T02 | URP context hook, singleton master policy, multi-camera/callback cleanup |
| P2-T04 | GPU/HLSL | T02–T03 | HLSL beat/pulse/cycle functions, fragment/vertex fixtures |
| P2-T05 | Shader Graph | T04 | Graph/subgraph import, URP assets, visual/phase readback tests |
| P2-T06 | Profiling core | T01 | Profiler markers, optional recorder, valid/missing counter flags |
| P2-T07 | Resource ledger | T06 | Ownership and teardown metrics, no unbounded native/managed/GPU resources |
| P2-T08 | Bench runner | T03–T07 | Generated scenes, A/B/C/D/M modes, reproducible metadata and raw samples |
| P2-T09 | Analysis | T08 | JSON schema validation, p50/p95/p99, delta + confidence and summary report |
| P2-T10 | CI/hardware | T04–T09 | Unity import + real GPU readback tests and benchmark artifacts |
| P2-T11 | Release docs | All | User setup guide, shader API, explicit limitations, versioning and approved baseline |

## 13. Acceptance checklist

### Functional (hard gates)

- [ ] Existing Phase 1 fixed and variable beat timeline tests pass unchanged.
- [ ] Verified StemLab source identity, irregular BPM, origin and negative pre-roll remain correct.
- [ ] Shader semantic ABI/version and source/continuous loop phase are documented and tested.
- [ ] Fixed/variable BPM drives actual Unity URP vertex **and** fragment shader outputs.
- [ ] Manual-time screenshot/readback tests pass for seeks, pause, loops, multiple cameras and non-4/4 meter.
- [ ] Shader/HLSL/Shader Graph assets import and compile in pinned Unity 6/URP; no unintended mandatory rendering dependency.
- [ ] No stale shader validity after clock disable, scene unload, domain reload or audio configuration change.
- [ ] Render callback does not double-fire CPU beat/cue events.

### Benchmark and monitoring (hard to ship without evidence)

- [ ] CPU markers isolate sampling/encoding/upload; runtime profiling is **off by default**.
- [ ] Reproducible A/B/C/D/M comparisons run on recorded hardware with raw and summarised results.
- [ ] GPU frame timing has available/unavailable semantics; no simulated GPU utilisation percentage.
- [ ] RAM, GC and DanceFlow-owned graphics allocations are measured or explicitly marked unavailable.
- [ ] 0 B/frame steady-state allocations from Phase 2 render path after warmup, using a supported profiler capture.
- [ ] Paired soak/teardown demonstrates no DanceFlow-owned resource leaks or leaked event subscriptions.
- [ ] Performance gates use approved device baselines; missing GPU test data is an explicit blocker/inconclusive result.
- [ ] `0.3.0` package release, changelog and tagged samples occur only after verified CI and review.

## 14. Risks, investigations and mitigations

| Risk | Mitigation / required evidence |
| --- | --- |
| `Current` from Update is stale for render | Sample Phase 1 transport at SRP context entry, distinct gameplay/render snapshots; use readback timing checks |
| Different clock sources/scene instances fight over globals | Explicit master registration and conflict/fail-closed behaviour; scoped pass/material alternatives |
| Shader properties shadow globals | Declare-only HLSL uniforms and check Shader Graph material property exposure/precedence |
| Single float beat loses phase over long playback | High/low or modular ABI with signed and odd-period 24h parity suite |
| Loop source position and continuous phase confused | Distinct fields and cycle APIs, loop-boundary reference samples |
| MPB per-instance clocks break SRP Batcher | Compare batching/draw counts and CPU/GPU cost; default one master global |
| Render-callback deprecation or allocations | Prefer beginContextRendering, unit-test registration/disposal and profile allocations |
| `FrameTimingManager` GPU metrics unavailable/delayed | Check support, tag delayed/missing frames, never use invalid zeros; supported hardware runner |
| VRAM estimate confused with GPU capacity/utilisation | Explicit owned ledger + snapshot provenance; label memory estimates and `SystemInfo.graphicsMemorySize` as capacity |
| CI host thermal/driver variability | Pair/repeat tests, pin environment, dedicated runner, confidence/noise rules |
| Monitoring overhead hides real effect | A/B with telemetry OFF, separate mode M with on-device collector overhead |
| Source-audio loops/rate changes not played by Phase 1 AudioSource adapter | Scope demonstrations as transport-only until proper audio driver verified |
| Unity/URP API churn | Pin real Unity editor/URP versions; modular HLSL and scoped SRP adapters |

## 15. Documentation and ecosystem handoff

Deliver a public readme path from `README.md` to this document and future user-facing shader quickstart, plus explicit links to `docs/00-implementation-plan.md` and `docs/02-clock-contract.md`. Record GPU ABI compatibility and performance sample-schema versions separately from the package SemVer. Golden fixtures and source hashes should be fixed and auditable.

**Phase 3 dependency:** Export GPU timing buffer APIs and resource-monitoring interfaces as an *adapter seam*, but do not add recording buffers until the Phase 3 source-clock and non-looping data contracts are implemented. Phase 3 will extend the same CPU/GPU benchmark harness rather than introduce a separate profiler.

### Verified Unity API references (reviewed 2026-10-22)

- [RenderPipelineManager.beginContextRendering](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.RenderPipelineManager-beginContextRendering.html) and [legacy beginFrameRendering allocation warning](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.RenderPipelineManager-beginFrameRendering.html).
- [Shader.SetGlobalVector](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Shader.SetGlobalVector.html) and [CommandBuffer.SetGlobalVector](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.SetGlobalVector.html).
- [ProfilerRecorder](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html), [ProfilerMarker](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerMarker.html), [Profiler counters reference](https://docs.unity3d.com/6000.0/Documentation/Manual/profiler-counters-reference.html).
- [FrameTimingManager API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/FrameTimingManager.html) and [timing availability/delay/platform limitations](https://docs.unity3d.com/6000.0/Documentation/Manual/frame-timing-manager.html).
- [MaterialPropertyBlock / SRP Batcher limitations](https://docs.unity3d.com/6000.0/Documentation/Manual/DrawCallBatching-Properties.html).
- [Unity Memory Profiler package](https://docs.unity3d.com/Packages/com.unity.memoryprofiler%401.1/manual/index.html).

### Open ADRs to resolve during first PR (not reasons to reimplement the clock)

1. Exact GPU packed vector layout and HLSL integer/float fallback representation across the first supported platforms.
2. Global source-vs-continuous default, including scene-context conflict resolution, camera ownership and scoped multi-clock API.
3. Default pulse width/shape semantics and whether any short transient needs an analytic prefilter under low FPS.
4. Exact Unity 6 editor patch and URP/Shader Graph package version, hardware profiles and benchmark runner availability.
5. Initial benchmark thresholds after the first measured A/B pilot; no invented baseline numbers.

**Handoff recommendation:** Implement P2-1 and P2-2 before authoring elaborate shader samples. Start instrumentation marker names alongside these changes, then complete the benchmark harness before claiming Phase 2 has been performance-validated. Keep the original plan as `docs/00-implementation-plan.md`, the implemented shader API at `docs/03-shader-api.md`, and this benchmark/acceptance plan at `docs/03-phase2-integration-plan.md`.


## Post-commit acceptance note (2026-10-22)

The published source commit implements core Phase 2 clock publishing, GPU shader helpers, a URP example and opt-in performance observation. This is **not** proof that all P2-1 through P2-5 gates above have passed. Before closing Phase 2, run the Unity editor and URP graphics tests on named hardware, collect baseline A/B/C/D/M runs with unavailable counters clearly flagged, exercise long-running phase precision and lifecycle/GC tests, and attach raw benchmark artifacts with the exact commit, player/driver, Unity version and graphics API. Use the current implementation reference for exact public property names and test commands.
