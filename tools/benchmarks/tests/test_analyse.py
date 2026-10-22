"""Only synthetic analyser fixtures; these tests are NOT evidence of Unity GPU performance."""
import copy
import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("dfu_analyse", Path(__file__).parents[1] / "analyse.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def sample(mode, run, *, gpu=True):
    rows = []
    for i in range(61):
        rows.append({
            "unityFrame": i, "cpuFrameAvailable": True, "cpuFrameMs": 4.0 + (i % 5) * .1,
            "gpuFrameAvailable": gpu, "gpuFrameMs": 3.0 + (i % 3) * .2 if gpu else 0,
            "mainThreadAvailable": True, "mainThreadMs": 2.0,
            "gcAllocatedAvailable": False, "gcAllocatedBytes": 0,
            "publisherAvailable": False, "publisherMicroseconds": 0,
        })
    return {
        "schema": module.SCHEMA, "scenario": mode, "runId": run, "revision": "synthetic",
        "unityVersion": "6000.0.23f1", "graphicsDevice": "SYNTHETIC-NOT-GPU",
        "graphicsApi": "D3D11", "processor": "SYNTHETIC", "operatingSystem": "synthetic",
        "width": 1920, "height": 1080, "scene": "Benchmark", "renderObjectCount": 1024,
        "qualityLevel": "High", "platform": "WindowsPlayer", "vsyncCount": 0,
        "framesSampled": 61, "requestedMeasuredFrames": 60,
        "observationsOverwritten": 0, "gpuUtilisation": "unavailable: synthetic",
        "gpuTimingAvailable": gpu, "runningInEditor": False,
        "observations": rows, "_file": f"synthetic-{run}-{mode}.json"
    }


class BenchmarkAnalysisTests(unittest.TestCase):
    def test_type7_percentile(self):
        self.assertEqual(module.percentile([0, 10, 20, 30, 40], .95), 38)
        self.assertIsNone(module.percentile([], .5))

    def test_missing_gpu_is_inconclusive_when_required(self):
        reports = [sample(mode, f"round{i}", gpu=False)
                   for i in range(3) for mode in module.VARIANTS]
        result = module.analyse(reports, 3, require_gpu=True)
        self.assertEqual(result["verdict"], "inconclusive")
        self.assertEqual(result["completePairedRuns"], 3)
        self.assertIsNone(result["pairedDeltas"]["Global publisher vs clock"]["gpu_p95_ms_delta"])

    def test_paired_runs_and_missing_variant(self):
        reports = [sample(mode, f"round{i}") for i in range(3) for mode in module.VARIANTS]
        self.assertEqual(module.analyse(reports, 3, True)["verdict"], "measured_not_release_accepted")
        self.assertEqual(module.analyse(reports[:-1], 3, True)["verdict"], "inconclusive")

    def test_environment_mismatch_fails(self):
        a = sample("Clock", "r0")
        b = sample("Baseline", "r0")
        b["graphicsDevice"] = "another"
        with self.assertRaises(ValueError):
            module.analyse([a, b])

    def test_duplicate_missing_and_overflow_rejected(self):
        a = sample("Clock", "r0")
        with self.assertRaises(ValueError):
            module.analyse([a, copy.deepcopy(a)])
        a["observationsOverwritten"] = 4
        # Direct validation of an on-disk report is covered separately by the CLI.
        self.assertEqual(a["observationsOverwritten"], 4)

    def test_discarded_observations_are_rejected(self):
        import json
        import tempfile
        report = sample("Clock", "r0")
        report["observationsOverwritten"] = 7
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory) / "overflow.json"
            file.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "buffer overflow"):
                module.read_report(file)

    def test_memory_counters_are_estimates_not_guessed_when_missing(self):
        reports = [sample(mode, f"round{i}") for i in range(3) for mode in module.VARIANTS]
        result = module.analyse(reports, 3, True)
        self.assertIsNone(result["memoryEstimates"]["ShaderMonitored"]["video_used_bytes_peak"])
        self.assertIsNone(result["memoryEstimates"]["ShaderMonitored"]["reserved_bytes_growth"])

    def test_optional_calibrated_budget_can_fail(self):
        reports = [sample(mode, f"round{i}") for i in range(3) for mode in module.VARIANTS]
        for report in reports:
            if report["scenario"] == "Globals":
                for row in report["observations"]:
                    row["cpuFrameMs"] += 2
        result = module.analyse(reports, 3, True, max_global_cpu_delta_ms=0.15)
        self.assertEqual(result["verdict"], "threshold_exceeded")

    def test_negative_counter_rejected(self):
        import json
        import tempfile
        report = sample("Clock", "r0")
        report["observations"][0]["cpuFrameMs"] = -1
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory) / "invalid.json"
            file.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "invalid cpuFrameMs"):
                module.read_report(file)


if __name__ == "__main__":
    unittest.main()
