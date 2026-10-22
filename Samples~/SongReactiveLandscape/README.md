# Song Reactive Landscape

This sample procedurally generates a landscape with:

- animated grass and tree canopies driven by motion-derived wind/gust channels;
- palette-shift and shimmer effects driven by higher-frequency motion channels;
- terrain ripple and abstract geometry-warp effects driven by beat and motion;
- presets for **Arcadians**, **The Truth is a Virus**, and **Bass Ben & Friends – Star Lights Glow**.

The package does **not** include copyrighted song audio or song-specific motion captures. It ships only the scene generator, provenance manifest and synthetic preview behaviour.

Menu entries:

- `Tools > DanceFlow > Samples > Create Star Lights Glow Landscape`
- `Tools > DanceFlow > Samples > Create Arcadians Landscape`
- `Tools > DanceFlow > Samples > Create The Truth is a Virus Landscape`
- `Tools > DanceFlow > Samples > Create Synthetic Motion Landscape`

To convert a preview scene into a matched scene:

1. Import or assign the exact local song audio on the generated scene's `AudioSource`.
2. Set the reviewed BPM where required on `SongShowcaseBootstrap`.
3. Import a canonical `dancerudiments.recording` JSON asset.
4. Assign that asset to `MotionSignalBus` and acknowledge the source association or explicit retiming mode.
5. Optionally enable `MotionRecordingGpuBridge` if you want shaders to sample the full recording buffer directly.

The on-screen overlay clearly states whether the scene is using a real matched recording or a synthetic/unmatched preview.
