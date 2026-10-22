# Phase 2 — GPU musical clock, shader API and performance evidence

**Status:** Source implementation provided for review; Unity import, GPU rendering and licensed hardware benchmarks **must still be executed** before a 0.3.0 release.
**Based on:** Phase 1 published commit `0d7328cec185494cbed891e301c223df617d1849`.
**UPM:** `com.kieransimkin.danceflow.unity` · Unity 6.0+ · URP sample optional.
**Contracts:** [implementation plan](00-implementation-plan.md) · [compatibility](01-compatibility-matrix.md) · [Phase 1 clock](02-clock-contract.md).

## Purpose

`DanceFlowShaderBridge` publishes an immutable sample **of the existing Phase 1 `MusicalTransport`**. It does not implement a new musical oscillator, beat detection, audio scheduling, cue handling or recording interpolation. CPU and GPU renderers agree on the same source timestamp, including source-verified StemLab `stemlab.beat-timeline.v1`, fixed BPM, negative pre-roll and arbitrary bar origin. Subsequent Phase 3 GPU motion buffers must reference this state rather than another clock.

The plugin's core is independent of URP and HDRP. The URP-only sample shader and generator live under `Samples~/ShaderClockURP`, not in the base runtime. No media, motion library, character assets or model weights are included.

## Install and attach

1. Install DanceFlow.Unity from the package's Git URL, or use the local embedded package while developing.
2. Add a `DanceFlowClockBehaviour` to a GameObject and configure its transport as described in [02-clock-contract.md](02-clock-contract.md).
3. Add a `DanceFlowShaderBridge` component to the same GameObject, assigning the explicit clock reference. Only **one** enabled bridge should own the process-wide globals; duplicate bridges are disabled with an error. For multiple independent songs use separate local material/instance state when implemented; do not let different cameras race to set the same globals.
4. For a URP demonstration, import **Shader Clock (URP)** from Package Manager > Samples and select **Tools > DanceFlow > Create URP Shader Clock Demo Scene**. It creates a new scene under Assets rather than shipping an unverified YAML scene. Press Play for a silent 120 BPM display. This demo is not proof of synchronisation with a real audio master.
5. Inspect **Window > DanceFlow > Shader Clock Diagnostics**. You can also call `bridge.PublishAtSourceSeconds(t)` to scrub a loaded source map without playing audio. Release with `bridge.ReleaseManualOverride()`.

## Authoritative phases and render ordering

`DanceFlowClockBehaviour.Update()` still owns beat/cue event delivery. The bridge obtains its own *read-only* state with `MusicalTransport.Evaluate()` in LateUpdate or the camera-render callback, never advancing Phase 1 event cursors. The default policy caches the **first sample per Unity `Time.frameCount`**; a multi-camera frame therefore has one uniform state. An explicit offline `Publish(BeatState)` overrides that automatic policy until released. The bridge is deliberately not a source of audio-to-display latency estimates. It also does not schedule/correct real audio excerpt loops or playback-rate changes.

- URP/HDRP: `RenderPipelineManager.beginCameraRendering` (before drawing the camera); an optional LateUpdate publication prepares cameras and non-camera GPU consumers.
- Built-in: `Camera.onPreCull`; LateUpdate also publishes.
- Editor: optional silent fixed-BPM preview, disabled by default. Explicit `Publish` can load a manually evaluated variable-tempo `BeatState` for deterministic offline renders.
- Pause: holding source seconds holds the phase; beat pulses are shader functions of *position*, not events in a timer.
- Seek/map change/loop: the next published sample jumps to the mathematically correct state, and the transport generation is transmitted for future temporal-history reset handling.
- Pipeline switching, multiple cameras, Scene view and manual Camera.Render are subject to Unity's render callback behaviour and need actual Editor/device tests. Multi-song global contexts are explicitly unsupported in this initial bridge.

## Shader property contract (API v2)

All parameters are **global shader properties**, not material `Properties` fields. They are published using `Shader.SetGlobalFloat` and `Shader.SetGlobalVector` with cached property IDs. Names are case sensitive.

| Property | HLSL type | Meaning |
| --- | --- | --- |
| `_DanceFlow_ShaderApiVersion` | float | 2 when a bridge has published; 0 when disabled |
| `_DanceFlow_BPM` | float | Local evaluated BPM at the source position |
| `_DanceFlow_BeatPhase` | float | Fractional quarter-note beat in `[0,1)` |
| `_DanceFlow_BarPhase` | float | Phase relative to configured meter and bar origin |
| `_DanceFlow_TickPhase` | float | Fraction inside current DanceMoves 1/16-beat tick |
| `_DanceFlow_PipPhase` | float | Fraction inside current DanceRudiments 1/64-beat pip |
| `_DanceFlow_BeatPulse` | float | `(1 - beatPhase)^8` |
| `_DanceFlow_DownbeatPulse` | float | `(max(0,1 - barPhase * beatsPerBar))^8` |
| `_DanceFlow_BeatSin`, `_DanceFlow_BeatCos` | float | `sin(2π phase)`, `cos(2π phase)` |
| `_DanceFlow_Meter` | float | Quarter-note beats per bar, 1..64 |
| `_DanceFlow_IsPlaying` | float | 0 or 1 transport playing indicator |
| `_DanceFlow_BeatIndexHiLo` | float4 | x = signed high chunk (floor(index/65536)); y = 0..65535 low chunk |
| `_DanceFlow_BeatRemainder256` | float | Absolute beat position modulo 256, informational only |
| `_DanceFlow_SourceSecondsHiLo` | float4 | x = float(high source seconds), y = float residual |
| `_DanceFlow_TransportGenerationHiLo` | float4 | transport generation split into the same integral radix |

`ShaderBeatDomain.Source` (default) encodes `BeatState.AbsoluteBeat`: source loops repeat phase. `ShaderBeatDomain.Continuous` encodes `BeatState.ContinuousBeat`: native loop beat spans accumulate. Source seconds always refer to the original master, irrespective of the selected beat domain.

The counters remain integer-exact up to an approximate bound of ±2^40 beats, after which packing explicitly fails instead of silently returning the wrong count. Float fractional phase has normal 32-bit conversion error but does **not** inherit the error from representing an ever-growing absolute beat as one float. The code does not claim exact GPU equality on all targets: compiler precision rules and half-float operations can vary. Use float precision in Shader Graph for timing, not half when precision matters.

### HLSL functions

Include using the UPM package path:

```hlsl
#include "Packages/com.kieransimkin.danceflow.unity/Runtime/GPU/DanceFlowClock.hlsl"

float pulse = _DanceFlow_BeatPulse * DF_ClockValid();
float barPhase = _DanceFlow_BarPhase;
float threeBeatCycle = DF_CyclePhase(3.0);
float eighthNotes = DF_SubdivisionPhase(2.0);
float offsetBeat = DF_BeatPhaseOffset(0.25);
```

`DF_CyclePhase(N)` accepts **integral** periods of 1..256 beats, including 3, 5, 7, 255 and negative pre-roll, using split integer modular arithmetic. For fractional divisions of a beat, use `DF_SubdivisionPhase(divisionsPerBeat)`. `DF_PulseEnvelope`, `DF_SwingPhase`, and the `DF_GetVertexBounce_*` functions provide simple sample effects. This is a compact reference shader API, not a GPU motion-recording interface (Phase 3).

### Shader Graph File-mode Custom Function nodes

Unity Shader Graph supports HLSL `File` functions named with `_float` or `_half` suffixes. Choose Source `Runtime/GPU/DanceFlowClock.hlsl`, select precision **Float** and use these names **without** the suffix:

| Graph Node Name | Inputs | Outputs |
| --- | --- | --- |
| `DF_GetClock` | none | `BeatPhase`, `BarPhase`, `BPM`, `BeatPulse`, `DownbeatPulse` (float) |
| `DF_GetCycle` | `PeriodBeats` float | `Phase` float |
| `DF_GetPulse` | `OffsetBeats`, `Exponent` float | `Pulse` float |
| `DF_GetVertexBounce` | `PositionOS`, `NormalOS` float3; `Amplitude` float | `DisplacedPositionOS` float3 |

Make reusable Shader Graph **Sub Graph** assets from these nodes in your own project. We deliberately have **not** manufactured serialized `.shadersubgraph` files without a Unity Editor version to verify them; the distributable file-mode functions work in URP's supported Shader Graph versions. The URP sample's ordinary `.shader` uses the same included HLSL functions in real vertex and fragment code.

## Deterministic parity and testing

The .NET harness `tools/clock-checks/ClockChecks.csproj` compiles the pure `ShaderClockMath.cs` alongside the Phase 1 clock and checks fixed/variable timing, negative indices, source/continuous loops, signed split counters and long-session 1–256-beat cycles. `python tools/shader-checks/check_shader_contract.py` validates fixtures, HLSL names, shader references, package metadata and Unity GUID uniqueness with no external dependencies.

Run the manually dispatched **Unity package smoke tests (licensed)** workflow after configuring GitHub Unity licence secrets and an available Unity 6 editor image. GameCI relocates the root UPM package into a temporary subdirectory, as required by its package-mode runner. In Unity run EditMode and PlayMode Test Runner suites (tests are already split into assemblies). GPU screenshot/render readback and Shader Graph compilation should be tested in a **licensed Unity 6 Editor with URP actually installed**. Static tests and a .NET calculation do not prove ShaderLab compilation or audio/display precision on a real GPU.

Testing matrix before declaring Phase 2 complete:

1. Fixed BPM 120, 128 and 145; verified StemLab irregular anchors at exact, inter-anchor and extrapolated positions.
2. Signed pre-roll, non-integer bar origin, arbitrary 3/4, 4/4 and 7/4 measures.
3. Pause, restart, random-access backward seek, transport discontinuity, source and continuous loop modes.
4. Long clocks (~10^8 beats), periods 3, 5, 7, 16, 64, 255 and 256, GPU phase tolerance `5e-5` cycles against CPU golden model.
5. URP vertex bounce and fragment emission in the same material, multiple cameras in the same frame, Scene view, EditMode/manual render, cleanup of shader state upon disabling.
6. Device tests: Windows D3D11/D3D12, Vulkan, Metal (where hardware available), plus mobile if target support is announced. Specify which were run; don't extrapolate from a single GPU.

## Opt-in performance and resource monitoring

`DanceFlowShaderBridge` has **Measure Publisher CPU Time** off by default. When enabled it reports publish-only elapsed CPU time (microseconds) using `Stopwatch`. Its CPU calls are wrapped in `ProfilerMarker`s: `DanceFlow.Clock.RenderEvaluate`, `DanceFlow.GPU.EncodeState` and `DanceFlow.GPU.PublishGlobals`. Instrumentation is not the same as whole-system CPU utilisation.

`DanceFlowShaderMetrics` is an optional component. Enable **Capture Frame Timings** to sample `FrameTimingManager` every N frames (default 4), using one preallocated `FrameTiming` array. It can report available CPU/GPU frame time in ms, Unity reserved memory and graphics-driver allocated memory (latter only in Editor/development builds). No background work, network or automatic disk writes occur. The Shader Clock Diagnostics window has an explicit JSON export control.

Memory readings belong to **the Unity process or graphics driver**, not the package alone. GPU frame time is **not GPU core utilisation** and a zero/unavailable metric is marked unavailable in the exported JSON. The portable APIs do not provide general GPU VRAM-free values, GPU occupancy or whole-process CPU percentage. Monitor the baseline scene and the same scene with DanceFlow enabled at the same hardware/settings for incremental cost; archive per-device JSON with graphics API, Unity version, enabled recording options and number of cameras/objects.

No actual hardware timing figures are claimed in this patch. Reported optimisations require measured before/after data in the same environment; the `0.3.0` performance gate is still awaiting real Unity tests.

## Release limitations and follow-up

- Base package has one global master clock owner. Multiple independent clocks/cameras need explicit per-renderer or per-pass buffer integration; globals cannot independently represent two songs in the same draw.
- Hardware-independent sample-exact audio-to-photon synchronisation is not claimed. Rendering/presentation latency must be measured and documented per target.
- Source-only sample sync maps are validated in the managed clock. The sample without media has no beat provenance beyond its chosen nominal BPM.
- This patch does **not** generate actual `.shadergraph` or `.shadersubgraph` assets, an automated URP sample compile in the licensed smoke workflow, nor render-back comparisons on Unity GPUs; these are acceptance follow-ups before Phase 2 release sign-off.
- Phase 3 supplies GPU-buffer recording sampling; Phase 4 supplies rigging. Neither is smuggled into this package's shader clock core.
