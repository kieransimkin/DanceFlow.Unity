# DanceFlow Stage 4 — rigged character demos

Import the **Rigged Character Demos** sample using Unity Package Manager, then select
**Tools → DanceFlow → Stage 4 → Create Two Synthetic Skinned Rig Demos**.

The generator imports two independently hashed `dancerudiments.rig-motion` v1
fixtures and produces a scene containing two **real, differently rigged
`SkinnedMeshRenderer` objects** with per-bone skin weights:

- **Humanoid**: hips, spine, left and right arm; native 64-pip sampling and
  direct retargeted Transform playback.
- **Quadruped**: hips, spine, tail; an `AnimationScriptPlayable` job with
  synthetic per-bone motion and an explicit root-motion policy.

Both use a synthetic, silent, 120 BPM clock with supplied phase. The meshes,
profiles and recordings are purpose-generated procedural test assets. They are
**not** verified Rigify source captures, actual CMU clips, Kandi Boom, or the
song *Star Lights Glow*. Original song data is not bundled.

Generated meshes, materials and the generic avatar are saved as uniquely named assets under `Assets/DanceFlowRigDemos`; the scene retains them after reopening. Generation never overwrites an existing demo asset. The generic avatar binds its root-motion transform by bone name, as required by Unity.

The motion recordings last 1,024 pips (16 beats, 8 seconds at 120 BPM), are
finite and return to the neutral pose afterward. They do not loop. Time is
sampled from the existing `DanceFlowClockBehaviour` and remains seek-safe.

To play a real DanceRudiments Rigify capture, choose
**Assets → DanceFlow → Import DanceRudiments Rig Motion…** with a canonical
`dancerudiments.rig-motion` file. The imported `RigMotionAsset` references a
SHA-pinned profile and independently verified DFR1 recording cache. Configure
`RigMotionPlayerBehaviour` with the target SkinnedMeshRenderer, explicit bone
bindings, coordinate policy and reviewed approval. Imported Rigify **control**
channels cannot drive an unrelated deformation skeleton by name alone; retarget
and bake first.

## Kandi Boom (optional original character)

The `kandi-boom-target.json` manifest records the original source FBX hash,
expected 24 bone names, and the two separately packaged motions: internal
`boxbot` = CMU salsa `60_01`, `prism` = CMU ballet `05_18`.
These slot IDs are **not character identities**. Both use Kandi Boom's custom
skinned rig and have **time-only** motion, not established BPM/phase alignment.

The original FBX and texture maps are NOT distributed here. Import licensed
copies into your Unity project and select the root object, then choose
**Tools → DanceFlow → Stage 4 → Validate Selected Kandi Boom Target**.
Passing the bone audit alone does not establish source motion/target rest-basis
retargeting, correct FBX UV/texture mapping, or foot-contact correctness. The
source captures must be explicitly mapped to Kandi Boom and baked to a
compatible destination profile before rig playback.

See `docs/06-rigging.md` for channel contracts, source-to-target conversion,
root motion, animation blending, limitations and acceptance tests.
