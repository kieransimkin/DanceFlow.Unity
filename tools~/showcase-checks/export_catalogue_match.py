#!/usr/bin/env python3
"""Export a VERIFIED native DanceRudiments catalogue pattern to finite recording JSON.

This does not calculate musical matches; --pattern must come from a separately
measured StemLab candidate report. Its origin remains unverified until reviewed.
The canonical DanceRudiments C++ sampler is the only source of motion values.
"""
from __future__ import annotations
import argparse
from pathlib import Path
import math


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--pattern', required=True)
    ap.add_argument('--bpm', type=float, required=True,
                    help='Explicit tempo for the exact target recording; not auto-detected')
    ap.add_argument('--beats', type=int, default=64)
    ap.add_argument('--output', type=Path, required=True)
    ap.add_argument('--bpm-source', default='explicit user-supplied song tempo')
    args = ap.parse_args()
    if not (math.isfinite(args.bpm) and args.bpm > 0):
        ap.error('BPM must be finite and positive')
    if not (1 <= args.beats <= 4096):
        ap.error('--beats must lie in 1..4096')
    try:
        import dancerudiments as native
        from dancerudiments_authoring.recording import Recording, RecordingClock, write_recording
    except ImportError as error:
        ap.error('Install the matching native dancerudiments and dancerudiments_authoring packages: ' + str(error))
    available = {row['name'] for row in native.catalogue()}
    if args.pattern not in available:
        ap.error('Pattern not found in the installed official DanceRudiments catalogue: ' + args.pattern)
    rows = []
    for pip in range(args.beats * 64):
        values = tuple(float(v) for v in native.sample(args.pattern, pip).as_tuple())
        if len(values) != 3 or any(not math.isfinite(v) or abs(v) > 1 for v in values):
            raise ValueError(f'Native pattern produced unsupported/non-normalised value at pip {pip}')
        rows.append(values)
    recording = Recording(
        name='catalogue_' + args.pattern,
        dimensions=('x', 'y', 'z'),
        samples=tuple(rows),
        clock=RecordingClock(bpm=args.bpm, bpm_source=args.bpm_source,
                             bpm_status='estimated', beat_zero_seconds=0,
                             phase_status='unverified', start_pip=0),
        provenance_json=__import__('json').dumps({
            'source': 'official DanceRudiments native catalogue',
            'pattern': args.pattern,
            'interpretation': 'Finite re-sampling of a procedural loop; NOT evidence of confirmed beat alignment',
            'sample_rate': '64 musical pips/beat',
        }),
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    write_recording(args.output, recording, overwrite=False)
    print(f'Wrote {args.output} ({len(rows)} native pips, explicit BPM {args.bpm}, unverified phase)')


if __name__ == '__main__':
    main()
