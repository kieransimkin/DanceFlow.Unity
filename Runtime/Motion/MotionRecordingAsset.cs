using System;
using UnityEngine;

namespace DanceFlow.Unity.Motion
{
    /// <summary>
    /// Metadata-only Unity asset pointing to an independently loadable .bytes cache.
    /// The importer never embeds the full recording sample table in this ScriptableObject.
    /// </summary>
    public sealed class MotionRecordingAsset : ScriptableObject
    {
        [SerializeField, HideInInspector] private string recordingName;
        [SerializeField, HideInInspector] private string[] dimensions;
        [SerializeField, HideInInspector] private double bpm;
        [SerializeField, HideInInspector] private string bpmStatus;
        [SerializeField, HideInInspector] private string bpmSource;
        [SerializeField, HideInInspector] private double beatZeroSeconds;
        [SerializeField, HideInInspector] private string phaseStatus;
        [SerializeField, HideInInspector] private int startPip;
        [SerializeField, HideInInspector] private int sampleCount;
        [SerializeField, HideInInspector] private string provenanceJson;
        [SerializeField, HideInInspector] private string sourceJsonSha256;
        [SerializeField, HideInInspector] private string binarySha256;
        [SerializeField, HideInInspector] private TextAsset binaryData;

        public string RecordingName => recordingName;
        public int SampleCount => sampleCount;
        public int ChannelCount => dimensions == null ? 0 : dimensions.Length;
        public string SourceJsonSha256 => sourceJsonSha256;
        public string BinarySha256 => binarySha256;
        public string[] ChannelNames => dimensions == null ? Array.Empty<string>() : (string[])dimensions.Clone();
        public string BpmStatus => bpmStatus;
        public string PhaseStatus => phaseStatus;
        public string ProvenanceJson => provenanceJson;
        public double Bpm => bpm;
        public double BeatZeroSeconds => beatZeroSeconds;
        public int StartPip => startPip;

        public RecordingDescriptor Descriptor => new RecordingDescriptor(recordingName, dimensions,
            new RecordingClock(bpm, bpmStatus, bpmSource, beatZeroSeconds, phaseStatus, startPip),
            sampleCount, provenanceJson);

        /// <summary>Loads, verifies, and decodes only when explicitly requested.</summary>
        public MotionRecording OpenVerified()
        {
            if (binaryData == null)
                throw new InvalidOperationException("Imported recording's companion binary asset is missing.");
            return RecordingBinaryCodec.Decode(binaryData.bytes, Descriptor, binarySha256);
        }

#if UNITY_EDITOR
        /// <summary>Editor importer only. Never call with unvalidated JSON or unverified samples.</summary>
        public void SetImported(RecordingDescriptor value, TextAsset cache, string originalSha, string cacheSha)
        {
            if (value == null || cache == null || !RecordingBinaryCodec.IsSha256(originalSha) ||
                !RecordingBinaryCodec.IsSha256(cacheSha))
                throw new ArgumentException("Invalid imported recording metadata.");
            recordingName = value.Name;
            dimensions = value.Dimensions;
            bpm = value.Clock.Bpm;
            bpmStatus = value.Clock.BpmStatus;
            bpmSource = value.Clock.BpmSource;
            beatZeroSeconds = value.Clock.BeatZeroSeconds;
            phaseStatus = value.Clock.PhaseStatus;
            startPip = value.Clock.StartPip;
            sampleCount = value.SampleCount;
            provenanceJson = value.ProvenanceJson;
            sourceJsonSha256 = originalSha;
            binarySha256 = cacheSha;
            binaryData = cache;
        }
#endif
    }
}
