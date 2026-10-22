#!/usr/bin/env python3
"""Dependency-free static shader contract and CPU/GPU arithmetic regression checks.

Does not pretend to compile Unity ShaderLab, render on a GPU or execute C#.
"""
from __future__ import annotations

import json
import math
import re
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROPS = [
    "_DanceFlow_ShaderApiVersion", "_DanceFlow_BPM", "_DanceFlow_BeatPhase",
    "_DanceFlow_BarPhase", "_DanceFlow_TickPhase", "_DanceFlow_PipPhase",
    "_DanceFlow_BeatPulse", "_DanceFlow_DownbeatPulse", "_DanceFlow_BeatSin",
    "_DanceFlow_BeatCos", "_DanceFlow_Meter", "_DanceFlow_IsPlaying",
    "_DanceFlow_BeatIndexHiLo", "_DanceFlow_BeatRemainder256",
    "_DanceFlow_SourceSecondsHiLo", "_DanceFlow_TransportGenerationHiLo",
]


def f32(x: float) -> float:
    return struct.unpack("<f", struct.pack("<f", float(x)))[0]


def fmod_positive(x: float, p: float) -> float:
    # Approximate GPU float32 intermediate calculations, including multiply/subtract.
    return f32(x - f32(p * math.floor(f32(x / p))))


def gpu_cycle(beat: float, period: int) -> float:
    whole = math.floor(beat)
    high = math.floor(whole / 65536)
    low = whole - high * 65536
    phase = f32(beat - whole)
    highrem = fmod_positive(f32(high), f32(period))
    lowrem = fmod_positive(f32(low), f32(period))
    radixrem = fmod_positive(f32(65536), f32(period))
    whole_rem = fmod_positive(f32(fmod_positive(f32(highrem * radixrem), period) + lowrem), period)
    return fmod_positive(f32(f32(whole_rem + phase) / period), 1)


def near_cycle(a: float, b: float, tolerance: float = 5e-5) -> bool:
    delta = abs(a - b)
    return min(delta, abs(delta - 1)) < tolerance


def main():
    hlsl = (ROOT / "Runtime/GPU/DanceFlowClock.hlsl").read_text()
    csharp = (ROOT / "Runtime/GPU/DanceFlowShaderBridge.cs").read_text()
    shader = (ROOT / "Samples~/ShaderClockURP/BeatPulseVertex.shader").read_text()
    graph = (ROOT / "docs/03-shader-api.md").read_text()
    for prop in PROPS:
        assert prop in hlsl and prop in csharp and prop in graph, prop
    for fn in ("DF_GetClock", "DF_GetCycle", "DF_GetPulse", "DF_GetVertexBounce"):
        assert f"void {fn}_float(" in hlsl and f"void {fn}_half(" in hlsl, fn
    assert '"RenderPipeline"="UniversalPipeline"' in shader
    assert "#pragma vertex Vert" in shader and "#pragma fragment Frag" in shader
    assert "Packages/com.kieransimkin.danceflow.unity/Runtime/GPU/DanceFlowClock.hlsl" in shader
    assert "_DanceFlow_BeatPulse" in shader and "DF_GetVertexBounce_float" in shader
    assert "#ifndef DANCEFLOW_CLOCK_HLSL_INCLUDED" in hlsl
    assert "#endif // DANCEFLOW_CLOCK_HLSL_INCLUDED" in hlsl

    package = json.loads((ROOT / "package.json").read_text())
    assert package["name"] == "com.kieransimkin.danceflow.unity"
    assert any(s["path"] == "Samples~/ShaderClockURP" for s in package["samples"])
    # Unity assets and the sample directory require committed metadata, not just GUID uniqueness.
    for folder in (ROOT / "Runtime/GPU", ROOT / "Samples~/ShaderClockURP", ROOT / "Editor"):
        if folder.name == "ShaderClockURP":
            assert Path(str(folder) + ".meta").is_file(), "Sample folder missing Unity metadata"
        for asset in folder.iterdir():
            if asset.is_file() and not asset.name.endswith(".meta"):
                assert Path(str(asset) + ".meta").is_file(), f"Unity asset missing metadata: {asset}"
    meta_files = list(ROOT.rglob("*.meta"))
    guids = []
    for path in meta_files:
        found = re.search(r"^guid:\s*([a-f0-9]{32})$", path.read_text(), re.M)
        assert found, f"Missing GUID in {path}"
        guids.append(found.group(1))
    assert len(guids) == len(set(guids)), "Duplicate Unity asset GUIDs"
    assert (ROOT / "docs/00-implementation-plan.md").is_file()

    fixture = json.loads((ROOT / "Tests/Fixtures/shader-clock-v2.json").read_text())
    for item in fixture["cases"]:
        beat = float(item["beat"])
        assert abs((beat - math.floor(beat)) - float(item["beat_phase"])) < 1e-11
        assert abs((beat - item["bar_origin"]) / item["meter"] % 1 - item["bar_phase"]) < 1e-11
        for period in (1, 2, 3, 4, 5, 7, 16, 64, 255, 256):
            cpu = (beat % period) / period
            assert near_cycle(gpu_cycle(beat, period), cpu), (item["name"], beat, period)
    print(f"PASS: {len(PROPS)} shader globals, {len(meta_files)} GUIDs, "
          f"{len(fixture['cases'])} CPU/GPU synthetic golden cases")


if __name__ == "__main__":
    main()
