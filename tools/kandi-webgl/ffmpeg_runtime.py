#!/usr/bin/env python3
"""Resolve only the executable embedded in the SHA-pinned imageio-ffmpeg wheel.

This is a BUILD tool. It does not ship with the Unity player. The wheel and all
platform hashes are pinned by requirements-ffmpeg.txt; neither a system FFmpeg
nor IMAGEIO_FFMPEG_EXE overrides the selected binary.
"""
from __future__ import annotations

import argparse
from importlib.metadata import PackageNotFoundError, version as distribution_version
from pathlib import Path
import subprocess

PINNED_VERSION = "0.6.0"


def pinned_ffmpeg_exe() -> str:
    """Locate the wheel-embedded FFmpeg; never silently fall back to PATH."""
    try:
        import imageio_ffmpeg
        version = distribution_version("imageio-ffmpeg")
    except (ImportError, PackageNotFoundError) as error:
        raise RuntimeError(
            "Install the checksum-pinned imageio-ffmpeg wheel from "
            "tools/kandi-webgl/requirements-ffmpeg.txt"
        ) from error
    if version != PINNED_VERSION:
        raise RuntimeError("Unpinned imageio-ffmpeg version: " + version)
    binary_dir = (Path(imageio_ffmpeg.__file__).resolve().parent / "binaries").resolve()
    # get_ffmpeg_exe may return IMAGEIO_FFMPEG_EXE or a system executable.
    # Refuse either source unless it resolves inside the pinned wheel's binary dir.
    selected = Path(imageio_ffmpeg.get_ffmpeg_exe()).resolve()
    if not selected.is_file() or selected.parent != binary_dir or not selected.name.startswith("ffmpeg-"):
        raise RuntimeError(
            "Refusing external or unbundled FFmpeg executable; a compatible "
            "platform-specific imageio-ffmpeg 0.6.0 wheel is required"
        )
    return str(selected)


def check_pinned_ffmpeg() -> tuple[str, str]:
    exe = pinned_ffmpeg_exe()
    process = subprocess.run(
        [exe, "-hide_banner", "-version"],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        timeout=20,
        check=True,
    )
    first_line = process.stdout.splitlines()[0] if process.stdout else ""
    if not first_line.startswith("ffmpeg version "):
        raise RuntimeError("Pinned executable did not identify itself as FFmpeg")
    return exe, first_line


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="validate wheel and binary")
    args = parser.parse_args()
    try:
        exe, version = check_pinned_ffmpeg()
    except (RuntimeError, OSError, subprocess.SubprocessError) as error:
        parser.exit(1, "Pinned FFmpeg verification failed: " + str(error) + "\n")
    print("Pinned wheel: imageio-ffmpeg==" + PINNED_VERSION)
    print("FFmpeg executable:", exe)
    print("FFmpeg version:", version)


if __name__ == "__main__":
    main()
