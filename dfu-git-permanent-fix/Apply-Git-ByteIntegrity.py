#!/usr/bin/env python3
"""Safe, narrow, repeatable Git byte-integrity + step-sampling test fix.

Affects only .gitattributes and tools~/recording-checks/Program.cs,
and never changes Git configuration or recording data.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

ATTR = Path('.gitattributes')
TEST = Path('tools~/recording-checks/Program.cs')
RECEIPT = Path('Tests/Fixtures/dancerudiments-four-axes-v1.receipt.json')
FIXTURE = Path('Tests/Fixtures/dancerudiments-four-axes-v1.json')

OLD_ATTR = (
    '# Byte-exact, SHA-256-pinned fixture: checkout MUST preserve LF line endings.\n'
    'Tests/Fixtures/dancerudiments-four-axes-v1.json text eol=lf\n'
    'Tests/Fixtures/dancerudiments-four-axes-v1.receipt.json text eol=lf\n'
)
NEW_ATTR = (
    '# Byte-exact SHA-256 fixtures: never apply core.autocrlf or EOL conversion.\n'
    '# Keep readable Git diffs, but preserve original blob bytes on every checkout.\n'
    '.gitattributes text eol=lf\n'
    'Tests/Fixtures/dancerudiments-four-axes-v1.json -text diff\n'
    'Tests/Fixtures/dancerudiments-four-axes-v1.receipt.json -text diff\n'
)
OLD_EXPECTATION = b'Near(recording.SampleAtPip(-31.5, 0, RecordingInterpolation.Step), 0.3,'
NEW_EXPECTATION = b'Near(recording.SampleAtPip(-31.5, 0, RecordingInterpolation.Step), 0.2,'
AFTER = b'            "Exact pip-window (step) sampling");'
ADDITIONAL_TEST = (
    b'        Near(recording.SampleAtPip(-31, 0, RecordingInterpolation.Step), 0.3,\n'
    b'            "Step sampling advances only at the following integer pip");\n'
)


def git(root: Path, *args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(['git', '-C', str(root), *args], capture_output=True,
                          text=True, check=check)


def require_clean_targets(root: Path, attr_change: bool, test_change: bool) -> None:
    for rel in ([ATTR] if attr_change else []) + ([TEST] if test_change else []):
        r = git(root, 'diff', '--quiet', '--', rel.as_posix(), check=False)
        if r.returncode != 0:
            raise ValueError(f'Uncommitted tracked changes in {rel}; preserve/review them first.')
        staged = git(root, 'diff', '--cached', '--quiet', '--', rel.as_posix(), check=False)
        if staged.returncode != 0:
            raise ValueError(f'Staged changes in {rel}; preserve/review them first.')


def plan(root: Path):
    attrs = (root / ATTR).read_bytes()
    # Normalize only for comparing expectations. Do not normalise fixtures.
    normalized = attrs.replace(b'\r\n', b'\n').decode('utf-8-sig')
    if normalized not in (OLD_ATTR, NEW_ATTR):
        raise ValueError('Unexpected .gitattributes contents; refusing to overwrite other rules.')
    attr_change = normalized != NEW_ATTR

    source = (root / TEST).read_bytes()
    nold = source.count(OLD_EXPECTATION)
    nnew = source.count(NEW_EXPECTATION)
    if (nold == 1 and nnew == 0):
        out = source.replace(OLD_EXPECTATION, NEW_EXPECTATION)
        # Optional extra assertion of integer boundary. Preserve source line endings.
        newline = b'\r\n' if b'\r\n' in source else b'\n'
        anchor = AFTER + newline
        if out.count(anchor) != 1:
            raise ValueError('The Phase 3 test body changed; refusing to edit unknown source.')
        out = out.replace(anchor, anchor + ADDITIONAL_TEST.replace(b'\n', newline), 1)
        test_change = True
    elif nnew == 1 and nold == 0:
        out = source
        test_change = False
    else:
        raise ValueError('Unexpected step-test assertion; refusing to replace unknown code.')

    raw = (root / FIXTURE).read_bytes()
    expected = json.loads((root / RECEIPT).read_text(encoding='utf-8'))['source_sha256']
    actual = hashlib.sha256(raw).hexdigest()
    if actual != expected:
        raise ValueError('Byte-exact source fixture fails SHA-256. No changes made; verify fixture first.')
    return attr_change, test_change, out


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, default=Path.cwd())
    parser.add_argument('--apply', action='store_true', help='Actually edit the two guarded files; Git configuration is left unchanged')
    args = parser.parse_args()
    root = args.repo.resolve()
    top = Path(git(root, 'rev-parse', '--show-toplevel').stdout.strip()).resolve()
    if top != root:
        raise ValueError(f'Run from the repository root, not {root}. Root: {top}')
    package = json.loads((root / 'package.json').read_text(encoding='utf-8'))
    if package.get('name') != 'com.kieransimkin.danceflow.unity':
        raise ValueError('Refusing to change another repository.')
    attr_change, test_change, updated = plan(root)
    require_clean_targets(root, attr_change, test_change)
    print(f'.gitattributes: {"change" if attr_change else "already protected"}')
    print(f'Step sampling test: {"correct 0.3 -> 0.2" if test_change else "already corrected"}')
    print('Pinned fixture: SHA-256 verified, unchanged')
    if not args.apply:
        print('Dry run only. Add --apply to write changes.')
        return 0
    if attr_change:
        (root / ATTR).write_bytes(NEW_ATTR.encode('utf-8'))
    if test_change:
        (root / TEST).write_bytes(updated)
    print('Git configuration unchanged; .gitattributes overrides core.autocrlf for these fixtures.')
    print('Git attributes for fixture:')
    for path in [FIXTURE, RECEIPT]:
        r = git(root, 'check-attr', 'text', 'diff', '--', path.as_posix())
        print(r.stdout.rstrip())
    print('Done. Review git diff --check, then run Python and .NET checks.')
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (ValueError, OSError, subprocess.CalledProcessError) as exc:
        print(f'REFUSED: {exc}', file=sys.stderr)
        sys.exit(2)
