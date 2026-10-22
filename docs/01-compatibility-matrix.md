# DanceFlow Unity compatibility lock (Phase 1)

**Status:** Reviewed source contracts, 22 October 2026. This document records source files and **Git blob hashes** (not full repository commit hashes). Lock against upstream commits in the next integration/release gate; a Git blob hash pins exactly the file contents seen here. These source repositories may have moved since this review.

| Authority | Source | Git blob SHA-1 | Contract adopted |
| --- | --- | --- | --- |
| StemLab | `src/stemlab/beat_map.py` | `bf565922fe4cb6189c97e0df9a9cd27e691838b5` | `validate_map`, `phase_at_frame`, `export_document`; 64 pips/beat; 25k anchor max; source hash |
| StemLab | `docs/beat-timeline.md` | `b44fa38d1671f1269ef0bc0514d07cc563d509d2` | Frame anchoring, signed Beat 1 offset, extrapolation |
| FrameRudiments | `src/framerudiments/beat_timeline.py` | `77abd1cf87dca4016ff9670b1fce39f93f5ef65c` | Derived field validation, exact sync fraction, local BPM and nearest interval interpolation |
| FrameRudiments | `tests/test_variable_bpm_words.py` | `f84ee83bf08092838036cc013fa3c5007d45640e` | Synthetic 8000 Hz anchor fixture, Beat 1 later, source seek tests |
| DanceMoves | `docs/variable-tempo.md` | `b2393110d3343220ae453cca77880409be9fcca3` | 16 ticks/beat, pip-to-tick ratio 4, original song master timestamp ownership |
| Unity | Unity 6 package test and AudioSource documentation | Live Unity docs, no source pin | DSP scheduling and Unity Test Framework assembly definition conventions |

The frame anchor evaluation is:

```
frame = source_seconds * source_sample_rate
beat = beat_left - origin_beat
     + (frame - frame_left) * (beat_right - beat_left) / (frame_right - frame_left)
BPM = 60 * source_sample_rate * (beat_right - beat_left) / (frame_right - frame_left)
```

At an interior anchor, choose its *following* interval. Before first/after last, extrapolate using the nearest interval. Retain skipped beat ordinals (`0, 3, 4`) as gaps in observed detections; this is NOT an assertion that missing beats were detected. `origin_beat` references an existing marker and need not be an acoustically reviewed downbeat. `review_status=estimated` stays estimated.

`stemlab.beat-timeline.v1` is the only variable clock format in Phase 1. Do not treat `stemlab.beat-alignment.v1` as variable tempo. Captured `dancerudiments.recording` retains its own source clock in Phase 3.

## Fixture

`Tests/Fixtures/stemlab-frames-v1.json` is a **synthetic fixture** derived from the exact reference values in upstream FrameRudiments tests: 8000 Hz; anchors `[1000, 5000, 9000, 14160, 18080]`; Beat 1 is ordinal `2`. The `a...` and `b...` hashes are test identities **only**, not a real audio file or proof of SHA verification. Source beat at frame 0 is -2.25 (-144 pips), at frame 9000 is 0, at frame 11580 is 0.5, and local BPM in that segment is `480000/5160`.

The managed standalone harness compiles the exact runtime clock sources and checks fixture parity, bounds, playback seeks, negative indices and source/derived-field tampering without Unity dependencies. Unity EditMode/PlayMode suites supplement that harness.

## Precision policy

StemLab's canonical `phase_at_frame` uses exact rational arithmetic for integer frames. Unity's public clock uses double-precision interpolation; Phase 1 checks exact exported offsets using `BigInteger` fractions and tolerates floating phase error in runtime sampling. It does **not** claim bitwise parity of arbitrary fractional source seconds with Python's exact rational implementation. Continuous beat and FPS parity must be within documented numerical tolerances; no integer beat boundary should shift when sampling an exact source frame.

## Integration gaps to close before production

- Confirm current **upstream commit SHAs**, stable release versions and regenerate SHA-pinned golden fixture export directly from released StemLab (rather than a source-derived synthetic fixture).
- Audit actual DanceMoves JavaScript and FrameRudiments Python reference consumer parity on more than the included synthetic inputs.
- Verify Unity 6 editor compilation and EditMode/PlayMode tests on a licensed Unity runner; the .NET console CI is not equivalent to a Unity Editor import test.
- Source audio bytes must be supplied for SHA verification. A Unity `AudioClip` does not intrinsically preserve the original uploaded file bytes. Metadata provided by a caller cannot itself prove physical source identity without independently verified bytes.
- For very large source frame indices, verify tolerance near sample-frame boundaries across host platforms. Do not claim unlimited sub-frame precision from IEEE 754 binary64.


## Phase 3 recording reader (first implementation slice; 2026-10-22)

Strict source format pinned to canonical DanceRudiments files:

| Authority | Git blob SHA-1 |
| --- | --- |
| `DanceRudiments/docs/recordings.md` | `08864bf612cb4634664f440d0c83193456004bad` |
| `DanceRudiments/python/dancerudiments_authoring/recording.py` | `63a829d7046fdebb1978ad1baf3bf24a2b1256f0` |
| `DanceRudiments/include/dancerudiments/recording.hpp` | `624a4d5051d2d861eb20ed107663f23832297992` |
| `DanceRudiments/tests/python/test_recording.py` | `dc8c6db9c690a132f4fe5679d15908397f784141` |

The source schema remains `dancerudiments.recording` version 1 with exactly 64
pips per quarter-note beat. The Unity binary cache DFR1 is a **separate,
versioned private representation**, never a change to the canonical source JSON.

A synthetic checked-in four-channel sample and SHA-256 receipt cover source
BPM/phase provenance, signed start pip, 12-decimal float64 values, capture
bounds and the exact rational authoring clock. A standalone C# harness additionally
tests rejected malformed schemas, byte tampering, channel-order binding and
captures crossing 65,535 pips. This does not replace native C++ playback or
licensed Unity import/device acceptance; see [the Phase 3 specification](04-recording-format.md).
