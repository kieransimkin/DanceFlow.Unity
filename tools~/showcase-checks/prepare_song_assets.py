#!/usr/bin/env python3
"""Validate local StemLab/DanceRudiments song assets and write a portable audit receipt.

This is not a beat detector, a downloader or a substitute for Unity's Editor wizard.
Audio and motion remain in the user's own workspace; output contains hashes and
local basenames only (no absolute locations, secrets, or copyrighted media).
"""
from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import math
import re
import wave
from pathlib import Path

SCHEMA = 'danceflow.unity.song-asset-receipt.v1'
HASH = re.compile(r'^[0-9a-f]{64}$')
NAME = re.compile(r'^[a-z][a-z0-9_]{0,127}$')
MAX_RECORDING_BYTES = 64 * 1024 * 1024
MAX_BEAT_BYTES = 32 * 1024 * 1024
MAX_SCALARS = 8_388_608
SONGS = ('arcadians', 'the_truth_is_a_virus',
         'star_lights_glow_mystery_liquid_kick', 'star_lights_glow_anthem')


def pairs(rows):
    result = {}
    for key, value in rows:
        if key in result:
            raise ValueError(f'duplicate JSON object key: {key}')
        result[key] = value
    return result


def load_json(path: Path, limit: int) -> tuple[dict, str]:
    path = Path(path)
    if path.suffix == '.gz':
        with gzip.open(path, 'rb') as stream:
            raw = stream.read(limit + 1)
    else:
        if path.stat().st_size > limit:
            raise ValueError(f'oversized input: {path.name}')
        raw = path.read_bytes()
    if len(raw) > limit:
        raise ValueError(f'oversized JSON (limit {limit} bytes): {path.name}')
    doc = json.loads(raw.decode('utf-8-sig'), object_pairs_hook=pairs,
                     parse_constant=lambda value: (_ for _ in ()).throw(ValueError('nonfinite: ' + value)))
    if not isinstance(doc, dict):
        raise ValueError('expected top-level JSON object')
    return doc, hashlib.sha256(raw).hexdigest()


def sha_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        while chunk := stream.read(1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()


def audio_metadata(path: Path) -> dict | None:
    if path.suffix.lower() not in ('.wav', '.wave'):
        return None
    try:
        with wave.open(str(path), 'rb') as wav:
            return {'sample_rate': wav.getframerate(), 'num_frames': wav.getnframes(),
                    'num_channels': wav.getnchannels()}
    except (wave.Error, EOFError):
        # Floating-point WAVE can be fully valid but unsupported by stdlib wave.
        return None


def check_map(document: dict, master_hash: str, pcm: dict | None) -> dict:
    if document.get('schema') != 'stemlab.beat-timeline.v1' or document.get('pips_per_beat') != 64:
        raise ValueError('not a StemLab beat-timeline.v1 map')
    source = document.get('source_audio')
    if not isinstance(source, dict) or source.get('sha256') != master_hash:
        raise ValueError('beat map master SHA-256 disagrees with local master bytes')
    for key in ('sample_rate', 'num_frames', 'num_channels'):
        value = source.get(key)
        if type(value) is not int or value <= 0:
            raise ValueError(f'invalid map source_audio.{key}')
        if pcm is not None and pcm[key] != value:
            raise ValueError(f'WAVE and beat timeline disagree on {key}')
    beats = document.get('beats')
    origin = document.get('origin_beat')
    if not isinstance(beats, list) or not 2 <= len(beats) <= 25000:
        raise ValueError('beat timeline must contain 2..25000 anchors')
    before = (-1, -1)
    origins = []
    for row in beats:
        if not isinstance(row, dict) or type(row.get('beat')) is not int or type(row.get('frame')) is not int:
            raise ValueError('invalid beat anchor')
        current = (row['beat'], row['frame'])
        if current[0] <= before[0] or current[1] <= before[1] or current[1] >= source['num_frames']:
            raise ValueError('unordered or out-of-range beat anchors')
        origins.append(row['beat']); before = current
    if origin not in origins:
        raise ValueError('beat zero does not identify a saved anchor')
    return {'source_sha256': master_hash, 'sample_rate': source['sample_rate'],
            'num_frames': source['num_frames'], 'num_channels': source['num_channels'],
            'anchor_count': len(beats), 'origin_beat': origin}


def check_recording(document: dict) -> dict:
    mandatory = {'format', 'schema_version', 'pips_per_beat', 'sample_dtype', 'sample_decimals',
                 'name', 'dimensions', 'samples', 'clock', 'provenance'}
    if set(document) != mandatory or document['format'] != 'dancerudiments.recording':
        raise ValueError('recording v1 keys/format mismatch')
    for key, expected in [('schema_version', 1), ('pips_per_beat', 64), ('sample_decimals', 12)]:
        if type(document[key]) is not int or document[key] != expected:
            raise ValueError(f'invalid {key}')
    if document['sample_dtype'] != 'float64' or not isinstance(document['provenance'], dict):
        raise ValueError('invalid recording dtype or provenance')
    if not isinstance(document['name'], str) or NAME.fullmatch(document['name']) is None:
        raise ValueError('invalid recording name')
    dims = document['dimensions']; rows = document['samples']; clock = document['clock']
    if not isinstance(dims, list) or not dims or len(dims) > MAX_SCALARS or \
            any(not isinstance(x, str) or not NAME.fullmatch(x) for x in dims) or len(set(dims)) != len(dims):
        raise ValueError('invalid channel names')
    if not isinstance(rows, list) or not rows or len(rows)*len(dims) > MAX_SCALARS:
        raise ValueError('invalid sample count')
    if not isinstance(clock, dict) or set(clock) != {'bpm', 'bpm_source', 'bpm_status',
                    'beat_zero_seconds', 'phase_status', 'start_pip'}:
        raise ValueError('invalid recording clock')
    bpm = clock['bpm']; start = clock['start_pip']
    if type(bpm) not in (int, float) or not math.isfinite(bpm) or bpm <= 0 or \
            type(start) is not int or start < -(2**31) or start + len(rows) > 2**31-1 or \
            clock['bpm_status'] not in ('estimated', 'confirmed') or \
            clock['phase_status'] not in ('unverified', 'estimated', 'supplied') or \
            not isinstance(clock['bpm_source'], str) or not clock['bpm_source'].strip():
        raise ValueError('recording clock or timing status invalid')
    for row in rows:
        if not isinstance(row, list) or len(row) != len(dims):
            raise ValueError('ragged recording rows')
        if any(type(x) not in (int, float) or not math.isfinite(x) or not -1 <= x <= 1 for x in row):
            raise ValueError('invalid recording values')
    return {'name': document['name'], 'channel_names': dims, 'samples': len(rows),
            'start_pip': start, 'bpm': bpm, 'phase_status': clock['phase_status'],
            'bpm_status': clock['bpm_status']}


def make_receipt(song: str, master: Path, recording: Path | None = None,
                 timeline: Path | None = None, *, bpm: float | None = None,
                 approve_original_clock: bool = False, channels: list[str] | None = None,
                 explicit_retime: tuple[float, float, float] | None = None,
                 approve_unverified_phase: bool = False) -> dict:
    if song not in SONGS:
        raise ValueError('unsupported song profile')
    master = Path(master)
    if not master.is_file():
        raise FileNotFoundError(master)
    master_hash = sha_file(master)
    pcm = audio_metadata(master)
    map_details = None; map_sha = None
    if timeline is not None:
        doc, map_sha = load_json(timeline, MAX_BEAT_BYTES)
        map_details = check_map(doc, master_hash, pcm)
    elif bpm is None or not isinstance(bpm, (int, float)) or not math.isfinite(bpm) or bpm <= 0:
        raise ValueError('supply exact source-bound beat map or an explicit evidence-backed BPM')
    record_details = None; recording_sha = None
    association = 'no_recording'
    if recording is not None:
        doc, recording_sha = load_json(recording, MAX_RECORDING_BYTES)
        record_details = check_recording(doc)
        provenance = doc['provenance']
        known = [provenance.get(k) for k in ('source_sha256', 'audio_sha256', 'master_sha256')]
        for key in ('source_audio', 'master_audio'):
            nested = provenance.get(key)
            if isinstance(nested, dict):
                known.append(nested.get('sha256'))
        if explicit_retime is not None:
            master_beat, recording_pip, pips_per_beat = explicit_retime
            if not all(math.isfinite(x) for x in explicit_retime) or pips_per_beat <= 0:
                raise ValueError('explicit recording retime needs finite anchors and positive pips/beat')
            if record_details['phase_status'] == 'unverified' and not approve_unverified_phase:
                association = 'explicit_retime_requires_phase_approval'
            else:
                association = 'explicit_retime_approved'
        else:
            if any(v is not None and v != master_hash for v in known):
                raise ValueError('recording provenance disagrees with this source master hash; explicit retiming is required')
            association = ('provenance_hash_matches' if any(v == master_hash for v in known)
                           else 'user_approved' if approve_original_clock else 'unverified_requires_review')
    choices = channels if channels is not None else (['x', 'y', 'z', 'x'] if record_details else [])
    if len(choices) not in (0, 4) or any(not isinstance(x, str) or NAME.fullmatch(x) is None for x in choices):
        raise ValueError('provide exactly four valid channel names, or none')
    if record_details and choices and not any(x in record_details['channel_names'] for x in choices):
        raise ValueError('no requested channel exists in the imported recording')
    return {'schema': SCHEMA, 'song': song,
            'master': {'basename': master.name, 'sha256': master_hash, 'pcm_metadata': pcm},
            'timing': {'mode': 'source_bound_variable_bpm' if timeline else 'explicit_fixed_bpm',
                       'bpm_hint': None if timeline else bpm,
                       'map_basename': Path(timeline).name if timeline else None,
                       'map_sha256': map_sha, 'validated_map': map_details},
            'recording': None if record_details is None else {
                'basename': Path(recording).name, 'sha256': recording_sha,
                'details': record_details, 'association': association,
                'approved_original_source_clock': approve_original_clock,
                'explicit_retime': None if explicit_retime is None else {
                    'master_anchor_beat': explicit_retime[0],
                    'recording_anchor_pip': explicit_retime[1],
                    'recording_pips_per_master_beat': explicit_retime[2],
                    'approved_unverified_phase': approve_unverified_phase},
                'channel_slots': choices},
            'note': 'Receipt records real bytes and author choices; neither BPM phase nor musical matching is inferred.'}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--song', choices=SONGS, required=True)
    p.add_argument('--master', type=Path, required=True)
    p.add_argument('--timeline', type=Path)
    p.add_argument('--recording', type=Path)
    p.add_argument('--bpm', type=float)
    p.add_argument('--approve-original-clock', action='store_true')
    p.add_argument('--retime-master-beat', type=float)
    p.add_argument('--retime-recording-pip', type=float)
    p.add_argument('--retime-pips-per-master-beat', type=float, default=64)
    p.add_argument('--approve-unverified-phase', action='store_true')
    p.add_argument('--channels', nargs=4)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    try:
        if (a.retime_master_beat is None) != (a.retime_recording_pip is None):
            raise ValueError('retiming requires BOTH --retime-master-beat and --retime-recording-pip')
        retime = None if a.retime_master_beat is None else (
            a.retime_master_beat, a.retime_recording_pip, a.retime_pips_per_master_beat)
        if retime is not None and a.approve_original_clock:
            raise ValueError('choose either shared original-source time OR explicit beat retiming')
        receipt = make_receipt(a.song, a.master, a.recording, a.timeline,
                               bpm=a.bpm, approve_original_clock=a.approve_original_clock,
                               channels=a.channels, explicit_retime=retime,
                               approve_unverified_phase=a.approve_unverified_phase)
        if a.output.exists():
            raise FileExistsError('Receipt already exists; choose a new output path to preserve existing evidence.')
        a.output.parent.mkdir(parents=True, exist_ok=True)
        a.output.write_text(json.dumps(receipt, indent=2, ensure_ascii=True, allow_nan=False) + '\n', encoding='utf-8')
        print('Created receipt', a.output, 'master SHA-256', receipt['master']['sha256'])
    except (ValueError, FileNotFoundError, FileExistsError, UnicodeError, OSError, gzip.BadGzipFile) as exc:
        p.error(str(exc))


if __name__ == '__main__':
    main()
