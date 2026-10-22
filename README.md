# DanceFlow for Unity

**Status: Phase 1 clock, Phase 2 shader bridge, and a complete Phase 3 recording/motion implementation are present in source. Unity Editor/GPU acceptance remains pending.**

UPM ID: `com.kieransimkin.danceflow.unity` · Unity 6+ (`6000.0` in `package.json`).

[Comprehensive implementation plan](docs/00-implementation-plan.md) · [Upstream compatibility matrix](docs/01-compatibility-matrix.md) · [Clock API / usage](docs/02-clock-contract.md) · [Phase 2 shader API](docs/03-shader-api.md) · [Phase 3 recording import and sampling](docs/04-recording-format.md)

## Implemented Phase 1

- Fixed BPM and strict, audio-hash-bound `stemlab.beat-timeline.v1` variable BPM parser with 64-pip/16-tick parity and native source-frame interpolation.
- Immutable beat state, signed beats/ticks/pips, configurable meter, local BPM, and explicit source/excerpt coordinates.
- Manual / Unity DSP time sources; stop, pause, schedule, seek, playback rate, excerpt and looping controls.
- Range-based crossed-beat/bar/optional-phrase event scheduler and original-source cue scheduling with seek suppression and bounded catch-up.
- Optional Unity `DanceFlowClockBehaviour` and Editor diagnostics window.
- Unity NUnit suites, synthetic golden fixture, and standalone .NET 8 source-level CI harness.

## Install

In Unity Package Manager, **Add package from git URL**:

```
https://github.com/kieransimkin/DanceFlow.Unity.git
```

No heavyweight motion assets, native libraries or graphics-pipeline dependencies are bundled. The clock works independently of Shader Graph or the render pipeline.

## Usage

```csharp
using DanceFlow.Unity.Clock;

var manual = new ManualClockSource();
var transport = new MusicalTransport(new FixedBeatMap(128), manual);
transport.Play();
manual.Advance(0.25);
BeatState state = transport.Evaluate();
// state.AbsoluteBeat, state.Bpm, state.Tick16, state.Pip64, state.BeatPhase
```

To validate the managed core without a Unity license:

```
dotnet run --project tools/clock-checks/ClockChecks.csproj --configuration Release
```

**Limitations:** The standalone managed tests do not prove Unity editor compilation, real-device audio latency or source-excerpt audio looping. Unity's minimal AudioSource adapter is for straight scheduled playback; rate changes and looping of the actual audio track need a later dedicated audio driver. The Phase 2 shader bridge and first Phase 3 CPU recording slice are source-implemented; GPU motion buffers, full motion bindings and rigging remain outstanding.

## Phase 2 — GPU shader clock (source implementation)

Add `DanceFlowShaderBridge` alongside the Phase 1 `DanceFlowClockBehaviour` and assign the clock field. One active bridge publishes a render-frame snapshot to shared shader globals (`_DanceFlow_BPM`, `_DanceFlow_BeatPhase`, `_DanceFlow_BarPhase`, `_DanceFlow_BeatIndexHiLo`, etc.). Include `Runtime/GPU/DanceFlowClock.hlsl` for precision-safe beat cycles and Shader Graph File-mode functions. `ShaderClockMath` is pure managed C# and tested by the standalone clock harness.

Import the optional **Shader Clock (URP)** sample and choose **Tools > DanceFlow > Create URP Shader Clock Demo Scene** for a working vertex and fragment shader demo (generated within the Unity Editor). For opt-in frame/CPU/resource metrics use `DanceFlowShaderMetrics` and **Window > DanceFlow > Shader Clock Diagnostics**. The metrics report honestly distinguishes timings, graphics memory allocations and unavailable utilisation counters.

`python tools/shader-checks/check_shader_contract.py` performs static shader API, fixture and GUID validation. This does **not** compile Unity shaders. Licensed Unity Test Runner, URP GPU rendering and hardware benchmark results are still required for release acceptance. See [03-shader-api.md](docs/03-shader-api.md) for details and caveats.

## Real GPU acceptance and resource benchmarks

The new opt-in `DanceFlowShaderMetrics` capture adds per-frame CPU/GPU/GC/video-memory observations, type-7 p50/p95/p99 summaries, and explicit *unavailable* counters. For reproducible same-device A/B/C/D/M benchmark runs, generated URP scenes, Unity Player build scripts, independent Python analysis, and a real GPU readback test, use **[Phase 2 benchmarking and acceptance](docs/05-benchmarking.md)**. Benchmark baseline/effect shaders now use matching URP Unlit passes and a non-overlapping object grid. These are tools, not claims of verified GPU performance; actual Unity Editor/device runs and a licensed hardware runner remain necessary.


## Phase 3 — native recordings (first slice; 0.4.0 not released)

Canonical `dancerudiments.recording` schema 1 files are now parsed with a strict
64 MiB JSON limit, duplicate-key rejection, exact clock/metadata validation,
upstream float64 twelve-decimal normalisation, arbitrary named channels, and
non-looping signed native 64-pip boundaries. `RecordingBinaryCodec` preserves
row-major float64 values in an independently hash-verified DFR1 binary cache.

In Unity choose **Assets > DanceFlow > Import DanceRudiments Recording JSON...**
to create a metadata-only `MotionRecordingAsset` and a separate `.bytes` payload
under `Assets/DanceFlowRecordings/`, with unique non-overwriting paths.
`MotionTransformDriver` maps up to three named channels to local XYZ offsets;
association with the master source timeline or explicit beat retiming must be
acknowledged. These components consume the existing Phase 1 clock state, not an
independent delta-time clock. Multiple active drivers share one decoded cache.

```csharp
using DanceFlow.Unity.Motion;
MotionRecording data = asset.OpenVerified();
int bass = data.Descriptor.ChannelIndex("bass");
double value = data.SampleExact(data.Clock.StartPip, bass);
```

Unity-independent managed checks: `dotnet run --project tools~/recording-checks/RecordingChecks.csproj -c Release`.
Independent fixture evidence: `python tools~/recording-checks/verify_fixture.py`.
See [Phase 3 recording implementation and known gaps](docs/04-recording-format.md).
GPU recording buffers, further property bindings, optional chunk streaming and
licensed Unity performance tests remain future Phase 3 work. Package version
remains `0.3.0` until acceptance, and no real GPU performance is claimed.


## Phase 3 — complete source implementation

Phase 3 now covers the full planned recording-and-motion layer for DanceFlow.Unity:

- strict canonical `dancerudiments.recording` import, lossless DFR1 cache and deterministic float64 CPU sampling;
- explicit retiming between the master song clock and recording clock with no hidden looping or phase invention;
- typed motion bindings for Transforms, Lights, ParticleSystem emission and Renderer material/property updates;
- global four-channel motion signal publication for shaders, plus an optional bounded GPU mirror of entire recordings for shader sampling;
- a **Song Reactive Landscape** sample that procedurally generates a tree-and-grass environment, palette-shift effects and geometry-warp sculpture driven by motion signals.

The sample catalogue includes presets for **Arcadians**, **The Truth is a Virus**, and two **Bass Ben & Friends — Star Lights Glow** variants. These sample manifests deliberately ship without copyrighted song audio or song-specific recordings; they provide scene generation, provenance, expected song hookups and synthetic preview behaviour until you wire verified local assets.
