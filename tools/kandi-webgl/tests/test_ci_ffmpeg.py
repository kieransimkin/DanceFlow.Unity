"""Pin FFmpeg wheel bytes and prevent accidental system-decoder fallback."""
from __future__ import annotations

from array import array
from pathlib import Path
import subprocess
import sys
import tempfile
import types
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[3]
TOOLS = ROOT / "tools" / "kandi-webgl"
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))
import fetch_songs
import ffmpeg_runtime


class PinnedWebGLAudioToolchainTests(unittest.TestCase):
    def test_runner_installs_hash_pinned_wheel_before_decoding(self):
        workflow = (ROOT / ".github/workflows/unity-webgl-pages.yml").read_text(encoding="utf-8")
        setup = workflow.index("- name: Install checksum-pinned FFmpeg wheel")
        audio = workflow.index("- name: Prepare verified song audio")
        self.assertLess(setup, audio)
        for required in ("cache: pip", "cache-dependency-path:", "--require-hashes",
                         "--only-binary=:all:", "--no-deps", "--force-reinstall",
                         "timeout-minutes: 7", "ffmpeg_runtime.py --check"):
            with self.subTest(required=required):
                self.assertIn(required, workflow)
        self.assertNotIn("apt-get", workflow.replace("never invoke apt-get", ""))
        self.assertNotIn("command -v ffmpeg", workflow)

    def test_all_platform_wheels_have_sha256_pins(self):
        requirements = (TOOLS / "requirements-ffmpeg.txt").read_text(encoding="utf-8")
        self.assertIn("imageio-ffmpeg==0.6.0", requirements)
        from re import findall
        pins = findall(r"--hash=sha256:([0-9a-f]{64})", requirements)
        self.assertEqual(len(pins), 6)
        self.assertEqual(len(set(pins)), 6)
        self.assertIn("c7e46fcec401dd990405049d2e2f475e2b397779df2519b544b8aab515195282", pins)

    def test_wheel_binary_accepted_only_from_its_binary_dir(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp) / "imageio_ffmpeg"
            binaries = base / "binaries"
            binaries.mkdir(parents=True)
            package_init = base / "__init__.py"
            package_init.write_text("# mock package", encoding="utf-8")
            executable = binaries / "ffmpeg-test-pinned"
            executable.write_bytes(b"dummy executable for pure unit test")
            fake_package = types.ModuleType("imageio_ffmpeg")
            fake_package.__file__ = str(package_init)
            fake_package.get_ffmpeg_exe = lambda: str(executable)
            with mock.patch.dict(sys.modules, {"imageio_ffmpeg": fake_package}), \
                 mock.patch.object(ffmpeg_runtime, "distribution_version", return_value="0.6.0"):
                self.assertEqual(ffmpeg_runtime.pinned_ffmpeg_exe(), str(executable))
                fake_package.get_ffmpeg_exe = lambda: "/usr/bin/ffmpeg"
                with self.assertRaisesRegex(RuntimeError, "Refusing external"):
                    ffmpeg_runtime.pinned_ffmpeg_exe()
                fake_package.get_ffmpeg_exe = lambda: str(executable)
                with mock.patch.object(ffmpeg_runtime, "distribution_version", return_value="0.5.1"):
                    with self.assertRaisesRegex(RuntimeError, "Unpinned"):
                        ffmpeg_runtime.pinned_ffmpeg_exe()

    def test_decoder_invokes_the_pinned_executable(self):
        fake_pcm = array("f", [0.0] * (fetch_songs.SAMPLE_RATE * 2)).tobytes()
        fake_result = subprocess.CompletedProcess(args=[], returncode=0, stdout=fake_pcm, stderr=b"")
        with mock.patch.object(fetch_songs, "pinned_ffmpeg_exe", return_value="/pinned/ffmpeg"), \
             mock.patch.object(fetch_songs.subprocess, "run", return_value=fake_result) as execute:
            samples = fetch_songs.decode_pcm_mp3(Path("example.mp3"))
        self.assertEqual(len(samples), fetch_songs.SAMPLE_RATE * 2)
        self.assertEqual(execute.call_args.args[0][0], "/pinned/ffmpeg")
        self.assertIn("-f", execute.call_args.args[0])


if __name__ == "__main__":
    unittest.main()
