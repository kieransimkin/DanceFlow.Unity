# DanceFlow for Unity

**Status: Phase 1 managed musical clock implemented; shader, recording and rigging phases remain planned.**

UPM ID: `com.kieransimkin.danceflow.unity` · Unity 6+ (`6000.0` in `package.json`).

[Comprehensive implementation plan](docs/00-implementation-plan.md) · [Upstream compatibility matrix](docs/01-compatibility-matrix.md) · [Clock API / usage](docs/02-clock-contract.md)

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

**Limitations:** The standalone managed tests do not prove Unity editor compilation, real-device audio latency or source-excerpt audio looping. Unity's minimal AudioSource adapter is for straight scheduled playback; rate changes and looping of the actual audio track need a later dedicated audio driver. Captured recordings, shader globals, and rigging belong to Phases 2–4.
