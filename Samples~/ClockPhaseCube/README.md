# Beat phase cube (Phase 1)

Import this sample from the DanceFlow.Unity package's **Samples** tab. In a Unity scene:

1. Add an `AudioSource` containing a locally provided music clip to a GameObject.
2. On that GameObject attach `DanceFlowClockBehaviour`, select the source, set nominal BPM, and enable **Play On Start**.
3. Create a Cube, add `ClockPhaseCube`, and assign the clock component.
4. Enter Play mode. The cube pulses on each quarter-note beat; changing the `BeatPhase` math will alter the movement envelope.
5. Open **Window > DanceFlow > Musical Clock Diagnostics** to inspect current BPM, beat/tick/pip and bar phase.

This sample intentionally does not bundle third-party audio, source motion, shaders or meshes. Unity default primitives are sufficient. For variable tempo, import a full StemLab timeline using `UseStemLabTimeline` with the independently verified original master bytes and audio metadata.
