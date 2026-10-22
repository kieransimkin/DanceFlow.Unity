# Phase 1 musical clock and transport API

## Ownership and coordinates

Namespace: `DanceFlow.Unity.Clock`, zero mandatory third-party runtime dependencies. Timing is entirely derived from `IClockTimeSource.NowSeconds` and the current `IBeatMap`, never accumulated from `Time.deltaTime`.

- `SourceSeconds`: original master source seconds. Lyrics and cues should stay in this coordinate.
- `AbsoluteBeat`: quarter-note beats relative to the confirmed/estimated Beat 1 origin. Signed, continuous, double precision.
- `ContinuousBeat`: across source loops, adds the exact loop beat-span per iteration so schedulers can cross seam boundaries. This does NOT change `AbsoluteBeat`/recording source identity.
- `Tick16=floor(16*AbsoluteBeat)` and `Pip64=floor(64*AbsoluteBeat)`; all indices use mathematical floor, including negative time.
- `BeatPhase`, `TickPhase`, `PipPhase`, `BarPhase` are continuous fractions in `[0,1)`.
- `BarIndex=floor((AbsoluteBeat-BarOriginBeat)/BeatsPerBar)`; beats per bar 1..64, default 4. Meter is not inferred by StemLab.
- `ContinuityGeneration` changes only on musical-coordinate jumps (seek/map/excerpt/loop/meter/stop); it lets the scheduler honour pause/resume without treating these as seeks. `TransportGeneration` changes on explicit stop, seek, tempo map replacement, meter/rate/excerpt/loop change, pause/resume, scheduled start. Consumers can suppress cue bursts on discontinuities.
- `LoopIteration` counts completed loops since the current transport anchor; a manual seek resets that count.

## Fixed and variable map

```csharp
IBeatMap fixedMap = new FixedBeatMap(bpm: 132, beatZeroSeconds: 0.17);
BeatPosition at = fixedMap.Evaluate(sourceSeconds: 2.5);
```

Variable maps require the complete exported StemLab JSON **and verified master bytes**:

```csharp
using (var originalMaster = File.OpenRead("path/to/original-master.wav"))
{
    var identity = SourceAudioIdentity.FromAudioBytes(
        originalMaster, sampleRate: 48000, frames: 4800000, channels: 2);
    var map = StemLabBeatTimeline.Parse(File.ReadAllText("beat-timeline.json"), identity);
}
```

`FromAudioBytes` hashes the entire byte stream with SHA-256 and restores the stream position. Its sample rate/frame count/channel values must come from a trusted audio metadata reader; it does not parse or decode the audio. The importer rejects source-hash mismatch, missing consensus identity, frame mismatches, invalid anchor ordering, unsupported status/schema, fabricated detected references, JSON duplicate keys, malformed rational offsets, and conflicting optional exported derived fields. Input is capped at 32 MiB.

## Pure transport

```csharp
var clock = new ManualClockSource();
var transport = new MusicalTransport(new FixedBeatMap(120), clock);
transport.SetMeter(3);
transport.Play();
clock.Advance(0.5);
BeatState state = transport.Evaluate(); // beat 1 at 120 BPM
transport.SeekBeat(12);
transport.SetPlaybackRate(0.5);
transport.SetExcerpt(0, 40);  // original source seconds
transport.SetLoop(4, 8);      // original source seconds
```

`PlayScheduled(clockSeconds)` refers to the same absolute time domain as the supplied `IClockTimeSource`; it should be driven by a Unity `AudioSettings.dspTime` source when scheduling an `AudioSource.PlayScheduled` call. Before the scheduled start the transport reports `IsPlaying=false` and holds source time; it becomes playing at that DSP timestamp. Pausing does not reset source position; seeking does not modify audio files or rewrite cue/lyric times.

Source loop playback is implemented on the **clock**. Unity's minimal `DanceFlowClockBehaviour` does not yet splice and schedule real audio loop excerpts: it is the consumer's responsibility to keep actual audio transport aligned if using the loop API. Use the component's `PlayScheduled`, `Pause`, `Resume` and `SeekSourceSeconds` for straight source-clip playback. `AudioSource.time` seeks may be approximate for compressed media. `AudioSource.pitch` and arbitrary audio-rate/time-stretch synchronization are not automatically managed by the behaviour in Phase 1; setting core `PlaybackRate` alone does not retime Unity audio.

## Boundary scheduler

```csharp
var events = new MusicalScheduler();
events.Advance(state, e => {
    if (e.Kind == MusicalBoundaryKind.Beat) { /* beat */ }
    if (e.Kind == MusicalBoundaryKind.Bar)  { /* bar */ }
});
```

Set `PhraseBars` to a positive number to emit optional `Phrase` boundaries at that many bars; zero disables phrases. `Bar` marks the downbeat of the configured meter grid, not proof of an acoustically reviewed song downbeat.

The first snapshot establishes a baseline. Subsequent updates enumerate **crossed integer beats** and **crossed meter-based bar boundaries**, even when one frame spans several of them. Ordering is by continuous beat, with beat then bar at ties. A backward step is suppressed. Seek generations suppress catch-up by default; `SeekEventPolicy.ReplayForward` is an explicit opt-in. `AllBounded` returns at most 512 entries per step and aggregates large gaps into last-boundary events with skipped counts. A seek never spams thousands of callbacks by default.

Original-source cue scheduling is available independently through `SourceCueScheduler`: supply data-only `SourceCue(sourceSeconds, name)` entries and subscribe to its `Advance` callback. It follows source excerpt loops and reports `DroppedCues` under large frame stalls. A cue at the exact excerpt start is dispatched when the transport first begins playing, provided the scheduler was seeded with a stopped initial snapshot. `DanceFlowClockBehaviour.SetCues` primes this baseline and exposes the scheduler through `CueCrossed`. Initialising a fresh scheduler only after playback has advanced establishes the current state as its baseline instead of replaying earlier cues. Phrase events are configured via `SetPhraseBars`. Source-verified acoustic downbeat annotations and transport-to-audio drift reconciliation remain separate adapters; these must not infer cue locations from unreviewed beat anchors.

## Unity behaviour

Add `DanceFlowClockBehaviour` to a GameObject with an `AudioSource`, configure the nominal fixed BPM, and enable `playOnStart` or call `PlayScheduled()`. Other components read `Current` and subscribe to `BoundaryCrossed`. Use `UseStemLabTimeline(json, originalMasterStream, sampleRate, frames, channels)` only with the original byte stream. Invalid imports leave the existing map unchanged. Open **Window > DanceFlow > Musical Clock Diagnostics** to view live source/beat/tick/pip/bar positions.

If Unity's audio device or sample rate changes, the component pauses transport; rescheduling audio is explicit. Multiple independent instances are supported and no global static mutable clock is installed in Phase 1.

## Test commands

```sh
dotnet run --project tools/clock-checks/ClockChecks.csproj --configuration Release
```

In Unity, add package tests to `testables` if installed through Git rather than embedded, then run the EditMode/PlayMode Test Runner suites. See [Unity's package testing guide](https://docs.unity.com/en-us/engine/6000.0/manual/packages-list/cus-pkg-development/cus-tests).
