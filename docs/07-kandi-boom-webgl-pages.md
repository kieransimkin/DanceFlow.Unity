# Real Kandi Boom in DanceFlow.Unity WebGL (GitHub Pages)

**Source implementation:** `.github/workflows/unity-webgl-pages.yml` and `tools/kandi-webgl/`.

This demo uses the **original Kandi Boom** from [DanceRudiments](https://github.com/kieransimkin/DanceRudiments/tree/main/harness/kandi-boom), not a procedural human-looking stand-in. Assets are downloaded from an immutable source commit and checked against the upstream SHA-256/byte-size audit before Unity can import them. Do not substitute a mesh if an asset is missing: build must fail.

## Expected URLs after first successful deployment

- GitHub Pages portal: `https://kieransimkin.github.io/DanceFlow.Unity/`
- **Actual Unity WebGL player:** `https://kieransimkin.github.io/DanceFlow.Unity/kandi/`
- Verified asset receipt: `https://kieransimkin.github.io/DanceFlow.Unity/kandi-source-receipt.json`

**These URLs are not confirmed live until the workflow succeeds and Pages is enabled.** GitHub does not publish from `Samples~` without a WebGL player build.

## Required GitHub setup

1. In repository **Settings → Pages → Build and deployment**, select **GitHub Actions** as the source.
2. Configure Unity CI license/activation secrets as appropriate for the GameCI Unity 6 setup (`UNITY_LICENSE` or `UNITY_SERIAL`; if your activation method requires them, `UNITY_EMAIL` and `UNITY_PASSWORD`). Do not put credentials in source or artifacts.
3. Enable the **WebGL Build Support** module for the Unity version used in the workflow. GameCI's WebGL editor container must actually support `6000.0.23f1` and URP 17.0.3.
4. Ensure you have permission to publicly exhibit Kandi Boom’s FBX/textures. Being visible in a public repository doesn't itself grant third parties redistribution rights.
5. Run workflow [Unity WebGL — original Kandi Boom GitHub Pages](../.github/workflows/unity-webgl-pages.yml) via **Actions → Run workflow** (or push changes to relevant paths on `main`).

The build **fails** when Unity licensing, WebGL target, source hashes, original rig or WebGL payload are missing. No static visual stand-in is published in its place.

## What the first build demonstrates

- Hash-pinned **original FBX** (`kandi-boom.fbx`, 8,563,852 bytes) and original `albedo.png`, `normal.png`, `metallic.png`, `roughness.png` from DanceRudiments.
- Unity importer configured for up to **10 bone weights per vertex**, retaining more of the actual rig's deformation than a mandatory four-weight reduction. Device GPU limits still require empirical checking.
- Native Kandi 24-bone transform hierarchy verified at build time (22 skin-influencing bones plus two zero-influence head helpers), one skinned mesh, at least 29,000 triangles and live UV geometry.
- PBR material with original UVs preserved; metallic and roughness converted to the URP Lit metallic R/smoothness A channels. No unverified UV flipping.
- Phase 1 DanceFlow clock drives labelled **synthetic** beat preview poses directly on the original skeleton. Clock state also drives Phase 2 shader globals and Phase 3 synthetic motion signal bus.
- Phase 3 procedurally generated trees/grass, palette shifting and geometric warping surrounding Kandi Boom.
- Browser controls for play/pause, BPM and camera orbit.

### Motion provenance

The source DanceRudiments Kandi demo uses *boxbot* as the internal CMU salsa `60_01` slot and *prism* as the ballet `05_18` slot. Both animate Kandi, **not two different characters**. Their captured clocks are time-only; do not pretend they are matched to *Star Lights Glow*. This first Unity scene demonstrates genuine character skinning and beat-synchronised synthetic movement. Integrating the real CMU motion into Unity requires a reviewed source-to-Kandi rest-basis retarget, skeleton-height translation scaling and foot-contact tests. No CMU motion clips or song audio are bundled.

## Local verification (without Unity)

```powershell
python -m unittest discover -s tools/kandi-webgl/tests -p "test_*.py" -v
python tools/kandi-webgl/prepare_project.py --repo . --output WebGLDemo
python tools/kandi-webgl/fetch_assets.py --output WebGLDemo/Assets/KandiBoomSource
```

The asset fetch step requires internet access and downloads about 29 MiB. It refuses mismatched bytes and existing conflicting files. `WebGLDemo` is generated, **not committed**; it contains source assets and an embedded snapshot of the UPM package. Do not include those downloaded binary assets inside the default Unity package.

For a local Unity run, open `WebGLDemo` with Unity `6000.0.23f1` including WebGL Build Support, then use **Tools → DanceFlow → WebGL → Create Verified Kandi Boom Scene**. The automated build method is `DanceFlow.Unity.WebGLDemo.Editor.KandiBoomWebGLBuild.Build`.

## Acceptance gates

- Asset manifest and audit hashes verified against pinned upstream.
- A genuine `SkinnedMeshRenderer` with Kandi's original 24 bone transforms imports; mesh and UV count checked.
- Unity 6 Editor compiles and builds the WebGL player.
- Browser/Chromium smoke test visibly shows correct Kandi textures, moving skin, dynamic vegetation and HUD.
- GPU/web loading tested on actual browser and device; visual clipping and UV orientation are not certified by static source tests.
- GitHub Pages deployment completes; its URL appears in the workflow's `deploy-pages` job.
