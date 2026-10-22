#!/usr/bin/env python3
"""Offline evidence check for the small native recording fixture; no model downloads."""
import hashlib
import json
import struct
from pathlib import Path
from fractions import Fraction

root = Path(__file__).resolve().parents[2]
folder = root / "Tests" / "Fixtures"
raw = (folder / "dancerudiments-four-axes-v1.json").read_bytes()
fixture = json.loads(raw)
receipt = json.loads((folder / "dancerudiments-four-axes-v1.receipt.json").read_text())
assert hashlib.sha256(raw).hexdigest() == receipt["source_sha256"]
assert fixture["format"] == receipt["format"] == "dancerudiments.recording"
assert fixture["clock"]["start_pip"] == receipt["start_pip"]
assert len(fixture["dimensions"]) == receipt["dimensions"]
assert len(fixture["samples"]) == receipt["sample_count"]
assert fixture["samples"][0] == receipt["sample_at_-32"]
assert fixture["samples"][2] == receipt["sample_at_-30"]
assert fixture["clock"]["start_pip"] + len(fixture["samples"]) == receipt["end_pip_exclusive"]

def write_7bit_string(value):
    encoded = value.encode('utf-8')
    size, prefix = len(encoded), bytearray()
    while size >= 0x80:
        prefix.append((size & 0x7f) | 0x80)
        size >>= 7
    prefix.append(size)
    return bytes(prefix) + encoded

clock = fixture['clock']
dims = fixture['dimensions']
provenance = json.dumps(fixture['provenance'], sort_keys=True, separators=(',', ':'), ensure_ascii=False)
descriptor = (write_7bit_string(fixture['name']) + struct.pack('<ii', len(fixture['samples']), len(dims)) +
              b''.join(map(write_7bit_string, dims)) + struct.pack('<d', clock['bpm']) +
              write_7bit_string(clock['bpm_status']) + write_7bit_string(clock['bpm_source']) +
              struct.pack('<d', clock['beat_zero_seconds']) + write_7bit_string(clock['phase_status']) +
              struct.pack('<i', clock['start_pip']) + write_7bit_string(provenance))
assert hashlib.sha256(descriptor).hexdigest() == receipt['descriptor_sha256']
header = b'DFR1' + struct.pack('<iiiidd', 1, len(dims), len(fixture['samples']),
                               clock['start_pip'], clock['bpm'], clock['beat_zero_seconds'])
assert len(header) == 36
payload = b''.join(struct.pack('<d', round(value, 12)) for row in fixture['samples'] for value in row)
cache = header + hashlib.sha256(descriptor).digest() + payload
assert len(cache) == receipt['cache_bytes']
assert hashlib.sha256(cache).hexdigest() == receipt['cache_sha256']
# Compare with upstream exact Fraction(str(decimal_BPM)) authoring clock, not frame counts.
bpm = Fraction(str(fixture["clock"]["bpm"]))
origin = Fraction(str(fixture["clock"]["beat_zero_seconds"]))
for index in range(len(fixture["samples"])):
    t = origin + Fraction(fixture["clock"]["start_pip"] + index) * Fraction(60, 64) / bpm
    assert (t - origin) * bpm * Fraction(64, 60) == fixture["clock"]["start_pip"] + index
print("Fixture SHA-256, native exact pip clock, channels and bounds: PASS")
