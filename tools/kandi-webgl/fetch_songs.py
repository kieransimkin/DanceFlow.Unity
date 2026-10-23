#!/usr/bin/env python3
"""Build immutable, source-labelled WebGL song, audio-analysis and rudiment assets.

Public original song MP3 files already live in the user's StemLab repository.
No audio is committed to the Unity UPM. CI verifies SHA-256 and byte count before
publishing an audio file. Native DanceRudiments compiled samples are fetched at
an immutable commit, Git-blob-verified, and kept distinct from measured audio.
"""
from __future__ import annotations

import argparse
from array import array
import hashlib
import json
import math
from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import quote
from urllib.request import urlopen

HERE = Path(__file__).resolve().parent
LOCK = HERE / "song-lock.json"
SAMPLE_RATE = 8000
HOP = 320  # 40ms per analysed frame, in original decoded audio time
BANDS = ("sub", "bass", "low_mid", "mid", "high", "onset", "width", "energy")
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
SHA1_RE = re.compile(r"^[0-9a-f]{40}$")
ID_RE = re.compile(r"^[a-z][a-z0-9_]{1,80}$")


def git_blob_sha1(raw: bytes) -> str:
    return hashlib.sha1(b"blob " + str(len(raw)).encode("ascii") + b"\0" + raw).hexdigest()


def load_lock(path: Path | None = None) -> dict:
    path = LOCK if path is None else Path(path)
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("schema") != "danceflow.unity.webgl-song-lock.v1":
        raise ValueError("wrong WebGL song lock schema")
    for key in ("stemlab_commit", "dancerudiments_commit"):
        if SHA1_RE.fullmatch(data.get(key, "")) is None:
            raise ValueError("source commit must be an immutable SHA-1")
    if len(data.get("songs", [])) < 3:
        raise ValueError("at least three verified demo songs required")
    ids = set()
    for s in data["songs"]:
        if ID_RE.fullmatch(s.get("id", "")) is None or s["id"] in ids:
            raise ValueError("invalid/duplicate song ID")
        ids.add(s["id"])
        if not isinstance(s.get("bytes"), int) or s["bytes"] < 1024 or s["bytes"] > 50_000_000:
            raise ValueError("invalid pinned MP3 byte size")
        if SHA256_RE.fullmatch(s.get("sha256", "")) is None:
            raise ValueError("missing song SHA-256")
        tempo = s.get("bpm")
        if tempo is None:
            # The supplied lossless-master WAV has no confirmed BPM yet.
            # Keep the 120 BPM procedural gallery rhythm separate from the
            # song's real audio envelopes; it is NOT a detected beat map.
            if (s.get("tempo_status") != "not_verified_preview_120" or
                s.get("beat_map_path") is not None or
                s.get("preview_bpm") != 120.0):
                raise ValueError("Unknown BPM requires an explicit, labelled preview pulse")
            if SHA256_RE.fullmatch(s.get("canonical_master_sha256", "")) is None:
                raise ValueError("Unknown-tempo mix requires exact lossless source hash")
        elif (not isinstance(tempo, (int, float)) or isinstance(tempo, bool) or
              not math.isfinite(tempo) or tempo <= 0):
            raise ValueError("invalid explicitly documented song BPM")
        if not s["path"].startswith("examples/") or not s["path"].endswith(".mp3") or ".." in s["path"]:
            raise ValueError("unsafe audio source path")
    return data


def raw_url(repo: str, commit: str, path: str) -> str:
    if any(p in (".", "..", "") for p in path.split("/")):
        raise ValueError("unsafe relative source path")
    return f"https://raw.githubusercontent.com/kieransimkin/{repo}/{commit}/{quote(path, safe='/')}"


def download(url: str, *, limit: int) -> bytes:
    # This function is intentionally limited to a fixed, pinned raw.githubusercontent host
    # and is called with source URLs constructed exclusively by raw_url().
    if not url.startswith("https://raw.githubusercontent.com/kieransimkin/"):
        raise ValueError("untrusted download origin")
    with urlopen(url, timeout=120) as response:
        if response.geturl().split("/")[2] != "raw.githubusercontent.com":
            raise ValueError("unexpected source redirect")
        raw = response.read(limit + 1)
    if len(raw) > limit:
        raise ValueError("source exceeds configured size limit")
    return raw


def checked_audio(raw: bytes, info: dict) -> None:
    if len(raw) != info["bytes"] or hashlib.sha256(raw).hexdigest() != info["sha256"]:
        raise ValueError("Song does not match pinned SHA-256 + byte count: " + info["id"])


def checked_blob(raw: bytes, expected_sha: str, expected_bytes: int | None = None) -> None:
    if (expected_bytes is not None and len(raw) != expected_bytes) or git_blob_sha1(raw) != expected_sha:
        raise ValueError("Pinned Git blob SHA mismatch")


def decode_pcm_mp3(path: Path) -> array:
    completed = subprocess.run(
        ["ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error", "-i", str(path),
         "-vn", "-ac", "2", "-ar", str(SAMPLE_RATE), "-f", "f32le", "pipe:1"],
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=180, check=True)
    if len(completed.stdout) % 8:
        raise ValueError("Decoded audio has incomplete stereo float samples")
    pcm = array("f")
    pcm.frombytes(completed.stdout)
    if sys.byteorder != "little":
        pcm.byteswap()
    if not SAMPLE_RATE * 2 <= len(pcm) <= SAMPLE_RATE * 2 * 600:
        raise ValueError("Decoded song outside 1..600 seconds")
    return pcm


def percentile(values: list[float], q: float) -> float:
    if not values:
        raise ValueError("No audio envelope samples")
    ordered = sorted(values)
    return ordered[min(len(ordered)-1, int(q * (len(ordered)-1)))]


def analyze_pcm(pcm: array) -> dict:
    """Deterministic IIR crossover envelopes; measured, not source-matched dance motions.

    8k stereo decode, four causal one-pole low-passes, RMS per 40ms window,
    normalized independently at 97th percentile. Onset derives from real RMS
    increases and is not a claimed BeatNet/BeatThis transcription.
    """
    alphas = [math.exp(-2 * math.pi * f / SAMPLE_RATE) for f in (70, 250, 900, 3000)]
    low = [0.0, 0.0, 0.0, 0.0]
    raw = [[] for _ in BANDS]
    last_energy = 0.0
    samples = len(pcm) // 2
    for start in range(0, samples, HOP):
        stop = min(samples, start + HOP)
        if stop - start < HOP // 2:
            break
        powers = [0.0] * 7
        for n in range(start, stop):
            left = float(pcm[2*n]); right = float(pcm[2*n+1]); mono = (left + right) * 0.5
            previous = 0.0
            splits = []
            for k, alpha in enumerate(alphas):
                low[k] = alpha * low[k] + (1.0 - alpha) * mono
                splits.append(low[k])
            components = (splits[0], splits[1]-splits[0], splits[2]-splits[1],
                          splits[3]-splits[2], mono-splits[3], (left-right)*0.5, mono)
            for k, value in enumerate(components):
                powers[k] += value * value
        frame = [math.sqrt(power / (stop-start)) for power in powers]
        onset = max(0.0, frame[6] - 0.80 * last_energy)
        last_energy = frame[6]
        frame.insert(5, onset)
        for channel, value in zip(raw, frame):
            channel.append(value)
    values = {}
    for name, row in zip(BANDS, raw):
        scale = max(1e-5, percentile(row, 0.97) * 1.25)
        values[name] = [round(min(1.0, x / scale), 5) for x in row]
    return values


def serialize_patterns(raw: bytes, selection: list[str]) -> dict:
    pack = json.loads(raw)
    if pack.get("format") != "dancerudiments.compiled-pack" or pack.get("schema_version") != 1 or pack.get("pips_per_beat") != 64:
        raise ValueError("Unexpected official DanceRudiments pack schema")
    by_name = {p["name"]: p for p in pack["patterns"]}
    if len(by_name) != len(pack["patterns"]):
        raise ValueError("Duplicate official pattern name")
    output = []
    for name in selection:
        if name not in by_name:
            raise ValueError("Missing pinned DanceRudiments pattern: " + name)
        p = by_name[name]
        rows = p["samples"]
        if len(rows) != p["period_pips"] or p["period_pips"] > 4096:
            raise ValueError("Invalid pattern period")
        if any(len(row) != 3 or any(not math.isfinite(v) or abs(v) > 1 for v in row) for row in rows):
            raise ValueError("Invalid motion sample")
        axes = [[round(float(row[i]), 6) for row in rows] for i in range(3)]
        spans = [max(v)-min(v) for v in axes]
        output.append({"name":name, "period_pips":p["period_pips"], "primary_axis":spans.index(max(spans)),
                       "x":axes[0], "y":axes[1], "z":axes[2],
                       "source_sha256":p["source_sha256"]})
    return {"schema":"danceflow.unity.webgl-rudiments.v1", "origin":"DanceRudiments compiled native pattern samples",
            "alignment":"Not proven matched to any source song; patterns are beat-retimed demonstration candidates",
            "patterns":output}


def consensus_beat_map(raw: bytes, *, duration: float) -> tuple[list[float], int]:
    doc = json.loads(raw)
    beats, downbeats = doc.get("beats"), doc.get("downbeats")
    if not isinstance(beats, list) or len(beats) < 2 or len(beats) > 30000:
        raise ValueError("Invalid frozen beat consensus")
    if any(not isinstance(t,(int,float)) or not math.isfinite(t) or t < 0 or t >= duration for t in beats):
        raise ValueError("Beat consensus outside actual audio duration")
    if any(b <= a for a,b in zip(beats,beats[1:])):
        raise ValueError("Nonmonotonic beat consensus")
    if not isinstance(downbeats, list) or not downbeats:
        raise ValueError("No model consensus downbeat")
    origin = min(range(len(beats)), key=lambda i: abs(beats[i] - downbeats[0]))
    if abs(beats[origin] - downbeats[0]) > 0.001:
        raise ValueError("Consensus downbeat does not identify a beat anchor")
    return [round(float(v), 5) for v in beats], origin


def build(destination: Path, *, getter=download, decoder=decode_pcm_mp3) -> dict:
    lock = load_lock()
    destination = Path(destination).resolve()
    if destination.exists() and any(destination.iterdir()):
        raise FileExistsError("Refusing to overwrite nonempty song output folder")
    destination.mkdir(parents=True, exist_ok=True)
    pack_info = lock["pattern_pack"]
    pack_raw = getter(raw_url("DanceRudiments",lock["dancerudiments_commit"],pack_info["path"]), limit=1_000_000)
    checked_blob(pack_raw,pack_info["git_blob_sha1"],pack_info["bytes"])
    pattern_data = serialize_patterns(pack_raw, lock["patterns"])
    (destination/"rudiments.json").write_text(json.dumps(pattern_data,separators=(",",":"),allow_nan=False),encoding="utf-8")
    songs=[]
    for s in lock["songs"]:
        master=getter(raw_url("stemlab",lock["stemlab_commit"],s["path"]),limit=s["bytes"])
        checked_audio(master,s)
        audio_name=s["id"]+".mp3"
        audio_path=destination/audio_name
        audio_path.write_bytes(master)
        pcm=decoder(audio_path)
        analysed=analyze_pcm(pcm)
        audio_seconds=(len(pcm)//2)/SAMPLE_RATE
        signals_name=s["id"]+".signals.json"
        signals={"schema":"danceflow.unity.webgl-audio-analysis.v1","source_sha256":s["sha256"],
                 "frame_seconds":HOP/SAMPLE_RATE,"frame_count":len(analysed["sub"]),
                 "source":"measured decoded MP3 energy bands; no claimed matched dance performance",**analysed}
        (destination/signals_name).write_text(json.dumps(signals,separators=(",",":"),allow_nan=False),encoding="utf-8")
        beats=[];origin=0;beat_sha=""
        if s.get("beat_map_path"):
            consensus_raw=getter(raw_url("stemlab",lock["stemlab_commit"],s["beat_map_path"]),limit=200_000)
            checked_blob(consensus_raw,s["beat_map_git_blob_sha1"])
            beats,origin=consensus_beat_map(consensus_raw,duration=audio_seconds)
            beat_sha=hashlib.sha256(consensus_raw).hexdigest()
        songs.append({"id":s["id"],"title":s["title"],"artist":s["artist"],"audio_file":audio_name,
                      "signals_file":signals_name,"audio_sha256":s["sha256"],
                      "bpm":s["bpm"] if s["bpm"] is not None else 0,
                      "preview_bpm":s.get("preview_bpm", 0),
                      "canonical_master_sha256":s.get("canonical_master_sha256", ""),
                      "tempo_status":s["tempo_status"],"phase_status":s["phase_status"],
                      "duration_seconds":audio_seconds,"source_sample_rate":SAMPLE_RATE,
                      "source_frames":len(pcm)//2,"beat_seconds":beats,"origin_beat":origin,
                      "beat_map_sha256":beat_sha})
    catalog={"schema":"danceflow.unity.webgl-song-gallery.v1", "songs":songs,
             "unavailable_songs":["Bass Ben & Friends — Star Lights Glow (Anthem)"],
             "note":"Audio from byte-verified StemLab exemplars; original native DanceRudiments patterns unverified for phase/song matching"}
    (destination/"catalogue.json").write_text(json.dumps(catalog,separators=(",",":"),allow_nan=False),encoding="utf-8")
    return catalog


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--output",type=Path,required=True)
    a=p.parse_args()
    try:
        result=build(a.output)
    except (OSError, ValueError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as e:
        p.error(str(e))
    print("Verified",len(result["songs"]),"songs with measured envelopes and",len(load_lock()["patterns"]),"native rudiment patterns")

if __name__=="__main__":
    main()
