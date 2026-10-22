#!/usr/bin/env python3
"""Analyse genuine DanceFlow Unity Player benchmark JSON reports (no synthetic results).

Uses raw observations, not self-reported summary values. Missing GPU counters and
insufficient paired repeats are INCONCLUSIVE, never zero-time successes.
"""
from __future__ import annotations

import argparse
import json
import math
import statistics
import sys
from collections import defaultdict
from pathlib import Path

SCHEMA = "danceflow.unity.benchmark.v2"
VARIANTS = ("Baseline", "Clock", "Globals", "Shader", "GlobalsMonitored", "ShaderMonitored")
METRICS = {
    "cpu_p95_ms": ("cpuFrameAvailable", "cpuFrameMs"),
    "gpu_p95_ms": ("gpuFrameAvailable", "gpuFrameMs"),
    "main_thread_p95_ms": ("mainThreadAvailable", "mainThreadMs"),
    "gc_mean_bytes": ("gcAllocatedAvailable", "gcAllocatedBytes"),
    "publisher_p95_us": ("publisherAvailable", "publisherMicroseconds"),
}
COMPARE = {
    "Clock vs baseline": ("Clock", "Baseline"),
    "Global publisher vs clock": ("Globals", "Clock"),
    "Vertex/fragment shader vs globals": ("Shader", "Globals"),
    "Global telemetry overhead": ("GlobalsMonitored", "Globals"),
    "Shader telemetry overhead": ("ShaderMonitored", "Shader"),
}
ENV_FIELDS = ("unityVersion", "graphicsDevice", "graphicsApi", "processor", "operatingSystem",
              "width", "height", "scene", "renderObjectCount", "qualityLevel", "platform", "vsyncCount")


def percentile(values: list[float], q: float) -> float | None:
    if not values:
        return None
    a = sorted(values)
    index = (len(a) - 1) * q
    i = math.floor(index)
    return a[i] + (a[min(len(a) - 1, i + 1)] - a[i]) * (index - i)


def read_report(path: Path) -> dict:
    try:
        result = json.loads(path.read_text(encoding="utf-8-sig"))
    except (ValueError, OSError) as exc:
        raise ValueError(f"{path}: invalid JSON: {exc}") from exc
    if not isinstance(result, dict) or result.get("schema") != SCHEMA:
        raise ValueError(f"{path}: expected schema {SCHEMA}")
    if result.get("scenario") not in VARIANTS:
        raise ValueError(f"{path}: invalid benchmark scenario")
    if not isinstance(result.get("observations"), list) or not result["observations"]:
        raise ValueError(f"{path}: missing raw frame observations")
    if result.get("framesSampled", 0) < len(result["observations"]):
        raise ValueError(f"{path}: impossible sample count")
    if result.get("observationsOverwritten", 0):
        raise ValueError(f"{path}: bounded buffer overflow; rerun with a shorter capture")
    if result.get("requestedMeasuredFrames", 0) < 60:
        raise ValueError(f"{path}: missing measurement target")
    for field in ENV_FIELDS:
        if result.get(field) is None:
            raise ValueError(f"{path}: missing environment field {field}")
    if result.get("gpuUtilisation", "").startswith("unavailable") is False:
        raise ValueError(f"{path}: GPU utilisation cannot be inferred from frame time")
    for row in result["observations"]:
        if not isinstance(row, dict):
            raise ValueError(f"{path}: invalid observation")
        for available, key in METRICS.values():
            if row.get(available, False):
                value = row.get(key)
                if not isinstance(value, (float, int)) or isinstance(value, bool) or not math.isfinite(value) or value < 0:
                    raise ValueError(f"{path}: invalid {key} observation")
    result["_file"] = str(path)
    return result


def summarise_run(report: dict) -> dict:
    rows = report["observations"]
    m = {}
    for key, (available, field) in METRICS.items():
        values = [float(row[field]) for row in rows if row.get(available, False)]
        m[key] = statistics.mean(values) if values and key.endswith("mean_bytes") else percentile(values, 0.95)
        m[key + "_samples"] = len(values)
    for key, valid, value in (
        ("reserved_bytes", "reservedMemoryAvailable", "totalReservedBytes"),
        ("video_used_bytes", "videoUsedMemoryAvailable", "videoUsedBytes"),
        ("driver_allocated_bytes", "driverAllocatedMemoryAvailable", "graphicsDriverAllocatedBytes"),
    ):
        memory = [int(row[value]) for row in rows if row.get(valid, False)]
        m[key + "_start"] = memory[0] if memory else None
        m[key + "_last"] = memory[-1] if memory else None
        m[key + "_peak"] = max(memory) if memory else None
        m[key + "_growth"] = memory[-1] - memory[0] if memory else None
    return m


def median_or_none(values):
    clean = [float(v) for v in values if v is not None]
    return statistics.median(clean) if clean else None


def analyse(reports: list[dict], required_repeats: int = 3, require_gpu: bool = False,
            max_global_cpu_delta_ms: float | None = None) -> dict:
    if not reports:
        raise ValueError("No benchmark files supplied")
    reference_env = {f: reports[0][f] for f in ENV_FIELDS}
    sessions: dict[str, dict[str, dict]] = defaultdict(dict)
    warnings = []
    for r in reports:
        environment = {f: r[f] for f in ENV_FIELDS}
        if environment != reference_env:
            raise ValueError(f"Incompatible hardware/project environment in {r['_file']}")
        run_id = r.get("runId")
        scenario = r["scenario"]
        if not run_id:
            raise ValueError(f"Missing runId in {r['_file']}")
        if scenario in sessions[run_id]:
            raise ValueError(f"Duplicate scenario {scenario} for run {run_id}")
        sessions[run_id][scenario] = r
        if r["framesSampled"] < r["requestedMeasuredFrames"]:
            warnings.append(f"{r['_file']}: fewer observations than measured frames")
        if not r.get("gpuTimingAvailable"):
            warnings.append(f"{r['_file']}: GPU timing unavailable")
        if r.get("runningInEditor"):
            warnings.append(f"{r['_file']}: Editor measurement, not a standalone Player")
    completed = {run: scenarios for run, scenarios in sessions.items() if set(VARIANTS).issubset(scenarios)}
    if len(completed) < required_repeats:
        warnings.append(f"Only {len(completed)} complete paired runs, require {required_repeats}")
    per_scenario = defaultdict(list)
    for scenarios in completed.values():
        for key in VARIANTS:
            per_scenario[key].append(summarise_run(scenarios[key]))
    summary = {
        key: {metric: median_or_none([r[metric] for r in runs]) for metric in METRICS}
        for key, runs in per_scenario.items()
    }
    resource_medians = {
        key: {metric: median_or_none([r[metric] for r in runs])
              for metric in ("reserved_bytes_start", "reserved_bytes_last", "reserved_bytes_peak", "reserved_bytes_growth",
                             "video_used_bytes_start", "video_used_bytes_last", "video_used_bytes_peak", "video_used_bytes_growth",
                             "driver_allocated_bytes_start", "driver_allocated_bytes_last", "driver_allocated_bytes_peak", "driver_allocated_bytes_growth")}
        for key, runs in per_scenario.items()
    }
    comparisons = {}
    for name, (variant, baseline) in COMPARE.items():
        data = {}
        for metric in METRICS:
            # Compute paired deltas first, then median. Do not subtract independent run averages.
            deltas = []
            for scenarios in completed.values():
                a = summarise_run(scenarios[variant]).get(metric)
                b = summarise_run(scenarios[baseline]).get(metric)
                if a is not None and b is not None:
                    deltas.append(a - b)
            data[metric + "_delta"] = median_or_none(deltas)
            data[metric + "_paired_runs"] = len(deltas)
        comparisons[name] = data
    gpu_coverage_valid = all(
        summarise_run(r)["gpu_p95_ms_samples"] >= 0.8 * r["requestedMeasuredFrames"]
        for scenarios in completed.values() for r in scenarios.values()
    )
    if not gpu_coverage_valid:
        warnings.append("Fewer than 80% of requested measured frames have valid GPU timings")
    gpu_pairs_valid = gpu_coverage_valid and all(
        comparisons[name]["gpu_p95_ms_paired_runs"] >= required_repeats for name in COMPARE
    )
    verdict = "measured_not_release_accepted"
    has_editor_runs = any(r.get("runningInEditor") for scenarios in completed.values() for r in scenarios.values())
    if len(completed) < required_repeats or (require_gpu and (not gpu_pairs_valid or has_editor_runs)):
        verdict = "inconclusive"
    if max_global_cpu_delta_ms is not None:
        observed = comparisons["Global publisher vs clock"]["cpu_p95_ms_delta"]
        if observed is None:
            verdict = "inconclusive"
        elif observed > max_global_cpu_delta_ms and verdict != "inconclusive":
            verdict = "threshold_exceeded"
    return {
        "schema": "danceflow.unity.benchmark-analysis.v1",
        "verdict": verdict,
        "scope": "performance-only; Unity shader parity and rendering correctness require independent GPU readback tests",
        "environment": reference_env,
        "completePairedRuns": len(completed),
        "expectedVariants": list(VARIANTS),
        "scenarioMedians": summary,
        "pairedDeltas": comparisons,
        "memoryEstimates": resource_medians,
        "warnings": sorted(set(warnings)),
        "requestedGpuData": require_gpu,
        "cpuDeltaBudgetMs": max_global_cpu_delta_ms,
        "inputs": sorted(r["_file"] for r in reports),
    }


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("reports", nargs="+", type=Path, help="Actual Unity Player JSON files")
    p.add_argument("--output", type=Path, help="Optional JSON summary path")
    p.add_argument("--required-repeats", type=int, default=3)
    p.add_argument("--require-gpu", action="store_true")
    p.add_argument("--max-global-cpu-delta-ms", type=float)
    a = p.parse_args(argv)
    if a.required_repeats < 1:
        p.error("--required-repeats must be positive")
    try:
        result = analyse([read_report(f) for f in a.reports], a.required_repeats,
                         a.require_gpu, a.max_global_cpu_delta_ms)
    except ValueError as exc:
        print("Benchmark error: " + str(exc), file=sys.stderr)
        return 2
    text = json.dumps(result, indent=2, sort_keys=True, allow_nan=False) + "\n"
    if a.output:
        a.output.parent.mkdir(parents=True, exist_ok=True)
        a.output.write_text(text, encoding="utf-8")
    else:
        print(text)
    return 0 if result["verdict"] == "measured_not_release_accepted" else 1


if __name__ == "__main__":
    sys.exit(main())
