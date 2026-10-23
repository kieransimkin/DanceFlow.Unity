# Kandi Boom WebGL — real audio and per-property rudiment matrix

This replaces the silent/synthetic WebGL preview with a **click-to-play** song
player and independently routable motion sources. The original, SHA-pinned Kandi
Boom model, PBR textures, skinned mesh and procedural landscape are unchanged.

## What is actually included

- **Arcadians**, Kieran Simkin. The exact publicly available StemLab MP3 is
  downloaded at a pinned commit, size/SHA-256 verified, then included only in
  the generated Unity WebGL example. Artist-confirmed 145 BPM, **unknown beat
  phase**. A fixed-tempo clock does not prove the candidate rudiments match.
- **The Truth is a Virus**, Kieran Simkin. Exact SHA-256-verified publicly
  available StemLab MP3. BeatThis + BeatNet **consensus estimate** from StemLab,
  sampled with the variable-beat `DanceFlowClockBehaviour` transport. Neither
  fitted BPM nor model-derived downbeat is artist-confirmed.
- **Star Lights Glow (Mystery Liquid Kick)** by Bass Ben & Friends. The original
  WAV SHA-256 is retained as the canonical lossless source in StemLab; its
  committed 192 kbps MP3 derivative is source-verified and playable in the demo.
  **BPM and phase have not been confirmed**: audio-derived envelopes are real,
  while the DanceRudiments motion loops use a visibly labelled 120 BPM preview
  pulse, not an asserted matching tempo. No artist-confirmed dance alignment.
- **Star Lights Glow (Anthem)** is not yet included because it is a distinct
  mix and no source master is available in StemLab. Never substitute the Mystery
  Liquid Kick mix or its analysis for the Anthem variant.

The generated Unity project has `Assets/StreamingAssets/Songs/catalogue.json`,
`rudiments.json`, three verified MP3 derivatives, and three source-bound `.signals.json`
files. Large media are **not included in the UPM or committed to this repository**.
`fetch_songs.py` downloads them during the GitHub Actions WebGL build, refusing
hash/size mismatches. GitHub Pages receives those assets only after the WebGL
player and public asset checks pass. An explicit post-build copy makes the
StreamingAssets URL files available at `/kandi/StreamingAssets/Songs/` even if
Unity's WebGL exporter packs its StreamingAssets into the player data bundle.

## Sources and provenance

The audio source lock lives in `tools/kandi-webgl/song-lock.json`. It pins
StemLab SHA, all three MP3 SHA-256s and sizes (plus the Star Lights Glow lossless
master fingerprint); the official DanceRudiments commit,
compiled initial-pack Git blob and selected pattern names; and, for The Truth
is a Virus, its frozen model-consensus beats SHA. The catalogue carries source
hashes, measured/detected BPM evidence and phase confidence separately.

The native DanceRudiments *compiled pattern samples* are used directly at
64 pips per beat; there is no hand-authored sinusoid passed off as a named
rudiment. The initial selection includes `groove_b_played`, `groove_b_grid`,
`lfo_morph`, `groove_a_played`, `lfo_breathe`, `lfo_flower`, `rhythm_euclid_5_12`,
`rudiment_six_stroke`, `ease_overshoot` and `rhythm_half_time`.

**Matching is NOT verified**: a catalogue pattern may be artist/algorithmically
selected as a candidate, but is not evidence that the motion matches an exact
stem, its beat origin, or a captured dancer performance. The matrix labels
this clearly and treats the source-specific envelopes as measured audio,
not inferred body movement. To claim song-matched motion instead, import a
reviewed, source-associated `dancerudiments.recording` / StemLab profile and
prove the beat/pip correspondence.

## How the modulation works

`fetch_songs.py` decodes the exact verified MP3 into stereo 8kHz float PCM
using the checksum-pinned imageio-ffmpeg 0.6.0 wheel executable (build time only)
and calculates a lightweight, deterministic set of **measured**
RMS envelopes (sub, bass, low-mid, mid, high, stereo width, onset change and
overall energy), at 40 ms intervals. This avoids unsupported WebGL real-time
PCM/FFT callbacks. The envelopes are stored with the exact source MP3 SHA.
At runtime they are linearly interpolated by *source seconds*, not by render
frame. Pattern samples are interpolated from the official precompiled pack at
`clock.Current.AbsoluteBeat * 64` (circular within each pattern period).

Each target property has its own independently selectable **audio band**,
**native rudiment**, **axis** and **gain**. The output is the pattern multiplied
by the song's measured energy for that band. The property matrix in the demo
covers grass wind, tree bend, terrain ripples, palette, star shimmer, helix
warping, Kandi hips, left arm, right arm, head, knees and fill light. The
patterns are distinct and may be switched from the on-screen scrollable mixer.
Kandi's original rest pose is the basis for every local bone rotation; angles
are intentionally constrained to mitigate body intersections.

The Unity project uses **one** DSP-based `DanceFlowClockBehaviour` and one
`AudioSource`. The browser's Play button is a real user gesture, required to
enable sound under autoplay restrictions. The authoritative musical clock owns
play/pause/resume/seek and phase; neither rig nor shaders start a second timer.

## CI and acceptance

The demo build uses **imageio-ffmpeg 0.6.0**, not an unpinned system decoder.
All six released platform-specific wheel SHA-256 values are pinned in
`tools/kandi-webgl/requirements-ffmpeg.txt`. On Linux CI the downloaded binary
comes from the hash-checked wheel and is cached through `setup-python`'s pip
cache. Installation cannot fall back to source distributions or Ubuntu `apt`.
The script `ffmpeg_runtime.py` rejects a system executable, an overridden
`IMAGEIO_FFMPEG_EXE` pointing outside the wheel or a mismatched wheel version;
`fetch_songs.py` invokes the verified executable using its absolute path.
The binary is **never** committed to the UPM or included in the WebGL release.
The bootstrap step has a seven-minute timeout and fails if installation or
validation cannot complete; the source MP3 hashes remain mandatory.

```powershell
python -m unittest discover -s tools/kandi-webgl/tests -p 'test_*.py' -v
# Requires Python >= 3.10 and network (first-time wheel download only).
python -m pip install --no-deps --only-binary=:all: --require-hashes -r tools/kandi-webgl/requirements-ffmpeg.txt
python tools/kandi-webgl/ffmpeg_runtime.py --check
# Requires network, ~20MiB audio plus pinned official pattern JSON:
python tools/kandi-webgl/prepare_project.py --repo . --output WebGLDemo
python tools/kandi-webgl/fetch_songs.py --output WebGLDemo/Assets/StreamingAssets/Songs
python tools/kandi-webgl/fetch_assets.py --output WebGLDemo/Assets/KandiBoomSource
```

Pushing the patch to `main` triggers the existing Unity WebGL GitHub Actions
workflow. Its build still needs a valid Unity Personal licence secret and
GameCI container. CI validates song hashes, official pattern pack identity,
source-bound envelope data, actual `.wasm`/`.data`/loader output and the presence
of the public song assets before GitHub Pages deployment.

Runtime browser acceptance must check audio actually plays after click, pause,
seek, song-switching, changes to all twelve property bindings, texture UVs,
rig interpenetration and shader deformation. Passing the Python source tests
or a Unity WebGL compile alone does **not** establish browser audio fidelity.

**Rights:** Public availability in the StemLab repository does not waive song
copyright. Publish only source MP3s that you have permission to redistribute;
adding Bass Ben & Friends masters requires explicit authorisation.
