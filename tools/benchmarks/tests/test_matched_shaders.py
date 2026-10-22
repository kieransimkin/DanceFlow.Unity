"""Static integrity checks, not claims of successful Unity shader compilation."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[3]
SAMPLE = ROOT / "Samples~" / "ShaderClockURP"


class MatchedShaderTests(unittest.TestCase):
    def test_baseline_and_effect_use_same_render_pass_and_material_contract(self):
        baseline = (SAMPLE / "BeatPulseBaseline.shader").read_text(encoding="utf-8")
        effect = (SAMPLE / "BeatPulseVertex.shader").read_text(encoding="utf-8")
        self.assertIn('Shader "DanceFlow/URP/BeatPulseBaseline"', baseline)
        self.assertIn('Shader "DanceFlow/URP/BeatPulseVertex"', effect)
        for contract in (
            '"RenderPipeline"="UniversalPipeline"', '"LightMode"="SRPDefaultUnlit"',
            '#pragma target 3.0', '#pragma vertex Vert', '#pragma fragment Frag',
            'float4 positionOS : POSITION;', 'float3 normalOS : NORMAL;',
            'float3 normalWS : TEXCOORD0;', 'CBUFFER_START(UnityPerMaterial)',
            '_BaseColor', '_Displacement', '_Glow',
            '0.28 + 0.72 * saturate(input.normalWS.y * 0.6 + 0.4)',
        ):
            with self.subTest(contract=contract):
                self.assertIn(contract, baseline)
                self.assertIn(contract, effect)
        self.assertNotIn('_DanceFlow_', baseline)
        self.assertNotIn('DanceFlowClock.hlsl', baseline)

    def test_benchmark_uses_matched_shader_and_unique_grid(self):
        builder = (ROOT / "Editor/DanceFlowBenchmarkBuilder.cs").read_text(encoding="utf-8")
        runner = (ROOT / "Runtime/Performance/DanceFlowBenchmarkRunner.cs").read_text(encoding="utf-8")
        self.assertIn('Shader.Find("DanceFlow/URP/BeatPulseBaseline")', builder)
        self.assertNotIn('Shader.Find("Universal Render Pipeline/Lit")', builder)
        self.assertIn('Mathf.CeilToInt(Mathf.Sqrt(objectCount))', runner)
        self.assertIn('i % columns', runner)
        self.assertIn('i / columns', runner)
        self.assertNotIn('i % 32', runner)
        self.assertIn('QualitySettings.vSyncCount = _previousVsync', runner)
        self.assertIn('Application.targetFrameRate = _previousFrameRate', runner)
        # Test generated grid coordinates numerically at all extremes. No shadows/overlaps.
        import math
        for count in (32, 256, 1024, 4096, 16384):
            columns = math.ceil(math.sqrt(count))
            cells = {(i % columns, i // columns) for i in range(count)}
            self.assertEqual(len(cells), count)


if __name__ == "__main__":
    unittest.main()
