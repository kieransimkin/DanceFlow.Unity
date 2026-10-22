#!/usr/bin/env python3
"""Assemble an isolated Unity 6 WebGL project from the real root UPM package.

No network actions: run fetch_assets.py separately. Does not modify the source repo.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import shutil

HERE = Path(__file__).resolve().parent
UNITY_VERSION = "6000.0.23f1"
URP_VERSION = "17.0.3"
PACKAGE_NAME = "com.kieransimkin.danceflow.unity"
ROOT_DIRECTORIES = ("Runtime", "Editor", "Samples~")


def build(repo: Path, dest: Path) -> Path:
    repo = repo.resolve()
    dest = dest.resolve()
    if dest == repo or repo not in dest.parents:
        raise ValueError("Generated Unity project must be inside the source repository")
    if dest.exists():
        raise FileExistsError("Refusing to overwrite a Unity project; use an empty output path")
    pkg = json.loads((repo / "package.json").read_text(encoding="utf-8"))
    if pkg.get("name") != PACKAGE_NAME:
        raise ValueError("Wrong UPM source repository")
    template = HERE / "UnityProject"
    if not (template / "Assets" / "Editor" / "KandiBoomWebGLBuild.cs").is_file():
        raise FileNotFoundError("Unity Editor build template missing")
    def prohibit_symlinks(p: Path):
        for target in p.rglob("*"):
            if target.is_symlink():
                raise ValueError("Refusing to copy symlink in Unity WebGL project")
    prohibit_symlinks(template)
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(template, dest)
    unity_pkg = dest / "Packages" / PACKAGE_NAME
    unity_pkg.mkdir(parents=True)
    (unity_pkg / "package.json").write_bytes((repo / "package.json").read_bytes())
    for f in ("README.md", "CHANGELOG.md", "Runtime.meta", "Editor.meta", "Samples~.meta"):
        if (repo / f).is_file():
            (unity_pkg / f).write_bytes((repo / f).read_bytes())
    for directory in ROOT_DIRECTORIES:
        prohibit_symlinks(repo / directory)
        shutil.copytree(repo / directory, unity_pkg / directory,
                        ignore=shutil.ignore_patterns("__pycache__", "*.pyc", "*.zip", "*.patch"))
    source_dir = dest / "Assets" / "KandiBoomSource"
    source_dir.mkdir(parents=True)
    shutil.copy2(HERE / "asset-lock.json", source_dir / "asset-lock.json")
    # Explicitly enable built-in modules used by DanceFlow runtime assemblies.
    # This generated Unity project starts with no default built-in modules; without
    # these entries Unity reports CS1069 for AudioSource, ParticleSystem and
    # UnityEngine.Animations types before the WebGL build method can run.
    package_deps = {
        "com.unity.render-pipelines.universal": URP_VERSION,
        "com.unity.modules.audio": "1.0.0",
        "com.unity.modules.animation": "1.0.0",
        "com.unity.modules.particlesystem": "1.0.0",
    }
    (dest / "Packages" / "manifest.json").write_text(
        json.dumps({"dependencies": package_deps}, indent=2) + "\n", encoding="utf-8")
    settings = dest / "ProjectSettings"
    settings.mkdir(exist_ok=True)
    (settings / "ProjectVersion.txt").write_text("m_EditorVersion: " + UNITY_VERSION + "\n", encoding="utf-8")
    # Copy only the runtime parts of the optional procedural landscape sample.
    sample = repo / "Samples~" / "SongReactiveLandscape"
    shader_assets = dest / "Assets" / "DanceFlowLandscape"
    shader_assets.mkdir(parents=True)
    for filename in ("ProceduralLandscape.cs", "MotionLandscape.shader"):
        if not (sample / filename).is_file():
            raise FileNotFoundError("Required authentic DanceFlow landscape sample missing: " + filename)
        shutil.copy2(sample / filename, shader_assets / filename)
    print("Unity project assembled using UPM", pkg["version"], "URP", URP_VERSION)
    return dest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=HERE.parents[1])
    parser.add_argument("--output", type=Path, required=True)
    a = parser.parse_args()
    build(a.repo, a.output)

if __name__ == "__main__":
    main()
