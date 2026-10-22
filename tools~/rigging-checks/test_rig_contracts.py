"""Independent canonical profile/hash and motion shape tests. No Unity/.NET needed."""
import hashlib
import json
import math
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / 'Samples~' / 'RiggedCharacterDemos' / 'Data'

class RigStageFourContractTests(unittest.TestCase):
    def test_fixture_generator_parity(self):
        result=subprocess.run([sys.executable,str(ROOT/'tools~'/'rigging-checks'/'generate_fixtures.py'),'--verify'],capture_output=True,text=True)
        self.assertEqual(result.returncode,0,result.stderr)

    def test_profile_fingerprint_and_channel_contract(self):
        for model,count in [('humanoid',20),('quadruped',15)]:
            obj=json.loads((DATA/f'unity_{model}_demo.rig-motion.json').read_bytes())
            profile=obj['profile']
            canonical=json.dumps(profile,sort_keys=True,ensure_ascii=True,separators=(',',':')).encode('ascii')
            self.assertEqual(hashlib.sha256(canonical).hexdigest(),obj['profile_sha256'])
            channels=profile['channels']
            self.assertEqual([x['number'] for x in channels],list(range(1,count+1)))
            self.assertEqual(obj['recording']['dimensions'],[x['name'] for x in channels])
            self.assertTrue(obj['type_uri'].startswith('syn://type/danceflow/motion/rig/custom/'))

    def test_all_quaternions_are_normalized_and_finite(self):
        for model in ('humanoid','quadruped'):
            obj=json.loads((DATA/f'unity_{model}_demo.rig-motion.json').read_bytes())
            profile=obj['profile'];rows=obj['recording']['samples'];channels=profile['channels']
            groups={}
            for i,ch in enumerate(channels):
                if ch['interpolation']=='quaternion':groups.setdefault(ch['bone'],{})[ch['array_index']]=i
            self.assertTrue(groups)
            for g in groups.values():self.assertEqual(set(g),set(range(4)))
            for row in rows:
                self.assertTrue(all(math.isfinite(x) and -1<=x<=1 for x in row))
                for g in groups.values():
                    v=[row[g[i]] for i in range(4)]
                    self.assertAlmostEqual(sum(x*x for x in v),1,delta=2e-5)

    def test_opt_in_sample_contains_no_unlicensed_media(self):
        sample=ROOT/'Samples~'/'RiggedCharacterDemos'
        for ext in ('*.fbx','*.wav','*.mp3','*.flac','*.png','*.glb','*.bvh'):
            self.assertEqual(list(sample.rglob(ext)),[])
        kandi=json.loads((sample/'kandi-boom-target.json').read_text())
        self.assertEqual(len(kandi['expected_bones']),24)
        self.assertTrue(kandi['motion_slots']['boxbot']['timing_kind']=='time-only')
        self.assertFalse(kandi['assets_bundled'])

    def test_unity_source_has_verified_bindings_and_job(self):
        source=(ROOT/'Runtime'/'Rigging'/'RigMotionPlayerBehaviour.cs').read_text()
        job=(ROOT/'Runtime'/'Rigging'/'RigMotionAnimationJob.cs').read_text()
        for needle in ['RigRetargetValidator.Build','mappingReviewedAndApproved','MotionRecordingCache.Acquire',
                       'AnimationScriptPlayable.Create','RecordingTimeMapping.ExplicitBeatRetiming','RestoreRestPose']:
            self.assertIn(needle,source)
        self.assertIn('IAnimationJob',job)
        self.assertIn('TransformStreamHandle',job)

if __name__=='__main__':unittest.main()
