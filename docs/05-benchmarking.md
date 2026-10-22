# Phase 2 — Unity GPU verification, performance benchmarks and resource monitoring

**Status:** tools implemented against the Phase 2 source; real Unity Editor compilation, GPU readback and hardware performance runs remain **unverified** until executed on licensed, GPU-equipped hardware. This document is an execution guide, not a fabricated benchmark report. See [the original acceptance plan](03-phase2-integration-plan.md) and [the shader API](03-shader-api.md).

## What the package now measures

`DanceFlowShaderMetrics` is opt-in and bounded (no network use, recording disabled by default). `BeginCapture(...)` preallocates 128–16,384 `FrameObservation` records and optionally starts `ProfilerRecorder` counters. It reports:

| Observable | Source | Semantics / caveats |
| --- | --- | --- |
| CPU frame time, CPU main-thread time | `FrameTimingManager` | Overall frame duration, not process CPU load |
| GPU frame time | `FrameTimingManager` | GPU elapsed time, **not** GPU occupancy or utilisation; unsupported on some APIs |
| Publisher microseconds | Bridge `Stopwatch` | CPU-side shader publication only; instrumentation changes measured cost |
| GC allocated bytes/frame | Memory `ProfilerRecorder` | Frame allocation counter when supported; zero is valid |
| Total reserved bytes | Memory `ProfilerRecorder` | Unity/app memory estimate, not all system RAM |
| Video used bytes | Memory `ProfilerRecorder` | Estimated video allocation, **not** free VRAM or GPU load |
| Graphics driver allocated bytes | `Profiler` in Editor/Development | Optional driver-reported allocations; unavailable elsewhere |
| System/graphics identity | `SystemInfo` | Platform, GPU/CPU names, graphics API, Unity version, resolution |

The v2 report includes valid/missing flags for every sampled counter, original frame timing timestamps to avoid double-counting delayed results, sampled-frame identifiers, summary p50/p95/p99 and raw observations. The capture buffer is bounded and reports overwrites; an overflow invalidates strict comparative analysis. Resource counters only activate in explicitly monitored scenarios.

`Window > DanceFlow > Shader Clock Diagnostics` displays current observables and provides **Start opt-in detailed capture**, **Stop capture**, **Calculate percentiles**, and **Export metrics JSON**. Percentiles are calculated on demand, not on every editor repaint.

## Build a real Unity benchmark project

1. Install an activated **Unity 6** editor on the target Windows graphics machine and create a **URP Unity project** (3D URP template). Assign a URP pipeline asset. **Do not use** `-nographics`.
2. In Unity Package Manager, add `https://github.com/kieransimkin/DanceFlow.Unity.git` or embed a clone under `Packages/com.kieransimkin.danceflow.unity`. Import the **Shader Clock (URP)** sample from Package Manager.
3. Choose **Tools > DanceFlow > Create URP Benchmark Scene**. This generates materials, a camera, light, Phase 1 clock, Phase 2 bridge, metrics component and runner under `Assets/DanceFlowBenchmark/`.
4. Choose **Tools > DanceFlow > Build URP Benchmark Player**. This enables `PlayerSettings.enableFrameTimingStats` and builds a Development Player for the active Windows/macOS/Linux standalone target. For meaningful benchmarks compare builds produced using identical Unity, URP, quality and graphics settings.
5. Keep resolution, quality, VSync, object count, camera, load and driver identical between all paired runs. The runner explicitly sets unlimited target FPS and VSync off. It does not claim to synchronise real audio during performance tests; the clock uses synthetic, silent 120 BPM.

For scripted build on Windows, supply the actual editor and dedicated URP project paths:

```powershell
.\tools\benchmarks\Build-BenchmarkPlayer.ps1 -UnityEditorPath 'C:\Program Files\Unity\Hub\Editor\6000.x\Editor\Unity.exe' -ProjectPath 'C:\Projects\DanceFlowBench'
```

The sample is a separate Unity project dependency, **not** a mandatory package dependency. The tools do not download an editor, install a licence or silently replace another project's render pipeline.

## Paired A/B/C/D/M hardware benchmark

| Variant | Musical clock | Shader globals | Effect shader | Resource telemetry |
| --- | --- | --- | --- | --- |
| `Baseline` (A) | Off | Off | Normal URP/Lit | Off |
| `Clock` (B) | On | Off | Normal URP/Lit | Off |
| `Globals` (C) | On | On | Normal URP/Lit | Off |
| `Shader` (D) | On | On | DanceFlow vertex/fragment | Off |
| `GlobalsMonitored` (C+M) | On | On | Normal URP/Lit | On |
| `ShaderMonitored` (D+M) | On | On | DanceFlow vertex/fragment | On |

All six variants generate the same shared-mesh sphere layout. They warm up **600** frames, collect **1,800** measured frames plus 6 flush frames for delayed GPU timings, and each run starts a fresh Player process. At least **three paired repeats** are required by default. The script rotates scenario order between repeats to reduce order/thermal bias; for stronger acceptance add repetitions and run at different object counts (e.g., 256, 1,024, 4,096).

```powershell
.\tools\benchmarks\Run-HardwareBenchmarks.ps1 `
  -PlayerPath 'C:\Projects\DanceFlowBench\Build\DanceFlowBenchmark\DanceFlowBenchmark.exe' `
  -OutputDirectory '.\bench-results' -Repeats 3 -Objects 1024 -RequireGpu
```

The script exports per-run `danceflow.unity.benchmark.v2` JSON and a `danceflow.unity.benchmark-analysis.v1` JSON. It exits nonzero if the Player fails, a report is missing, or strict GPU evidence is inconclusive. It **never replaces** a missing timing with zero.

The independent analyzer may be run manually:

```powershell
python tools/benchmarks/analyse.py .\bench-results\*-repeat*-*.json `
  --output .\bench-results\analysis.json --required-repeats 3 --require-gpu
```

Pass only the **raw Unity Player report JSONs**, not a previously generated analysis file. The analyzer verifies hardware/project identity, variant completeness, raw nonnegative finite observations, at least 80% measured-frame GPU counter coverage when GPU data is mandatory, and sample-capacity integrity. It computes type-7 percentiles and uses the median of **within-repeat** p95 deltas, avoiding biased subtraction of unpaired runs.

The provisional CPU publisher budget in the earlier plan (0.15 ms p95) is **not** an accepted device-independent threshold. After an initial pilot, hardware owners can optionally supply a calibrated delta budget through `--max-global-cpu-delta-ms`.

## Shader correctness acceptance (must accompany benchmark)

`Tests/Runtime/GpuShaderReadbackTests.cs` renders the **imported URP shader** using `RenderPipeline.StandardRequest`, reads the resulting GPU texture and checks both fragment emission and vertex-deformed silhouette at beat 0 vs 0.5. It deliberately reports **inconclusive** if no real GPU, URP renderer or sample shader is present; skipping the shader is not evidence of compatibility. Run PlayMode tests on the configured graphics machine and retain Test Runner XML/logs. For repeatable automation, add `com.kieransimkin.danceflow.unity` to the fixture project's Package Manager `testables` list and execute `tools/benchmarks/Run-GpuReadbackTests.ps1` with the installed Unity Editor and project paths. The hardware GitHub Action does this automatically and rejects **inconclusive, skipped or undiscovered** tests.

The broader Phase 2 golden tests still cover fixed/variable source-bound BPM, signed pre-roll, 24-hour phase precision and lifecycle. A single rendered pulse test does not prove speaker-to-display timing or every graphics backend.

## CI and reproducibility

- Ordinary hosted GitHub Actions (`clock-contracts.yml`) runs existing .NET clock/encoding checks, Python shader contract checks and **benchmark analyser unit tests**, all without requiring Unity or a GPU.
- `unity-package-smoke.yml` is manual/opt-in licensed Editor integration testing; import/assembly errors must be fixed before performance acceptance.
- `hardware-benchmarks.yml` is manual-only and requires a **dedicated, trusted Windows GPU runner** labelled `self-hosted`, `windows`, `gpu`, with an activated Unity editor path `DANCEFLOW_UNITY_EDITOR` and a complete URP fixture project directory `DANCEFLOW_UNITY_PROJECT` set as runner environment variables. It builds a real Player, runs paired scenarios and uploads JSON reports. A GitHub environment named `hardware-benchmark` can require approval. Do not expose the runner to arbitrary untrusted PR code.

The reports record revision (supplied by runner), Unity patch version, CPU, GPU, graphics API, quality, resolution, VSync and object counts. The hardware workflow does not fabricate GPU utilisation or claim standalone release acceptance: **numerical performance evidence is one part of Phase 2 sign-off**, separate from real GPU shader readback and lifecycle tests.

## Known limitations

- No Unity executable or GPU is included in this patch; no real benchmark numbers are claimed.
- Time spent inside Unity's GPU driver/queues is platform-dependent. GPU frame time can be delayed by several frames or unavailable, notably on headless and some graphics backends.
- Video reserved/used profiler counters are estimates, not dedicated VRAM usage; unavailable counters are flagged.
- A global shader state is shared across cameras. Multi-song per-camera bindings, VFX buffer uploads and source motion recording integration are later phases.
- The benchmark runner uses a synthetic silence clock, not real audio sample presentation latency. Real speakers/displays require calibrated physical measurement.
- Benchmarks measure the workload of an actual Unity Player, but editor overhead, thermal state, operating system noise, resolution, GPU driver and SRP Batcher effects still need separate investigation.
