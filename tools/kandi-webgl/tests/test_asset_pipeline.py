"""Offline tests for SHA-bound original Kandi assets and Unity project packaging."""
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
import fetch_assets
import prepare_project
import publish_pages


class AssetAcquisitionTests(unittest.TestCase):
    @staticmethod
    def synthetic_lock():
        locked = json.loads(fetch_assets.LOCK.read_text(encoding="utf-8"))
        files = {name: (name + ":known-binary-test").encode() for name in
                 ("kandi-boom.fbx", "albedo.png", "normal.png", "metallic.png", "roughness.png")}
        for asset in locked["assets"]:
            content = files[asset["name"]]
            asset["bytes"] = len(content)
            asset["sha256"] = hashlib.sha256(content).hexdigest()
        audit = {"bone_count":24,"mesh_count":1,
                 "assets": {a["name"]:{"sha256":a["sha256"]} for a in locked["assets"]}}
        rig = {"type_uri":locked["target_type"],
               "fbx_asset_sha256":locked["assets"][0]["sha256"],
               "bones":[{"name":name} for name in locked["bones"]]}
        for name, payload in (("rig-audit.json", audit), ("source-rig.json", rig)):
            data = json.dumps(payload).encode()
            files[name] = data
            for item in locked["audit_files"]:
                if item["name"] == name:
                    item["git_blob_sha1"] = fetch_assets.git_blob_sha1(data)
        return locked, files

    def test_original_source_lock_has_expected_immutable_assets(self):
        locked = fetch_assets.checked_lock(json.loads(fetch_assets.LOCK.read_text()))
        self.assertEqual(len(locked["bones"]), 24)
        self.assertEqual(len(locked["assets"]), 5)
        self.assertEqual(locked["assets"][0]["sha256"],
                         "714b6a182f2a5684487cd0227b6c9175da604d9c1f98416d44ee23f465ca488e")
        self.assertEqual(locked["commit"], "08e8628cb31fdc124358ae6f818c081f4cf0e96e")

    def test_verified_source_and_metadata_are_idempotent(self):
        lock, files = self.synthetic_lock()
        requests = []
        def download(url, limit):
            name = url.rsplit("/", 1)[-1]
            self.assertEqual(url.split("/")[3:5], ["kieransimkin", "DanceRudiments"])
            requests.append(name)
            return files[name]
        with tempfile.TemporaryDirectory() as temp:
            target = Path(temp) / "Assets" / "KandiBoomSource"
            receipt = fetch_assets.acquire(lock, target, download)
            self.assertEqual(receipt["bone_count"], 24)
            self.assertEqual(set(requests), set(files))
            self.assertEqual(fetch_assets.acquire(lock, target, download), receipt)
            self.assertEqual(len(requests), len(files))

    def test_corrupt_model_rejected_and_never_published(self):
        lock, files = self.synthetic_lock()
        files["kandi-boom.fbx"] += b"altered"
        with tempfile.TemporaryDirectory() as temp:
            target = Path(temp) / "assets"
            with self.assertRaisesRegex(ValueError, "SHA-256"):
                fetch_assets.acquire(lock, target, lambda url, lim: files[url.rsplit("/", 1)[-1]])
            self.assertFalse((target / "kandi-boom.fbx").exists())

    def test_unrelated_file_never_overwritten(self):
        lock, files = self.synthetic_lock()
        with tempfile.TemporaryDirectory() as temp:
            target = Path(temp) / "assets"
            target.mkdir()
            (target / "albedo.png").write_bytes(b"not the verified map")
            with self.assertRaisesRegex(ValueError, "SHA-256"):
                fetch_assets.acquire(lock, target, lambda url, lim: files[url.rsplit("/", 1)[-1]])
            self.assertEqual((target / "albedo.png").read_bytes(), b"not the verified map")

    def test_unsafe_audit_paths_rejected(self):
        lock, _ = self.synthetic_lock()
        lock["audit_files"][0]["path"] = "../../evil"
        with self.assertRaises(ValueError):
            fetch_assets.checked_lock(lock)


class ProjectAndDeployTests(unittest.TestCase):
    def test_project_assembly_uses_existing_upm_and_real_landscape(self):
        with tempfile.TemporaryDirectory() as td:
            source = Path(td) / "DanceFlow.Unity"
            source.mkdir()
            (source / "package.json").write_text(json.dumps({"name":prepare_project.PACKAGE_NAME, "version":"0.5.0-preview.1"}))
            for folder in ("Runtime", "Editor", "Samples~"):
                (source / folder).mkdir()
            sample = source / "Samples~" / "SongReactiveLandscape"
            sample.mkdir()
            for file in ("ProceduralLandscape.cs", "MotionLandscape.shader"):
                (sample / file).write_text("// test original UPM sample input")
            output = prepare_project.build(source, source / "WebGLDemo")
            self.assertTrue((output / "Packages" / prepare_project.PACKAGE_NAME / "package.json").exists())
            self.assertTrue((output / "Assets" / "DanceFlowLandscape" / "MotionLandscape.shader").exists())
            lock = json.loads((output / "Assets" / "KandiBoomSource" / "asset-lock.json").read_text())
            self.assertEqual(lock["character"], "Kandi Boom")
            manifest = json.loads((output / "Packages" / "manifest.json").read_text())
            self.assertIn("com.unity.render-pipelines.universal", manifest["dependencies"])
            with self.assertRaises(FileExistsError):
                prepare_project.build(source, output)

    def test_pages_refuses_placeholder_and_accepts_actual_player_payload(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            build = root / "build"
            (build / "Build").mkdir(parents=True)
            (build / "index.html").write_text("<html>Unity WebGL</html>")
            receipt = root / "receipt.json"
            receipt.write_text(json.dumps({"schema":"danceflow.unity.kandi-asset-receipt.v1",
                                           "bone_count":24,"source_commit":"08e8628cb31fdc124358ae6f818c081f4cf0e96e"}))
            with self.assertRaisesRegex(ValueError, "WASM"):
                publish_pages.create_pages(build, root / "pages", receipt)
            for name in ("KandiBoom.wasm", "KandiBoom.data", "KandiBoom.loader.js"):
                (build / "Build" / name).write_bytes(b"local test payload")
            pages = publish_pages.create_pages(build, root / "pages", receipt)
            self.assertTrue((pages / "kandi" / "Build" / "KandiBoom.wasm").exists())
            self.assertIn("SYNTHETIC", (pages / "index.html").read_text().upper())
            self.assertTrue((pages / ".nojekyll").exists())

if __name__ == "__main__":
    unittest.main()
