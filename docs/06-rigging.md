# Stage 4 — Rig profile import, retargeting and Unity Animation Playables

**Status:** Source implementation and synthetic demonstrations. Native Editor compilation,
GPU readback, saved-scene persistence and both synthetic skinning backends passed on
Unity 6000.6.7f1 / URP 17.6 / RTX 4060 Ti; see the [native record](11-native-acceptance.md).
Two *real sourced* skinned characters and their calibrated retargeting remain
acceptance gates. This does not claim the entire Stage 4 release is accepted.

**Version:** 0.5.0-preview.2 qualification slice.

## Canonical upstream and compatibility

- [`DanceRudiments/docs/rig-motion.md`](https://github.com/kieransimkin/DanceRudiments/blob/main/docs/rig-motion.md): Rigify controls, channel IDs, physical decode, quaternion grouping, source timing.
- [`DanceRudiments/python/dancerudiments_authoring/rigs.py`](https://github.com/kieransimkin/DanceRudiments/blob/main/python/dancerudiments_authoring/rigs.py): `dancerudiments.rig-profile` version 1, hierarchical `syn://` type URI and SHA-pinned canonical profiles.
- [`DanceRudiments/python/dancerudiments_authoring/motion.py`](https://github.com/kieransimkin/DanceRudiments/blob/main/python/dancerudiments_authoring/motion.py): `dancerudiments.rig-motion` schema 1 embeds profile and recording with `profile_sha256`.
- [`DanceRudiments/docs/mesh-demos.md`](https://github.com/kieransimkin/DanceRudiments/blob/main/docs/mesh-demos.md): original 24-bone Kandi Boom target and separately packaged CMU salsa/ballet source motions.
- [Unity 6 `AnimationScriptPlayable.Create`](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/animations/animationscriptplayable/create): animation job in a PlayableGraph.

All profile channel numbers are **one-based**, contiguous and permanent within a
versioned profile. Recording dimensions must exactly match ordered profile
channel names. Canonical profiles are SHA-256-identifiable by their exact source
UTF-8 bytes; embedded `rig-motion` documents require that SHA to match the
`profile_sha256` and canonical nested profile representation. Duplicate keys,
wrong schemas, missing quaternion wxyz components, invalid discrete selectors,
non-unit quaternions and out-of-bounds pips fail closed. Input documents have
explicit size limits; no arbitrary Python or animation expressions are executed.

The decoder applies the upstream transform:

```
physical = minimum + (encoded + 1) * (maximum - minimum) / 2
```

Continuous values interpolate linearly, integer/Boolean controls hold their
current pip, and complete wxyz quaternion groups use shortest-path SLERP. An
exact integer pip gives exact source physical channel values without retiming.
Captured recordings are finite and **never implicitly loop**.

## Public Unity components

- `RigProfileJson`, `RigMotionJson`, `RigPoseSampler`: managed source validation,
  channel-aware physical sampling and quaternion blending; no Unity dependency.
- `RigProfileAsset`, `RigMotionAsset`: metadata-only assets referencing validated
  original profile and the existing Phase 3 binary recording cache.
- `RigMotionImporter`: explicit Editor import of canonical rig profile or complete
  rig-motion wrapper. Import creates a new unique folder; it does not overwrite
  unrelated assets or download a model.
- `RigBoneBinding`: reviewed source control bone to actual target Transform,
  per-bone mask weight, translation scale, root-motion policy and basis correction.
- `RigMotionPlayerBehaviour`: reads **one** existing `DanceFlowClockBehaviour`
  state and maps it into native recording pips via `RecordingTimeMapping`.
- `RigMotionAnimationJob`: post-processes Unity `AnimationStream` values through
  `AnimationScriptPlayable`. An optional Animator Controller source supplies the
  base stream; `blendWeight × maskWeight` blends the rig pose into it.
- `RigMappingEditor`: proposes exact-name matches but deliberately clears
  `mappingReviewedAndApproved` before allowing playback.

Retargeted source control transforms are deltas relative to their declared
neutral values and the destination bone's captured rest local transform. The
basis policy is either explicit *already-retargeted Unity local* or Blender
Z-up → Unity Y-up with an optional per-bone reviewed correction.
Only complete `location`, `rotation_quaternion`, XYZ `rotation_euler` (source
`Rz * Ry * Rx`, not Unity's default ZXY `Quaternion.Euler`) and `scale`
vector groups are supported as direct transform channels. Unsupported Rigify
IK/FK/parent-selector custom properties are retained as source data, **not**
executed inside Unity. Bake those controls to a compatible deformation rig
before applying. Non-XYZ Euler rotations require a reviewed quaternion bake;
no incorrect implicit order conversion is attempted.

Root translation is independently controllable (`InPlace` versus
`ApplyRecordedTranslation`), preserving the target rest offset and scale.
Missing targets, duplicate bone assignments, mappings outside the selected
skinned mesh, incompatible channel lists, and unapproved associations refuse
playback before acquiring heavy assets. An exhausted recording clears animation
job weights or restores the direct rig's recorded rest transforms.

## Current sample and acceptance

The opt-in **Rigged Character Demos** sample generates two different real
`SkinnedMeshRenderer` hierarchies. Both use synthetic canonical rig-motion
fixtures with 1,024 pips and no imported songs. Humanoid uses direct Transform
playback; quadruped uses an animation job. These demos prove the pipeline can be
exercised without bundling other artists' copyrighted FBXs or recordings; they
are **not** sufficient evidence of CMU / Rigify / Kandi Boom conversion quality.

Stage 4 acceptance beyond this slice requires:

1. Editor import/reimport tests, asset move/prefab serialization and explicit
   source-hash/profile-hash/recording-hash mismatch rejection.
2. Two licensed real skinned character assets, preferably Kandi Boom and a
   verified exported Rigify rig, with exact target skeleton/rest calibration.
3. Per-pip physical pose parity against upstream native `RigMotionPlayer` and
   cross-frame/seek/variable-BPM parity against the authoritative music clock.
4. Playables graph smoke tests in the Unity Test Runner and GPU rendering
   confirming deformations rather than merely transform value changes.
5. Retargeted root motion continuity, constraint evaluation and foot-contact
   audits; optional IK tooling remains conditional on installed Animation Rigging.
6. Memory/CPU budgets and no implicit loading of Kandi textures/audio or optional
   CMU motion packages in ordinary Unity package installation.

## Run tests

```
python tools~/rigging-checks/generate_fixtures.py --verify
python -m unittest discover -s tools~/rigging-checks -p 'test_*.py' -v
dotnet run --project tools~/rigging-checks/RiggingChecks.csproj -c Release
```

Unit/contract tests do not substitute for a licensed Unity 6 build and actual
PlayMode test, or proof of the hardware GPU results. The standalone harness
uses only the existing managed clock/recording source and the Stage 4 models.
