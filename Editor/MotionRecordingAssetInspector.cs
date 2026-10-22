using System;
using DanceFlow.Unity.Motion;
using UnityEditor;
using UnityEngine;

namespace DanceFlow.Unity.Editor
{
    [CustomEditor(typeof(MotionRecordingAsset))]
    public sealed class MotionRecordingAssetInspector : UnityEditor.Editor
    {
        private bool _channels;
        private bool _provenance;

        public override void OnInspectorGUI()
        {
            var asset = (MotionRecordingAsset)target;
            EditorGUILayout.LabelField("Recording", asset.RecordingName);
            EditorGUILayout.LabelField("Samples", asset.SampleCount.ToString());
            EditorGUILayout.LabelField("Channels", asset.ChannelCount.ToString());
            EditorGUILayout.LabelField("Start pip", asset.StartPip.ToString());
            EditorGUILayout.LabelField("BPM", asset.Bpm.ToString("R"));
            EditorGUILayout.LabelField("BPM provenance", asset.BpmStatus);
            EditorGUILayout.LabelField("Phase provenance", asset.PhaseStatus);
            EditorGUILayout.LabelField("Source beat-zero seconds", asset.BeatZeroSeconds.ToString("R"));
            EditorGUILayout.SelectableLabel(asset.SourceJsonSha256, EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.LabelField("Original JSON SHA-256");
            _channels = EditorGUILayout.Foldout(_channels, "Named channels", true);
            if (_channels)
                foreach (string name in asset.ChannelNames) EditorGUILayout.LabelField("  " + name);
            _provenance = EditorGUILayout.Foldout(_provenance, "Inert source provenance JSON", true);
            if (_provenance) EditorGUILayout.TextArea(asset.ProvenanceJson);
            if (GUILayout.Button("Verify binary cache and exact sample extent"))
            {
                try
                {
                    var recording = asset.OpenVerified();
                    Debug.Log("DanceFlow recording cache verified: " + recording.Descriptor.Name +
                        " / " + recording.SampleCount + " sample rows / " + recording.DimensionCount + " channels.");
                }
                catch (Exception ex)
                {
                    Debug.LogError("DanceFlow recording integrity check failed: " + ex.Message, asset);
                }
            }
            EditorGUILayout.HelpBox("Capture is finite. The source recording clock is not automatically verified against the scene's audio. The Transform driver requires explicit source association or beat retiming.", MessageType.Info);
        }
    }
}
