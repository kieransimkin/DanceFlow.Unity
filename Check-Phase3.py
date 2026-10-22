#!/usr/bin/env python3
"""Read-only Phase 3 post-recovery diagnostics for DanceFlow.Unity.

Runs individual tests and retains complete output in a user-local temp folder.
Does not apply patches, edit tracked sources, reset Git or install SDKs.
Python 3.9+; all commands are executed from the validated repo root.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import traceback

REQUIRED = [
    'Samples~/SongReactiveLandscape/SongShowcaseProfile.cs',
    'Samples~/SongReactiveLandscape/RecordedMotionWarp.shader',
    'tools~/showcase-checks/prepare_song_assets.py',
    'Runtime/Motion/RecordingGpuWindow.cs',
    'tools~/recording-checks/RecordingChecks.csproj',
]


def execute(label: str, args: list[str], repo: Path, output: Path, *, timeout: int = 300):
    name = re.sub(r'[^A-Za-z0-9_-]+', '-', label).strip('-').lower()
    logfile = output / (name + '.log')
    print(f'RUN  {label}', flush=True)
    try:
        result = subprocess.run(args, cwd=str(repo), stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                text=True, encoding='utf-8', errors='replace', timeout=timeout, check=False)
        transcript = result.stdout or ''
        logfile.write_text(f'Command: {args!r}\nExit code: {result.returncode}\n\n{transcript}', encoding='utf-8')
        ok = result.returncode == 0
        print(f'{"PASS" if ok else "FAIL"} {label} (exit {result.returncode})')
        if not ok:
            lines = transcript.splitlines()
            print('  --- failure output (last 45 lines) ---')
            for line in lines[-45:]:
                print('  ' + line)
            print('  --- end failure output ---')
        return {'name': label, 'status': 'PASS' if ok else 'FAIL', 'exit_code': result.returncode,
                'log': str(logfile)}
    except (OSError, subprocess.TimeoutExpired) as exc:
        logfile.write_text(f'Command: {args!r}\nError: {exc}\n', encoding='utf-8')
        print(f'FAIL {label} ({exc})')
        return {'name': label, 'status': 'FAIL', 'error': str(exc), 'log': str(logfile)}


def check_dotnet_sdk() -> tuple[str | None, str]:
    exe = shutil.which('dotnet')
    if not exe:
        return None, 'dotnet executable not found'
    try:
        result = subprocess.run([exe, '--list-sdks'], stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, text=True, timeout=30, check=False)
        if result.returncode:
            return None, result.stdout.strip() or 'dotnet --list-sdks failed'
        versions = re.findall(r'^\s*(\d+\.\d+\.\d+)', result.stdout, re.M)
        if not any(v.startswith('8.') for v in versions):
            return None, f'.NET 8 SDK not installed; detected: {", ".join(versions) or "no SDKs"}'
        return exe, result.stdout.strip()
    except (OSError, subprocess.TimeoutExpired) as exc:
        return None, str(exc)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', default='.', help='Path to DanceFlow.Unity checkout')
    parser.add_argument('--output', default=None, help='Destination for diagnostic logs, default a fresh temp folder')
    parser.add_argument('--python-only', action='store_true', help='Skip .NET checks, useful before installing SDK')
    options = parser.parse_args()
    repo = Path(options.repo).resolve()
    manifest = repo / 'package.json'
    if not manifest.is_file() or json.loads(manifest.read_text(encoding='utf-8-sig')).get('name') != 'com.kieransimkin.danceflow.unity':
        print(f'ERROR: Expected DanceFlow.Unity repository at {repo}', file=sys.stderr)
        return 3
    output = (Path(options.output).resolve() if options.output else
              Path(tempfile.mkdtemp(prefix='DanceFlow-Phase3-Checks-')))
    output.mkdir(parents=True, exist_ok=True)
    print('DanceFlow Phase 3 verification (NO file modification)')
    print('Repository:', repo)
    print('Logs:      ', output)
    print('Python:    ', sys.version.split()[0], sys.executable)
    missing = [p for p in REQUIRED if not (repo / p).is_file()]
    if missing:
        print('MISSING REQUIRED SOURCE FILES:')
        for p in missing: print('  -', p)
    else:
        print('Required Phase 3 recovery files: present')
    try:
        head = subprocess.run(['git', '-C', str(repo), 'rev-parse', '--short', 'HEAD'],
                              capture_output=True, text=True, check=False)
        print('HEAD:      ', head.stdout.strip() if head.returncode == 0 else 'unavailable')
        dirty = subprocess.run(['git', '-C', str(repo), 'status', '--short'],
                               capture_output=True, text=True, check=False)
        if dirty.stdout.strip():
            print('Uncommitted changes present (these will NOT be modified).')
    except OSError:
        print('Git unavailable; no source changes will be made.')

    checks = [
        ('Python recording fixture', [sys.executable, 'tools~/recording-checks/verify_fixture.py']),
        ('Python song profile and showcase', [sys.executable, '-m', 'unittest', 'discover',
                                               '-s', 'tools~/showcase-checks', '-p', 'test_*.py', '-v']),
        ('Python shader contract', [sys.executable, 'tools/shader-checks/check_shader_contract.py']),
        ('Python benchmark regressions', [sys.executable, '-m', 'unittest', 'discover',
                                           '-s', 'tools/benchmarks/tests', '-p', 'test_*.py', '-v']),
    ]
    results = [execute(label, cmd, repo, output) for label, cmd in checks]
    dotnet, sdk_detail = check_dotnet_sdk()
    if options.python_only:
        results.append({'name': '.NET checks', 'status': 'SKIP', 'reason': 'Python-only mode'})
        print('SKIP .NET checks (--python-only)')
    elif dotnet is None:
        results.append({'name': '.NET checks', 'status': 'INCOMPLETE', 'reason': sdk_detail})
        print('INCOMPLETE .NET checks:', sdk_detail)
        print('Install the .NET 8 SDK (not just the runtime):')
        print('  winget install --id Microsoft.DotNet.SDK.8 --exact --source winget')
        print('Open a new PowerShell terminal and rerun this diagnostic command.')
    else:
        print('Detected SDKs:', sdk_detail)
        results.extend([
            execute('.NET recording checks', [dotnet, 'run', '--project',
                    'tools~/recording-checks/RecordingChecks.csproj', '-c', 'Release'], repo, output),
            execute('.NET clock checks', [dotnet, 'run', '--project',
                    'tools/clock-checks/ClockChecks.csproj', '-c', 'Release'], repo, output),
        ])
    report = {'schema': 'danceflow.unity.phase3-diagnostics.v1',
              'repository': str(repo), 'generated_utc': dt.datetime.now(dt.timezone.utc).isoformat(),
              'missing_required_source': missing, 'checks': results,
              'notes': 'This script does not modify tracked source files or apply patches.'}
    (output / 'report.json').write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    failures = [r['name'] for r in results if r['status'] == 'FAIL']
    incomplete = [r['name'] for r in results if r['status'] == 'INCOMPLETE']
    print('\nRESULTS')
    for r in results:
        print(f'  {r["status"]:<11} {r["name"]}')
    print('Full logs:', output)
    if missing or failures:
        print('ISSUES: inspect failing logs. Files already recovered remain untouched.')
        return 1
    if incomplete:
        print('INCOMPLETE: available checks passed, but .NET acceptance needs an SDK.')
        return 0
    print('Available local checks passed. Unity Editor/GPU validation is a separate gate.')
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception:
        traceback.print_exc()
        sys.exit(3)
