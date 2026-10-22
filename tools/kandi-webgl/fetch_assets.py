#!/usr/bin/env python3
"""Download exactly the audited Kandi Boom FBX/PBR files from a pinned DanceRudiments commit.

Never executes remote code, never accepts arbitrary URLs/paths and never silently uses
another model. Validate both exact SHA-256 and file length before atomically installing.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import tempfile
import urllib.request

HERE = Path(__file__).resolve().parent
LOCK = HERE / "asset-lock.json"
MAX_FILE_BYTES = 48 * 1024 * 1024


def git_blob_sha1(data: bytes) -> str:
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\x00" + data).hexdigest()


def checked_lock(lock: dict) -> dict:
    if lock.get("schema") != "danceflow.unity.kandi-boom-assets.v1":
        raise ValueError("Unexpected Kandi Boom asset lock format")
    if lock.get("repository") != "kieransimkin/DanceRudiments":
        raise ValueError("Source must be the public DanceRudiments repository")
    if len(lock.get("commit", "")) != 40 or any(c not in "0123456789abcdef" for c in lock["commit"]):
        raise ValueError("Immutable 40-hex source commit required")
    if lock.get("asset_root") != "harness/kandi-boom/assets":
        raise ValueError("Unexpected Kandi Boom asset root")
    expected = {"kandi-boom.fbx", "albedo.png", "normal.png", "metallic.png", "roughness.png"}
    assets = lock.get("assets")
    if not isinstance(assets, list) or {item.get("name") for item in assets} != expected or len(assets) != 5:
        raise ValueError("Expected original FBX plus four named PBR textures")
    for entry in assets:
        if type(entry.get("bytes")) is not int or not 0 < entry["bytes"] <= MAX_FILE_BYTES:
            raise ValueError("Invalid asset size constraint")
        if len(entry.get("sha256", "")) != 64 or any(c not in "0123456789abcdef" for c in entry["sha256"]):
            raise ValueError("Invalid SHA-256 constraint")
    if len(lock.get("bones", [])) != 24 or len(set(lock["bones"])) != 24:
        raise ValueError("Original Kandi Boom must declare exactly 24 distinct bones")
    audits = lock.get("audit_files", [])
    expected_audits = {
        "rig-audit.json": "harness/kandi-boom/rig-audit.json",
        "source-rig.json": "harness/kandi-boom/source-rig.json"
    }
    if len(audits) != 2 or {item.get("name"): item.get("path") for item in audits} != expected_audits:
        raise ValueError("Unrecognised audit path or missing source manifest")
    for item in audits:
        sha1 = item.get("git_blob_sha1", "")
        if len(sha1) != 40 or any(ch not in "0123456789abcdef" for ch in sha1):
            raise ValueError("Audit Git blob hash is invalid")
    return lock


def download_bytes(url: str, limit: int = MAX_FILE_BYTES) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": "DanceFlow.Unity-KandiBoom-PinnedAssetImporter/1"})
    with urllib.request.urlopen(req, timeout=120) as response:
        if response.status != 200:
            raise RuntimeError("Unexpected asset HTTP status " + str(response.status))
        chunks: list[bytes] = []
        total = 0
        while True:
            chunk = response.read(min(1024 * 1024, limit + 1 - total))
            if not chunk:
                break
            chunks.append(chunk)
            total += len(chunk)
            if total > limit:
                raise ValueError("Pinned source download exceeded size limit")
    return b"".join(chunks)


def install_bytes(target: Path, data: bytes) -> None:
    if target.is_symlink():
        raise ValueError("Refusing to overwrite a symbolic link")
    if target.exists():
        if target.read_bytes() == data:
            return
        raise FileExistsError("Refusing to overwrite existing nonmatching asset: " + target.name)
    fd, temporary = tempfile.mkstemp(prefix=".kandi-", dir=str(target.parent))
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        if target.exists():
            raise FileExistsError("Concurrent Kandi asset creation: " + target.name)
        os.replace(temporary, target)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def acquire(lock: dict, dest: Path, download=download_bytes) -> dict:
    lock = checked_lock(lock)
    if dest.is_symlink():
        raise ValueError("Destination cannot be a symbolic link")
    dest.mkdir(parents=True, exist_ok=True)
    if any(child.is_symlink() for child in dest.iterdir()):
        raise ValueError("No symbolic links may exist in the output directory")
    base = f"https://raw.githubusercontent.com/{lock['repository']}/{lock['commit']}/"
    receipt = []
    for entry in lock["assets"]:
        path = dest / entry["name"]
        url = base + lock["asset_root"] + "/" + entry["name"]
        if path.exists():
            data = path.read_bytes()
        else:
            data = download(url, entry["bytes"])
        if len(data) != entry["bytes"] or hashlib.sha256(data).hexdigest() != entry["sha256"]:
            raise ValueError("Kandi Boom source asset failed exact SHA-256 and byte-length verification: " + entry["name"])
        install_bytes(path, data)
        receipt.append({"name": entry["name"], "bytes": len(data), "sha256": entry["sha256"]})
        print("Verified original Kandi asset:", entry["name"], len(data), "bytes")
    for entry in lock["audit_files"]:
        path = dest / entry["name"]
        data = path.read_bytes() if path.exists() else download(base + entry["path"], 1024 * 1024)
        if git_blob_sha1(data) != entry["git_blob_sha1"]:
            raise ValueError("Git blob identity mismatch for " + entry["name"])
        install_bytes(path, data)
    audit = json.loads((dest / "rig-audit.json").read_text(encoding="utf-8"))
    source = json.loads((dest / "source-rig.json").read_text(encoding="utf-8"))
    if audit.get("bone_count") != 24 or audit.get("mesh_count") != 1 or \
       source.get("type_uri") != lock["target_type"] or \
       source.get("fbx_asset_sha256") != lock["assets"][0]["sha256"]:
        raise ValueError("Kandi Boom source rig identity/morphology audit did not match")
    if [row.get("name") for row in source.get("bones", [])] != lock["bones"]:
        raise ValueError("Original Kandi Boom source bone order changed")
    for asset in lock["assets"]:
        if audit["assets"][asset["name"]]["sha256"] != asset["sha256"]:
            raise ValueError("Local lock disagrees with pinned original source audit")
    return {"schema": "danceflow.unity.kandi-asset-receipt.v1", "source_commit": lock["commit"],
            "character": "Kandi Boom", "bone_count": 24, "files": receipt}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path, help="Unity project Assets/KandiBoomSource directory")
    args = parser.parse_args()
    lock = json.loads(LOCK.read_text(encoding="utf-8"))
    receipt = acquire(lock, args.output)
    (args.output / "asset-receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print("Original Kandi Boom FBX, textures and 24-bone rig: VERIFIED")

if __name__ == "__main__":
    main()
