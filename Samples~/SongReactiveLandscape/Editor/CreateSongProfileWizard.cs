using System;
using System.IO;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.Motion;
using UnityEditor;
using UnityEngine;

namespace DanceFlow.Unity.Samples.Editor
{
    /// <summary>
    /// Offline, opt-in importer. Never downloads or republishes music; hashes the exact
    /// original file and checks a supplied StemLab map against that identity.
    /// </summary>
    public sealed class CreateSongProfileWizard : EditorWindow
    {
        private SongShowcasePreset _song = SongShowcasePreset.StarLightsGlowMysteryLiquidKick;
        private string _title = "Star Lights Glow";
        private AudioClip _audio;
        private TextAsset _timeline;
        private MotionRecordingAsset _recording;
        private float _bpm;
        private string _evidence = "";
        private RecordingTimeDomain _domain = RecordingTimeDomain.OriginalSourceSeconds;
        private bool _acknowledged;
        private bool _approvedRetime;
        private double _masterBeat;
        private double _recordingPip;
        private double _pipsPerBeat = 64;
        private string _wind = "x", _gust = "y", _palette = "z", _shimmer = "x";

        [MenuItem("Tools/DanceFlow/Samples/Create Source-Verified Song Profile...")]
        private static void ShowWindow() => GetWindow<CreateSongProfileWizard>("DanceFlow Song Assets");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Song-specific assets (nothing downloaded)", EditorStyles.boldLabel);
            _song = (SongShowcasePreset)EditorGUILayout.EnumPopup("Song", _song);
            _title = EditorGUILayout.TextField("Title", _title);
            _audio = (AudioClip)EditorGUILayout.ObjectField("Exact original master", _audio, typeof(AudioClip), false);
            _timeline = (TextAsset)EditorGUILayout.ObjectField("Corrected StemLab map", _timeline, typeof(TextAsset), false);
            _bpm = EditorGUILayout.FloatField("Explicit BPM (without map)", _bpm);
            _evidence = EditorGUILayout.TextField("BPM source / evidence", _evidence);
            _recording = (MotionRecordingAsset)EditorGUILayout.ObjectField("Imported recording", _recording, typeof(MotionRecordingAsset), false);
            _domain = (RecordingTimeDomain)EditorGUILayout.EnumPopup("Recording mapping", _domain);
            if (_domain == RecordingTimeDomain.OriginalSourceSeconds)
                _acknowledged = EditorGUILayout.Toggle("Approve same-source timing", _acknowledged);
            else
            {
                _masterBeat = EditorGUILayout.DoubleField("Master beat anchor", _masterBeat);
                _recordingPip = EditorGUILayout.DoubleField("Recording pip anchor", _recordingPip);
                _pipsPerBeat = EditorGUILayout.DoubleField("Recording pips/master beat", _pipsPerBeat);
                _approvedRetime = EditorGUILayout.Toggle("Approve unverified phase", _approvedRetime);
            }
            EditorGUILayout.LabelField("Named channels (no automatic semantic guesses)", EditorStyles.boldLabel);
            _wind = EditorGUILayout.TextField("Wind", _wind);
            _gust = EditorGUILayout.TextField("Gust", _gust);
            _palette = EditorGUILayout.TextField("Palette", _palette);
            _shimmer = EditorGUILayout.TextField("Shimmer", _shimmer);
            EditorGUILayout.HelpBox("The audio must already be imported under Assets/. The source SHA-256 is calculated from its ORIGINAL file bytes. No song alignment is inferred from the preview clock. A compressed AudioClip may be decoder-offset from its StemLab master; use the same original WAV when precise source timing matters.", MessageType.Info);
            if (GUILayout.Button("Validate and create local song profile"))
            {
                try { Create(); }
                catch (Exception ex) when (ex is IOException || ex is InvalidOperationException ||
                                           ex is ArgumentException || ex is FormatException || ex is OverflowException)
                { EditorUtility.DisplayDialog("Song profile rejected", ex.Message, "OK"); }
            }
        }

        private void Create()
        {
            if (_audio == null) throw new InvalidOperationException("Choose the actual audio master first.");
            string path = AssetDatabase.GetAssetPath(_audio);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("Import the song master into this project's Assets/ directory first.");
            string projectRoot = Path.GetFullPath(Directory.GetCurrentDirectory());
            string masterPath = Path.GetFullPath(Path.Combine(projectRoot, path));
            string assetsRoot = Path.GetFullPath(Path.Combine(projectRoot, "Assets")) + Path.DirectorySeparatorChar;
            if (!masterPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Selected master escapes Assets/.");
            if (!File.Exists(masterPath)) throw new FileNotFoundException("Original audio bytes unavailable.", masterPath);
            SourceAudioIdentity identity;
            using (var bytes = File.OpenRead(masterPath))
                identity = SourceAudioIdentity.FromAudioBytes(bytes, _audio.frequency, _audio.samples, _audio.channels);
            if (_timeline != null) StemLabBeatTimeline.Parse(_timeline.text, identity);
            if (_timeline == null && (_bpm <= 0 || string.IsNullOrWhiteSpace(_evidence)))
                throw new InvalidOperationException("Set a documented BPM/source or select a source-verified beat map.");
            if (_recording != null)
            {
                // Verify original importer metadata/binary and reject contradictory source evidence.
                var verified = _recording.OpenVerified();
                if (_domain == RecordingTimeDomain.OriginalSourceSeconds)
                    RecordingSourceIdentity.HasMatchingProvenanceHash(verified.Descriptor, identity.Sha256);
            }
            if (_recording != null && _domain == RecordingTimeDomain.OriginalSourceSeconds && !_acknowledged)
                throw new InvalidOperationException("Check 'Approve same-source timing' after confirming the source recording and master actually share timestamps.");
            var asset = CreateInstance<SongShowcaseProfile>();
            bool saved = false;
            try
            {
                asset.SetAuthoring(_song, _title, _audio, identity, _timeline, _bpm, _evidence,
                    _recording, new[] { _wind, _gust, _palette, _shimmer }, _domain,
                    _acknowledged, _masterBeat, _recordingPip, _pipsPerBeat, _approvedRetime);
                const string folder = "Assets/DanceFlowSongShowcase";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "DanceFlowSongShowcase");
                string target = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + _song + "_Verified.asset");
                AssetDatabase.CreateAsset(asset, target);
                saved = true;
                AssetDatabase.SaveAssets();
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                Debug.Log("Created source-hash-bound DanceFlow song profile " + target + " (master SHA-256 " + identity.Sha256 + "). Recording/source approval remains separately documented.");
            }
            finally { if (!saved) DestroyImmediate(asset); }
        }
    }
}
