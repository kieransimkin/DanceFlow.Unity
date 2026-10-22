"""Verify the optional source-bound song workflow remains connected, with no media bundled."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SAMPLE = ROOT / 'Samples~' / 'SongReactiveLandscape'


class SongProfileIntegrationTests(unittest.TestCase):
    def test_source_identity_and_authoring_contract(self):
        wizard = (SAMPLE / 'Editor' / 'CreateSongProfileWizard.cs').read_text()
        profile = (SAMPLE / 'SongShowcaseProfile.cs').read_text()
        for name in ('SourceAudioIdentity.FromAudioBytes', 'StemLabBeatTimeline.Parse',
                     'AssetDatabase.GenerateUniqueAssetPath', 'ORIGINAL file bytes'):
            # Documentation and source text aren't substitutes for C# compilation.
            self.assertIn(name, wizard, 'Missing wizard integration: ' + name)
        for name in ('OriginalMasterSha256', 'ValidateForPlayback',
                     'originalSourceAssociationApproved', 'recordingPipsPerMasterBeat'):
            self.assertIn(name, profile)
        self.assertIn('UseVerifiedBeatMap(map)', profile)
        self.assertIn('UseRecording(motionRecording, ChannelSlots', profile)

    def test_scene_from_selected_profile(self):
        builder = (SAMPLE / 'Editor' / 'CreateSongShowcaseScene.cs').read_text()
        bootstrap = (SAMPLE / 'SongShowcaseBootstrap.cs').read_text()
        self.assertIn('CreateFromSelectedProfile', builder)
        self.assertIn('ConfigureProfile(profile)', builder)
        self.assertIn('songProfile.ApplyTo', bootstrap)
        self.assertIn('songClock.PlayScheduled()', bootstrap)
        self.assertIn('UseSyntheticPreview()', (ROOT/'Runtime/Motion/MotionSignalBus.cs').read_text())

    def test_gpu_windows_are_bounded_and_seek_safe(self):
        code = (ROOT / 'Runtime/Motion/RecordingGpuWindow.cs').read_text()
        bridge = (ROOT / 'Runtime/GPU/MotionRecordingGpuBridge.cs').read_text()
        self.assertIn('int stride = Math.Max(1, capacity - 1)', code)
        self.assertIn('Math.Floor(absoluteRow)', code)
        self.assertIn('signalBus.LastRecordingPip', bridge)
        self.assertIn('RecordingGpuWindows.AtPip', bridge)
        self.assertIn('window.StartRow', bridge)
        self.assertNotIn('float offset = _Time', bridge)

    def test_no_master_recordings_in_base_or_optional_source(self):
        for suffix in ('.wav', '.mp3', '.flac', '.ogg', '.fbx', '.mp4'):
            self.assertFalse(list(SAMPLE.rglob('*' + suffix)))
        self.assertTrue((SAMPLE / 'SongShowcaseProfile.cs.meta').exists())
        self.assertTrue((SAMPLE / 'Editor' / 'CreateSongProfileWizard.cs.meta').exists())

if __name__ == '__main__': unittest.main()
