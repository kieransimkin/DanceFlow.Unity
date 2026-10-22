"""Source-level contract checks; these do NOT compile Unity or prove GPU rendering."""
from pathlib import Path
import unittest

TOOLS = Path(__file__).resolve().parents[1]
ROOT = TOOLS / "UnityProject" / "Assets"

class WebGLUnityContractTests(unittest.TestCase):
    def test_uses_original_fbx_and_source_verified_weights(self):
        code = (ROOT / "Editor" / "KandiBoomWebGLBuild.cs").read_text()
        for literal in (
            'kandi-boom.fbx', 'kandi-boom.fbx', 'SkinnedMeshRenderer',
            'ModelImporterSkinWeights.Custom', 'importer.maxBonesPerVertex = 10',
            'skins[0].bones.Length < 22', 'PrefabUtility.InstantiatePrefab',
            'InvalidDataException', 'SHA256.Create()',
            'OriginalTexture("albedo.png")', 'OriginalTexture("normal.png")',
            'OriginalTexture("metallic.png")', 'OriginalTexture("roughness.png")',
            '255 - roughPixel[i].r', 'QualitySettings.skinWeights = SkinWeights.Unlimited',
        ):
            self.assertIn(literal, code)
        self.assertNotIn('GameObject.CreatePrimitive', code)

    def test_uses_danceflow_cpu_clock_and_labelled_synthetic_motion(self):
        motion = (ROOT / "Scripts" / "KandiBoomBeatDance.cs").read_text()
        overlay = (ROOT / "Scripts" / "KandiBoomControls.cs").read_text()
        for text in ('clock.Current.AbsoluteBeat','_rest[name]','LeftArm','RightArm','Head'):
            self.assertIn(text, motion)
        self.assertIn('SYNTHETIC BEAT PREVIEW', overlay)
        self.assertNotIn('Time.deltaTime', motion)
        self.assertIn('clock.Transport.Pause()', overlay)

    def test_build_requires_real_unity_artifacts(self):
        root = TOOLS.parents[1]
        workflow = (root / '.github' / 'workflows' / 'unity-webgl-pages.yml').read_text()
        for value in ('game-ci/unity-builder@v4','actions/upload-pages-artifact@v3',
                      'actions/deploy-pages@v4','fetch_assets.py','buildMethod:',
                      'UNITY_LICENSE','UNITY_SERIAL'):
            self.assertIn(value, workflow)
        self.assertIn('publish_pages.py', workflow)

if __name__ == '__main__':
    unittest.main()
