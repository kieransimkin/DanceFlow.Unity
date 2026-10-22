#!/usr/bin/env python3
"""Conservatively recover the *missing* Phase 3 files from the user's own Git patches.

Does not reset, stash, force, overwrite, fetch from network or apply unrelated paths.
Uses file-level Git apply checks and byte-pinned source patches. Python 3.9+.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

CUMULATIVE_NAME = "DanceFlow.Unity-Phase3-Cumulative.patch"
SHOWCASE_NAME = "Phase3-From-Showcase.patch"
PINNED_SHA256 = {
    CUMULATIVE_NAME: "61f1ad4dd07c56bd19e380b1fe7070ec6a6b76b9a6291537a208724d20dfe0c1",
    SHOWCASE_NAME: "84f1a04ff0e101e292f54b03d2edeef6b85ee159cd2b16322853359836d1e63c",
}

# Tracked Phase 3 files at HEAD whose canonical blob still matches the source
# patch's known base, and whose modifications are safe to apply individually.
CUMULATIVE_SAFE_MODIFICATIONS = {
    ".gitignore",
    "Runtime/Clock/DanceFlowClockBehaviour.cs",
    "tools/clock-checks/ClockChecks.csproj",
    "tools/clock-checks/Program.cs",
}

# Stage 3B paths which must be updated from the compatibility *delta*, not the
# cumulative initial-add patch. A different HEAD blob is never overwritten.
SHOWCASE_SAFE_MODIFICATIONS = {
    "Runtime/GPU/DanceFlowMotionBuffer.hlsl",
    "Runtime/GPU/MotionRecordingGpuBridge.cs",
    "Runtime/Motion/MotionSignalBus.cs",
    "Samples~/SongReactiveLandscape/Editor/CreateSongShowcaseScene.cs",
    "Samples~/SongReactiveLandscape/SongShowcaseBootstrap.cs",
    "Tests/Editor/MotionShowcaseTests.cs",
    "tools~/recording-checks/Program.cs",
}

# Diverged documentation/metadata have intentionally independent changes;
# replacing them with the original cumulative diff would lose those updates.
PRESERVED_DIVERGED = (
    ".github/workflows/clock-contracts.yml",
    "CHANGELOG.md",
    "README.md",
    "package.json",
    "docs/04-recording-format.md",
    "docs/09-song-reactive-showcase.md",
)


def git(repo: Path, *args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(["git", "-C", str(repo), *args], text=True,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          encoding="utf-8", errors="replace", check=False)


def must_succeed(result: subprocess.CompletedProcess[str], label: str) -> str:
    if result.returncode:
        raise RuntimeError(f"{label}:\n{result.stdout}{result.stderr}".rstrip())
    return result.stdout.strip()


def patches_from(path: Path) -> dict[str, dict[str, str | bool]]:
    raw = path.read_bytes()
    expected = PINNED_SHA256[path.name]
    actual = hashlib.sha256(raw).hexdigest()
    if actual != expected:
        raise RuntimeError(f"{path.name} has unexpected SHA-256 {actual}; "
                           f"expected the published version {expected}. No files changed.")
    text = raw.decode("utf-8-sig")
    blocks = re.split(r"(?=^diff --git )", text, flags=re.MULTILINE)
    entries: dict[str, dict[str, str | bool]] = {}
    for block in blocks:
        if not block.startswith("diff --git "):
            continue
        header = re.search(r"^diff --git a/(.+?) b/(.+?)\s*$", block, re.M)
        index = re.search(r"^index ([a-f0-9]+)\.\.([a-f0-9]+)", block, re.M)
        if not header or not index:
            raise RuntimeError(f"Unrecognised file diff in {path.name}; refusing to guess.")
        oldname, newname = header.group(1), header.group(2)
        if oldname != newname or '\\' in newname or '"' in newname or newname.startswith('/'):
            raise RuntimeError("Unsupported or unsafe patch path: " + newname)
        if any(part in ('', '.', '..') for part in newname.split('/')):
            raise RuntimeError("Unsafe patch path: " + newname)
        if newname in entries:
            raise RuntimeError("Duplicate file patch: " + newname)
        entries[newname] = {"source": block, "new": bool(re.search(r"^new file mode ", block, re.M)),
                            "oldhash": index.group(1), "newhash": index.group(2)}
    if not entries:
        raise RuntimeError(f"No patch entries found in {path.name}.")
    return entries


def head_hash(repo: Path, path: str) -> str | None:
    r = git(repo, "rev-parse", f"HEAD:{path}")
    return r.stdout.strip() if r.returncode == 0 else None


def file_exists(repo: Path, rel: str) -> bool:
    absolute = (repo / rel).resolve()
    if os.path.commonpath((str(repo), str(absolute))) != str(repo):
        raise RuntimeError("Path escapes checkout: " + rel)
    return absolute.exists() or absolute.is_symlink()


def file_context_applies(repo: Path, patch: Path, rel: str) -> bool:
    return git(repo, "apply", "--check", f"--include={rel}", "--", str(patch)).returncode == 0


def build_selected(repo: Path, cum_path: Path, show_path: Path, context_merge: bool):
    cumulative = patches_from(cum_path)
    showcase = patches_from(show_path)
    selected: dict[str, tuple[str, str]] = {}
    added, updated, already, conflicts = [], [], [], []

    for rel, data in cumulative.items():
        if not data['new']:
            continue
        if not file_exists(repo, rel):
            if file_context_applies(repo, cum_path, rel):
                selected[rel] = (CUMULATIVE_NAME, str(data['source']))
                added.append(rel)
            else:
                conflicts.append((rel, "missing new file but Git refuses its creation"))
        else:
            # Existing files stay untouched even if they differ from the original
            # cumulative target; this is the specific problem we are repairing.
            already.append(rel)

    def offer_modification(rel: str, data: dict[str, str | bool], source_path: Path):
        if rel in selected:
            return
        if not file_exists(repo, rel):
            conflicts.append((rel, "expected existing tracked file is missing"))
            return
        sha = head_hash(repo, rel)
        if sha is not None and sha.startswith(str(data['newhash'])):
            already.append(rel)
            return
        # No local dirty tracked files are allowed by the caller, so HEAD blob is
        # a faithful test of the current base. A divergent context can optionally
        # be merged only if git apply --check finds an exact applicable hunk.
        if (sha is None or not sha.startswith(str(data['oldhash']))) and not context_merge:
            conflicts.append((rel, "independently modified since patch base (preserved)"))
            return
        if file_context_applies(repo, source_path, rel):
            selected[rel] = (source_path.name, str(data['source']))
            updated.append(rel)
        else:
            conflicts.append((rel, "patch context differs (preserved)"))

    for rel in sorted(CUMULATIVE_SAFE_MODIFICATIONS):
        if rel not in cumulative:
            raise RuntimeError("Expected path absent from cumulative patch: " + rel)
        offer_modification(rel, cumulative[rel], cum_path)
    for rel in sorted(SHOWCASE_SAFE_MODIFICATIONS):
        if rel not in showcase:
            raise RuntimeError("Expected path absent from showcase compatibility delta: " + rel)
        offer_modification(rel, showcase[rel], show_path)

    return selected, added, updated, already, conflicts


def run_checks(repo: Path):
    commands = [
        ("Python recording fixture", [sys.executable, "tools~/recording-checks/verify_fixture.py"]),
        ("Python song-profile/showcase tests", [sys.executable, "-m", "unittest", "discover", "-s", "tools~/showcase-checks", "-p", "test_*.py", "-v"]),
        ("Python shader contract", [sys.executable, "tools/shader-checks/check_shader_contract.py"]),
    ]
    if shutil.which("dotnet"):
        commands.append((".NET recording checks", ["dotnet", "run", "--project", "tools~/recording-checks/RecordingChecks.csproj", "-c", "Release"]))
    else:
        print("  SKIP .NET: dotnet CLI not installed; managed compilation still required")
    failed = []
    for label, command in commands:
        print("  RUN " + label, flush=True)
        proc = subprocess.run(command, cwd=repo, check=False)
        if proc.returncode:
            failed.append(label)
    if failed:
        print("  CHECK FAILURES: " + ", ".join(failed))
        return False
    print("  Available local checks passed. Unity Editor/GPU hardware tests remain separate.")
    return True


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--repo", default=".", help="Path to the DanceFlow.Unity repo; defaults to current directory")
    p.add_argument("--patch-dir", default=None, help="Directory containing the two original Phase 3 patches")
    p.add_argument("--dry-run", action="store_true", help="Only identify and validate the targeted patch; write nothing")
    p.add_argument("--checks", action="store_true", help="Run available Python/.NET tests after applying")
    p.add_argument("--context-merge", action="store_true", help="Also permit compatible changed-file hunks with differing HEAD hashes; inspect carefully")
    args = p.parse_args()
    repo = Path(args.repo).resolve()
    if not (repo / "package.json").is_file():
        raise RuntimeError("Run from DanceFlow.Unity or use --repo <path>.")
    data = json.loads((repo / "package.json").read_text(encoding="utf-8-sig"))
    if data.get("name") != "com.kieransimkin.danceflow.unity":
        raise RuntimeError("This is not the DanceFlow.Unity UPM package.")
    must_succeed(git(repo, "rev-parse", "--show-toplevel"), "Git repository check")
    if git(repo, "diff", "--quiet").returncode != 0 or git(repo, "diff", "--cached", "--quiet").returncode != 0:
        raise RuntimeError("Tracked local changes exist; refusing to merge or overwrite them. Commit or inspect first.")
    patch_dir = Path(args.patch_dir).resolve() if args.patch_dir else repo
    cp = patch_dir / CUMULATIVE_NAME
    sp = patch_dir / SHOWCASE_NAME
    for path in (cp, sp):
        if not path.is_file():
            raise RuntimeError(f"Missing pinned source patch: {path}")

    selected, additions, updates, already, conflicts = build_selected(repo, cp, sp, args.context_merge)
    print(f"Published checkout: {must_succeed(git(repo, 'rev-parse', '--short', 'HEAD'), 'Read HEAD')}")
    print(f"Preflight: {len(additions)} missing files to create, {len(updates)} clean tracked modifications, {len(already)} existing files preserved.")
    if conflicts:
        print(f"  {len(conflicts)} paths need separate review (not touched):")
        for rel, why in conflicts:
            print(f"    {rel}: {why}")

    if not selected:
        print("Nothing safe remains to apply. No changes made.")
        return 0
    # Construct one actual targeted patch (only specific missing or compatible
    # entries) from the exact user-owned, hash-pinned original Git patches.
    # It cannot recreate already existing files or modify unrelated docs.
    targeted = "".join(section for _, section in selected.values())
    if not targeted.endswith("\n"):
        targeted += "\n"
    with tempfile.TemporaryDirectory(prefix="danceflow-phase3-recovery-") as tmp:
        target = Path(tmp) / "Phase3-Targeted-Recovery.patch"
        target.write_bytes(targeted.encode("utf-8"))
        result = git(repo, "apply", "--check", "--whitespace=error", "--", str(target))
        must_succeed(result, "Final all-path patch preflight")
        if args.dry_run:
            print("DRY RUN: targeted patch passes Git preflight; no tracked file was changed.")
            return 0
        must_succeed(git(repo, "apply", "--whitespace=error", "--", str(target)), "Applying targeted recovery patch")
    print(f"Applied {len(selected)} independent, compatible file changes; nothing was reset or force-applied.")
    must_succeed(git(repo, "diff", "--check"), "Git whitespace validation")
    missing_after = [rel for rel in additions if not file_exists(repo, rel)]
    if missing_after:
        raise RuntimeError("Unexpected missing outputs: " + ", ".join(missing_after))
    print("Important: independently edited README, package metadata, changelog and docs remain unchanged.")
    print("Inspect `git status --short` and `git diff --stat` before committing. Avoid adding installer/patch bundles.")
    if args.checks and not run_checks(repo):
        print("Source changes remain in the checkout so their diagnostics can be inspected. No commit was made.")
        return 2
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, RuntimeError, ValueError) as exc:
        print("ERROR: " + str(exc), file=sys.stderr)
        sys.exit(1)
