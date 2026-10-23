"""Require an explicit FFmpeg bootstrap before CI processes real song audio."""
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[3]


class WebGLAudioToolchainTests(unittest.TestCase):
    def test_runner_provides_ffmpeg_before_source_verified_song_processing(self):
        workflow = (ROOT / ".github/workflows/unity-webgl-pages.yml").read_text(encoding="utf-8")
        bootstrap = workflow.index("- name: Ensure FFmpeg is available")
        analysis = workflow.index("- name: Prepare verified song audio")
        self.assertLess(bootstrap, analysis, "FFmpeg must be available before decoding songs")
        self.assertIn("if ! command -v ffmpeg", workflow)
        self.assertIn("sudo apt-get update -qq", workflow)
        self.assertIn("apt-get install -y --no-install-recommends ffmpeg", workflow)
        self.assertIn("python tools/kandi-webgl/fetch_songs.py", workflow)
        self.assertLess(workflow.index("apt-get install -y --no-install-recommends ffmpeg"),
                        workflow.index("python tools/kandi-webgl/fetch_songs.py"))


if __name__ == "__main__":
    unittest.main()
