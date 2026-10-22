# Shader clock — URP vertex/fragment demo

1. Install **Universal Render Pipeline** and assign its pipeline asset under Project Settings > Graphics.
2. In Package Manager > DanceFlow for Unity > Samples, import **Shader Clock (URP)**.
3. Choose **Tools > DanceFlow > Create URP Shader Clock Demo Scene**. This creates a scene and material under `Assets/DanceFlowShaderClockDemo` (it never overwrites an existing material).
4. Press Play. The silently advancing Phase 1 DSP-backed 120 BPM clock animates the cube and sphere together, with vertex deformation and fragment emission driven by exactly the same GPU globals.
5. To use a real song, attach an `AudioSource` with that clip to `DanceFlowClockBehaviour`, remove `SilentDemoClock`, and call `PlayScheduled` from your song controller. The synthetic demo's silent clock does **not** imply audio has been analysed or beat-aligned.

You can scrub with `bridge.PublishAtSourceSeconds(t)` when a loaded Phase 1 `MusicalTransport` is present, or call `bridge.Publish(BeatState)` for offline rendering. Call `bridge.ReleaseManualOverride()` to return to real-time mode.

All package runtime code is render-pipeline agnostic. This sample shader uses URP's `Core.hlsl` and therefore depends on URP, but the base UPM package does not.

The generated scene also includes `DanceFlowShaderMetrics` with all capture settings **off by default**. Enable timings in its inspector if desired; export manually from Window > DanceFlow > Shader Clock Diagnostics.

For Shader Graph's File-mode Custom Function node, see `docs/03-shader-api.md` and `Runtime/GPU/DanceFlowClock.hlsl`.

## Matched performance baseline

`BeatPulseBaseline.shader` is installed alongside `BeatPulseVertex.shader`.
Both use an identical URP Unlit pass and matching attributes/material parameters;
the baseline does not read the DanceFlow shader clock or displace vertices.
The benchmark scene generator requires **both** shaders. This prevents
comparing Unity's more expensive URP Lit material against DanceFlow's Unlit
material and misattributing the result to the musical clock. The measured
shader delta can still include extra vertex displacement and screen-space
coverage; this is a workload comparison, not a GPU instruction microbenchmark.
