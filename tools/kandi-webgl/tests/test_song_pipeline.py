"""Regression gates for REAL audio, bounded analysis, native rudiment provenance and routing.

No internet/Unity required: live downloads/Unity builds are separate CI steps.
"""
from __future__ import annotations
from array import array
import hashlib
import json
import math
from pathlib import Path
import sys
import tempfile
import unittest

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
import fetch_songs
import publish_pages


class SongSourceTests(unittest.TestCase):
    def test_three_real_song_references_and_native_pattern_pin(self):
        lock = fetch_songs.load_lock()
        self.assertEqual({s['id'] for s in lock['songs']},
                         {'arcadians', 'the_truth_is_a_virus',
                          'star_lights_glow_mystery_liquid_kick'})
        self.assertEqual(lock['stemlab_commit'],
                         'e0a3d270608df7a47cd04c6e30bcd0b4f566b157')
        self.assertEqual(lock['songs'][0]['sha256'],
                         '5a3d17d8b5b27d62a6bb9fa1f654c403db0341da826d650cc282dfa5758ee6de')
        self.assertEqual(lock['songs'][1]['sha256'],
                         'b11ac7c48df227ac51d94c05a245d3138acf1a002f851c55d98fd7fb7f0de510')
        self.assertEqual(lock['songs'][2]['sha256'],
                         '48393fbb5ef5a1afc184ea388ce760612fe0c249c5c2f7caa57e563c6bd2e098')
        self.assertIsNone(lock['songs'][2]['bpm'])
        self.assertEqual(lock['songs'][2]['canonical_master_sha256'],
                         '9de00a34643a61eada7bec26da050ad6857d6ea31306ab9104b51cd214799284')
        self.assertIn('groove_b_played', lock['patterns'])
        self.assertIn('lfo_morph', lock['patterns'])
        self.assertEqual(lock['songs'][1]['tempo_status'], 'estimated_grid_fit')
        self.assertEqual(lock['songs'][0]['phase_status'], 'unverified')

    def test_source_path_traversal_and_mismatched_master_refused(self):
        with self.assertRaises(ValueError):
            fetch_songs.raw_url('stemlab', 'a'*40, '../secrets/key')
        source = fetch_songs.load_lock()['songs'][0]
        with self.assertRaisesRegex(ValueError, 'SHA-256'):
            fetch_songs.checked_audio(b'a' * source['bytes'], source)
        with self.assertRaises(ValueError):
            fetch_songs.checked_blob(b'incorrect', 'a'*40)

    def test_measured_signals_are_distinct_and_bounded(self):
        rate = fetch_songs.SAMPLE_RATE
        pcm = array('f')
        for i in range(rate * 2):
            # Two audible tones, then a transient to exercise several bands.
            t = i / rate
            v = 0.3*math.sin(2*math.pi*84*t) + 0.2*math.sin(2*math.pi*900*t)
            if 0.96 < t < 0.98:
                v += 0.3*math.sin(2*math.pi*2900*t)
            pcm.extend((v, v*0.8))
        analysis = fetch_songs.analyze_pcm(pcm)
        self.assertEqual(set(analysis), set(fetch_songs.BANDS))
        for name, values in analysis.items():
            self.assertEqual(len(values), 50, name)
            self.assertTrue(all(0 <= v <= 1 and math.isfinite(v) for v in values), name)
        self.assertGreater(max(analysis['sub']), 0)
        self.assertGreater(max(analysis['mid']), 0)
        self.assertGreater(max(analysis['onset']), 0)
        self.assertNotEqual(analysis['sub'], analysis['mid'])

    def test_native_pattern_pack_is_not_synthetic_or_a_song_match(self):
        pack = {'format': 'dancerudiments.compiled-pack', 'schema_version': 1,
                'pips_per_beat': 64, 'patterns': [{
                    'name':'groove_b_played', 'period_pips':64,
                    'source_sha256':'c'*64,
                    'samples':[[math.sin(n*math.pi/32),0.0,0.0] for n in range(64)]
                }]}
        raw = json.dumps(pack).encode()
        compiled = fetch_songs.serialize_patterns(raw, ['groove_b_played'])
        self.assertIn('Not proven matched', compiled['alignment'])
        self.assertEqual(compiled['patterns'][0]['period_pips'],64)
        self.assertEqual(compiled['patterns'][0]['primary_axis'],0)
        self.assertEqual(len(compiled['patterns'][0]['x']),64)
        self.assertEqual(fetch_songs.git_blob_sha1(raw), hashlib.sha1(
            b'blob ' + str(len(raw)).encode('ascii') + b'\x00' + raw).hexdigest())

    def test_consensus_downbeat_is_in_recorded_audio_time(self):
        data = json.dumps({'beats':[0.03,0.60,1.17,1.73], 'downbeats':[1.17]}).encode()
        beats, origin = fetch_songs.consensus_beat_map(data, duration=2.0)
        self.assertEqual(origin,2)
        self.assertEqual(beats,[0.03,0.6,1.17,1.73])
        with self.assertRaises(ValueError):
            fetch_songs.consensus_beat_map(data, duration=1.0)

    def test_full_build_with_test_masters_and_compiled_pack(self):
        """End-to-end without network; checks that generated files are source-associated."""
        songs = fetch_songs.load_lock()
        synth1 = b'fake-arcadians-audio-mp3'
        synth2 = b'fake-truth-audio-mp3'
        synth3 = b'fake-star-lights-audio-mp3'
        patterns = {'format':'dancerudiments.compiled-pack','schema_version':1,
                    'pips_per_beat':64,'patterns':[
                        {'name':name,'period_pips':64,'source_sha256':'a'*64,
                         'samples':[[math.sin(i*math.pi/32),0,0] for i in range(64)]}
                        for name in songs['patterns']]}
        patterns_raw = json.dumps(patterns).encode()
        beat_raw = json.dumps({'beats':[0.03,0.6,1.17,1.73], 'downbeats':[1.17]}).encode()
        songs['pattern_pack']['bytes'] = len(patterns_raw)
        songs['pattern_pack']['git_blob_sha1'] = fetch_songs.git_blob_sha1(patterns_raw)
        for song, raw in zip(songs['songs'],(synth1,synth2,synth3)):
            song['bytes'] = len(raw)
            song['sha256'] = hashlib.sha256(raw).hexdigest()
        songs['songs'][1]['beat_map_git_blob_sha1'] = fetch_songs.git_blob_sha1(beat_raw)
        with tempfile.TemporaryDirectory() as d:
            target = Path(d)
            lockpath = target/'song-lock.json'
            lockpath.write_text(json.dumps(songs))
            original_lock = fetch_songs.LOCK
            def getter(url, *, limit):
                if 'initial.json' in url: return patterns_raw
                if 'consensus.json' in url: return beat_raw
                if 'arcadians' in url: return synth1
                if 'the-truth-is-a-virus' in url: return synth2
                if 'star-lights-glow-mystery-liquid-kick' in url: return synth3
                raise AssertionError(url)
            def decoder(path):
                pcm = array('f')
                for i in range(2*fetch_songs.SAMPLE_RATE):
                    v=0.5*math.sin(2*math.pi*100*i/fetch_songs.SAMPLE_RATE)
                    pcm.extend((v,v))
                return pcm
            try:
                fetch_songs.LOCK=lockpath
                with self.assertRaisesRegex(ValueError, 'byte size'):
                    fetch_songs.load_lock()  # synthetic tiny fixtures fail the live size guard
                # Override only fixture values in the in-memory loader, preserving all
                # production validation for actual source locks.
                old_loader=fetch_songs.load_lock
                fetch_songs.load_lock=lambda path=None: songs
                try:
                    catalogue=fetch_songs.build(target/'Songs', getter=getter, decoder=decoder)
                finally:
                    fetch_songs.load_lock=old_loader
                self.assertEqual(len(catalogue['songs']),3)
                self.assertEqual(catalogue['songs'][2]['bpm'],0)
                self.assertEqual(catalogue['songs'][2]['preview_bpm'],120)
                self.assertEqual(catalogue['songs'][2]['phase_status'],'not_verified')
                self.assertEqual(catalogue['songs'][1]['origin_beat'],2)
                self.assertEqual(catalogue['songs'][1]['phase_status'],'model_consensus_estimated')
                for entry in catalogue['songs']:
                    self.assertTrue((target/'Songs'/entry['audio_file']).is_file())
                    envelope=json.loads((target/'Songs'/entry['signals_file']).read_text())
                    self.assertEqual(envelope['source_sha256'], entry['audio_sha256'])
                    self.assertTrue(envelope['frame_count']>0)
            finally:
                fetch_songs.LOCK=original_lock

    def test_pages_publish_requires_real_source_bound_song_bytes(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            build = root / 'build'
            (build / 'Build').mkdir(parents=True)
            (build / 'index.html').write_text('<html>real Unity WebGL example</html>')
            for ext in ('wasm', 'data', 'loader.js'):
                (build / 'Build' / ('KandiBoom.' + ext)).write_bytes(b'nonempty compiled test fixture')
            receipt = root / 'receipt.json'
            receipt.write_text(json.dumps({
                'schema':'danceflow.unity.kandi-asset-receipt.v1', 'bone_count':24,
                'source_commit':'0'*40
            }))
            with self.assertRaises(FileNotFoundError):
                publish_pages.create_pages(build, root/'not-yet-deployable', receipt, require_songs=True)
            songs_dir = build/'StreamingAssets'/'Songs'
            songs_dir.mkdir(parents=True)
            mp3 = b'actual verified test bytes'
            sha = hashlib.sha256(mp3).hexdigest()
            (songs_dir/'first.mp3').write_bytes(mp3)
            (songs_dir/'second.mp3').write_bytes(mp3)
            (songs_dir/'third.mp3').write_bytes(mp3)
            envelopes = {'source_sha256':sha,'frame_count':2}
            (songs_dir/'first.signals.json').write_text(json.dumps(envelopes))
            (songs_dir/'second.signals.json').write_text(json.dumps(envelopes))
            (songs_dir/'third.signals.json').write_text(json.dumps(envelopes))
            (songs_dir/'rudiments.json').write_text(json.dumps({
                'schema':'danceflow.unity.webgl-rudiments.v1',
                'patterns':[{'name':f'pattern_{i}'} for i in range(4)]
            }))
            catalogue = {'schema':'danceflow.unity.webgl-song-gallery.v1','songs':[
                {'id':identity, 'audio_file':f'{name}.mp3',
                 'signals_file':f'{name}.signals.json', 'audio_sha256':sha}
                for name,identity in (
                    ('first','arcadians'), ('second','the_truth_is_a_virus'),
                    ('third','star_lights_glow_mystery_liquid_kick'))
            ]}
            (songs_dir/'catalogue.json').write_text(json.dumps(catalogue))
            # Unity's Docker build can leave StreamingAssets owned by root.
            # Pages publishing must only READ the exported files, not overwrite
            # them or depend on a post-build copy performed as the runner user.
            source_mode = songs_dir.stat().st_mode & 0o777
            songs_dir.chmod(0o555)
            try:
                output = publish_pages.create_pages(build, root/'deployable', receipt, require_songs=True)
            finally:
                songs_dir.chmod(source_mode)
            self.assertTrue((output/'kandi'/'StreamingAssets'/'Songs'/'first.mp3').is_file())
            self.assertEqual(hashlib.sha256((songs_dir/'first.mp3').read_bytes()).hexdigest(), sha)
            (songs_dir/'second.mp3').write_bytes(b'altered!')
            with self.assertRaisesRegex(ValueError, 'SHA-256'):
                publish_pages.create_pages(build, root/'corrupt', receipt, require_songs=True)

    def test_ci_publishes_unity_export_without_writing_into_it(self):
        workflow = (HERE.parents[1]/'.github'/'workflows'/'unity-webgl-pages.yml').read_text()
        self.assertNotIn('cp -a WebGLDemo/Assets/StreamingAssets/Songs/.', workflow)
        self.assertNotIn('mkdir -p build/WebGL/KandiBoom/StreamingAssets/Songs', workflow)
        self.assertIn('python tools/kandi-webgl/publish_pages.py', workflow)
        self.assertIn('--require-songs', workflow)

    def test_scene_uses_one_clock_and_property_matrix(self):
        src = (HERE/'UnityProject'/'Assets'/'Editor'/'KandiBoomWebGLBuild.cs').read_text()
        song = (HERE/'UnityProject'/'Assets'/'Scripts'/'KandiBoomSongPlayer.cs').read_text()
        matrix = (HERE/'UnityProject'/'Assets'/'Scripts'/'KandiBoomMotionMatrix.cs').read_text()
        control = (HERE/'UnityProject'/'Assets'/'Scripts'/'KandiBoomControls.cs').read_text()
        workflow = (HERE.parents[1]/'.github'/'workflows'/'unity-webgl-pages.yml').read_text()
        self.assertIn('systems.AddComponent<KandiBoomSongPlayer>()',src)
        self.assertIn('systems.AddComponent<KandiBoomMotionMatrix>()',src)
        self.assertNotIn('systems.AddComponent<KandiBoomBeatDance>()',src)
        self.assertIn('UnityWebRequestMultimedia.GetAudioClip',song)
        self.assertIn('clock.PlayScheduled()',song)
        self.assertIn('songs.Signal',matrix)
        self.assertIn('songs.PatternValue',matrix)
        self.assertIn('CyclePattern',control)
        self.assertIn('fetch_songs.py',workflow)


if __name__ == '__main__': unittest.main()
