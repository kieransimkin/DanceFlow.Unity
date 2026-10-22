"""Functional evidence tests — no copyrighted audio, model downloads or GPU required."""
import copy
import gzip
import hashlib
import json
import tempfile
import unittest
import wave
from pathlib import Path
from prepare_song_assets import make_receipt, load_json, check_recording


class SongAssetTests(unittest.TestCase):
    def setUp(self):
        self.ctx = tempfile.TemporaryDirectory()
        self.addCleanup(self.ctx.cleanup)
        self.root = Path(self.ctx.name)
        self.master = self.root/'master.wav'
        with wave.open(str(self.master), 'wb') as audio:
            audio.setnchannels(1)
            audio.setsampwidth(2)
            audio.setframerate(8000)
            audio.writeframes(b'\0\0' * 8000)
        sha = hashlib.sha256(self.master.read_bytes()).hexdigest()
        self.timeline = self.root/'timeline.json'
        self.timeline.write_text(json.dumps({
            'schema':'stemlab.beat-timeline.v1', 'pips_per_beat':64,
            'source_audio':{'sha256':sha, 'sample_rate':8000,'num_frames':8000,'num_channels':1},
            'origin_beat':0, 'beats':[{'beat':0,'frame':1000}, {'beat':1,'frame':5000}],
        }))
        self.recording = self.root/'recording.json.gz'
        self.document = {
            'format':'dancerudiments.recording','schema_version':1,'pips_per_beat':64,
            'sample_dtype':'float64','sample_decimals':12,'name':'example',
            'dimensions':['x','y','z'],
            'samples':[[0.1,0.2,0.3],[0.4,0.5,0.6]],
            'clock':{'bpm':120.0,'bpm_status':'estimated','bpm_source':'fixture',
                     'beat_zero_seconds':0,'phase_status':'unverified','start_pip':-1},
            'provenance':{'source_sha256':sha}
        }
        with gzip.open(self.recording,'wt',encoding='utf-8') as f:
            json.dump(self.document,f)

    def test_real_source_hash_and_gzip_recording_receipt(self):
        receipt = make_receipt('arcadians',self.master,self.recording,self.timeline,
                               channels=['x','y','z','x'],approve_original_clock=True)
        self.assertEqual(receipt['master']['pcm_metadata']['num_frames'],8000)
        self.assertEqual(receipt['timing']['validated_map']['anchor_count'],2)
        self.assertEqual(receipt['recording']['association'],'provenance_hash_matches')
        self.assertEqual(receipt['recording']['details']['start_pip'],-1)
        self.assertNotIn(str(self.root),json.dumps(receipt))

    def test_unconfirmed_association_is_not_claimed_verified(self):
        self.document['provenance']={}
        with gzip.open(self.recording,'wt',encoding='utf-8') as f: json.dump(self.document,f)
        receipt=make_receipt('star_lights_glow_anthem',self.master,self.recording,bpm=122)
        self.assertEqual(receipt['recording']['association'],'unverified_requires_review')
        self.assertEqual(receipt['timing']['mode'],'explicit_fixed_bpm')

    def test_wrong_master_fails_closed(self):
        broken=json.loads(self.timeline.read_text());broken['source_audio']['sha256']='f'*64
        self.timeline.write_text(json.dumps(broken))
        with self.assertRaisesRegex(ValueError,'SHA-256'):
            make_receipt('arcadians',self.master,timeline=self.timeline)

    def test_provenance_mismatch_fails_closed(self):
        self.document['provenance']['source_sha256']='b'*64
        with gzip.open(self.recording,'wt',encoding='utf-8') as f: json.dump(self.document,f)
        with self.assertRaisesRegex(ValueError,'disagrees'):
            make_receipt('arcadians',self.master,self.recording,bpm=145)

    def test_cross_song_retime_requires_explicit_phase_acknowledgement(self):
        self.document['provenance']['source_sha256']='b'*64  # intentionally another song
        with gzip.open(self.recording,'wt',encoding='utf-8') as f: json.dump(self.document,f)
        pending = make_receipt('star_lights_glow_anthem', self.master, self.recording,
                               bpm=123, explicit_retime=(4, -32, 64))
        self.assertEqual(pending['recording']['association'], 'explicit_retime_requires_phase_approval')
        accepted = make_receipt('star_lights_glow_anthem', self.master, self.recording,
                                bpm=123, explicit_retime=(4, -32, 64), approve_unverified_phase=True)
        self.assertEqual(accepted['recording']['association'], 'explicit_retime_approved')
        self.assertEqual(accepted['recording']['explicit_retime']['recording_anchor_pip'], -32)

    def test_nested_original_source_sha_is_checked(self):
        self.document['provenance']={'source_audio':{'sha256':'c'*64}}
        with gzip.open(self.recording,'wt',encoding='utf-8') as f: json.dump(self.document,f)
        with self.assertRaisesRegex(ValueError,'disagrees'):
            make_receipt('arcadians',self.master,self.recording,bpm=145)

    def test_duplicate_json_rejected(self):
        self.timeline.write_text('{"schema":1,"schema":2}')
        with self.assertRaisesRegex(ValueError,'duplicate'):
            load_json(self.timeline,10000)

    def test_ragged_and_invalid_values(self):
        for mutate in (lambda d: d['samples'].append([1.0]),
                       lambda d: d['samples'][0].__setitem__(0,1.5),
                       lambda d: d['clock'].__setitem__('phase_status','confirmed')):
            doc=copy.deepcopy(self.document);mutate(doc)
            with self.assertRaises(ValueError):check_recording(doc)

    def test_no_guess_without_bpm_or_map(self):
        with self.assertRaisesRegex(ValueError,'explicit'):
            make_receipt('star_lights_glow_mystery_liquid_kick',self.master)


if __name__=='__main__':unittest.main()
