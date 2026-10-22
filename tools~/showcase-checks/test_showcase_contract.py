#!/usr/bin/env python3
"""Offline static checks for Phase 3B. Unity GPU compilation is a separate acceptance gate."""
import json
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
SAMPLE = ROOT / 'Samples~' / 'SongReactiveLandscape'


class ShowcaseContractTests(unittest.TestCase):
    def test_song_provenance_and_unverified_tempo(self):
        manifest = json.loads((SAMPLE / 'song-sources.json').read_text(encoding='utf-8'))
        self.assertEqual(manifest['schema'], 'danceflow.unity.showcase-songs.v1')
        tracks = {s['id']: s for s in manifest['songs']}
        self.assertEqual(len(tracks), 4)
        for id in ('star_lights_glow_mystery_liquid_kick', 'star_lights_glow_anthem'):
            self.assertIsNone(tracks[id]['bpm_hint'])
            self.assertFalse(tracks[id]['audio_included'])
            self.assertFalse(tracks[id]['recording_json_included'])
        self.assertEqual(tracks['arcadians']['matched_rudiment_candidates'][0]['pattern'], 'groove_b_played')
        self.assertEqual(tracks['the_truth_is_a_virus']['bpm_status'], 'detector_grid_fit_not_confirmed')

    def test_shader_contract_is_connected(self):
        bus = (ROOT / 'Runtime/Motion/MotionSignalBus.cs').read_text()
        motion = (ROOT / 'Runtime/GPU/DanceFlowMotionSignals.hlsl').read_text()
        shader = (SAMPLE / 'MotionLandscape.shader').read_text()
        for name in ('_DanceFlow_MotionA', '_DanceFlow_MotionB',
                     '_DanceFlow_MotionAvailable', '_DanceFlow_MotionIsRecording'):
            self.assertIn(name, bus)
            self.assertIn(name, motion)
        self.assertIn('DanceFlowClock.hlsl', shader)
        self.assertIn('DanceFlowMotionSignals.hlsl', shader)
        self.assertIn('MotionLandscape', shader)
        self.assertIn('_DanceFlow_BeatPulse', shader)

    def test_optional_gpu_recording_requires_explicit_enable(self):
        gpu = (ROOT / 'Runtime/GPU/MotionRecordingGpuBridge.cs').read_text()
        self.assertIn('1024 * 1024', gpu)
        self.assertIn('enableFullRecordingGpuBuffer', gpu)
        self.assertIn('Shader.SetGlobalFloat(ValidId, 0)', gpu)
        self.assertIn('recording.Clock.StartPip', gpu)
        self.assertIn('DF_MotionBufferSample', (ROOT / 'Runtime/GPU/DanceFlowMotionBuffer.hlsl').read_text())

    def test_only_scene_mesh_generation_not_song_assets(self):
        builder = (SAMPLE / 'ProceduralLandscape.cs').read_text()
        self.assertIn('BuildGrass(', builder)
        self.assertIn('AddTree(', builder)
        self.assertIn('BuildWarpTower()', builder)
        self.assertIn('BuildStars(', builder)
        self.assertFalse(list(SAMPLE.rglob('*.mp3')) + list(SAMPLE.rglob('*.wav')) + list(SAMPLE.rglob('*.fbx')))

    def test_unique_meta_guids(self):
        guids = {}
        for p in ROOT.rglob('*.meta'):
            text = p.read_text(errors='replace')
            match = re.search(r'^guid:\s*([0-9a-f]{32})$', text, flags=re.M)
            if not match:
                self.fail(f'Invalid Unity .meta GUID for {p.relative_to(ROOT)}')
            self.assertNotIn(match.group(1), guids, f'Duplicate GUID: {p} and {guids.get(match.group(1))}')
            guids[match.group(1)] = str(p)


if __name__ == '__main__':
    unittest.main()
