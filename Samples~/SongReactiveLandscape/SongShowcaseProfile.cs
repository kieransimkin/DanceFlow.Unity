using System;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.Motion;
using UnityEngine;

namespace DanceFlow.Unity.Samples
{
    /// <summary>
    /// Local, explicitly authored association of one exact source master, its beat map and
    /// its independent motion recording. No media or precomputed song data is in the UPM.
    /// The Editor wizard hashes the original AudioClip asset bytes when this is created.
    /// </summary>
    [CreateAssetMenu(fileName = "DanceFlowSongProfile", menuName = "DanceFlow/Verified Song Showcase Profile")]
    public sealed class SongShowcaseProfile : ScriptableObject
    {
        [SerializeField] private SongShowcasePreset song;
        [SerializeField] private string displayTitle;
        [SerializeField] private AudioClip audioClip;
        [SerializeField, HideInInspector] private string originalMasterSha256;
        [SerializeField, HideInInspector] private int originalSampleRate;
        [SerializeField, HideInInspector] private long originalSampleFrames;
        [SerializeField, HideInInspector] private int originalChannels;
        [SerializeField] private TextAsset correctedBeatTimeline;
        [SerializeField, Tooltip("Only used when there is no source-bound beat timeline.")]
        private float explicitBpm;
        [SerializeField] private string bpmEvidence;
        [SerializeField] private MotionRecordingAsset motionRecording;
        [SerializeField] private RecordingTimeDomain recordingTimeDomain = RecordingTimeDomain.OriginalSourceSeconds;
        [SerializeField, Tooltip("Author confirms the recording and master refer to the same original source clock; the recording JSON does not prove this by itself.")]
        private bool originalSourceAssociationApproved;
        [SerializeField] private double masterAnchorBeat;
        [SerializeField] private double recordingAnchorPip;
        [SerializeField] private double recordingPipsPerMasterBeat = 64;
        [SerializeField] private bool unverifiedPhaseRetimingApproved;
        [SerializeField] private string windChannel = "x";
        [SerializeField] private string gustChannel = "y";
        [SerializeField] private string paletteChannel = "z";
        [SerializeField] private string shimmerChannel = "x";

        public SongShowcasePreset Song => song;
        public string DisplayTitle => displayTitle;
        public AudioClip Audio => audioClip;
        public MotionRecordingAsset Recording => motionRecording;
        public string OriginalMasterSha256 => originalMasterSha256;
        public float ExplicitBpm => explicitBpm;
        public string BpmEvidence => bpmEvidence;
        public bool HasCorrectedMap => correctedBeatTimeline != null;
        public bool UserApprovedSourceAssociation => originalSourceAssociationApproved;

        public string[] ChannelSlots => new[] { windChannel, gustChannel, paletteChannel, shimmerChannel };

        /// <summary>Validate all saved claims before altering transport or song motion.</summary>
        public VariableBeatMap ValidateForPlayback()
        {
            if (audioClip == null) throw new InvalidOperationException("Song profile has no imported AudioClip.");
            if (!RecordingBinaryCodec.IsSha256(originalMasterSha256))
                throw new InvalidOperationException("Profile lacks the SHA-256 receipt for original audio bytes. Rebuild it using the Editor wizard.");
            if (originalSampleRate != audioClip.frequency || originalChannels != audioClip.channels ||
                originalSampleFrames != audioClip.samples)
                throw new InvalidOperationException("Imported AudioClip metadata changed since profile verification. Rebuild the profile from its original source master.");
            VariableBeatMap beatMap = null;
            if (correctedBeatTimeline != null)
            {
                // Parse enforces exact master SHA, source metadata, every anchor and derived fields.
                var identity = new SourceAudioIdentity(originalMasterSha256, originalSampleRate,
                    originalSampleFrames, originalChannels);
                beatMap = StemLabBeatTimeline.Parse(correctedBeatTimeline.text, identity);
            }
            else if (float.IsNaN(explicitBpm) || float.IsInfinity(explicitBpm) || explicitBpm <= 0 ||
                     string.IsNullOrWhiteSpace(bpmEvidence))
                throw new InvalidOperationException("A documented, explicit BPM is required without a corrected StemLab timeline.");

            if (motionRecording != null)
            {
                // Inert provenance is not authoritative unless it asserts an exact hash;
                // an asserted mismatch must fail rather than be overridden by a checkbox.
                if (recordingTimeDomain == RecordingTimeDomain.OriginalSourceSeconds)
                    RecordingSourceIdentity.HasMatchingProvenanceHash(
                        motionRecording.Descriptor, originalMasterSha256);
                // An explicit beat-retime is allowed to target a DIFFERENT song,
                // but must declare actual correspondence and phase approval.
                // These are two DIFFERENT source domains: a match must be an authored decision.
                if (recordingTimeDomain == RecordingTimeDomain.OriginalSourceSeconds &&
                    !originalSourceAssociationApproved)
                    throw new InvalidOperationException("Explicit approval of original recording/master source association is required.");
                if (recordingTimeDomain == RecordingTimeDomain.ExplicitMusicalRetiming)
                    RecordingTimeMapping.ExplicitBeatRetiming(masterAnchorBeat, recordingAnchorPip,
                        recordingPipsPerMasterBeat, unverifiedPhaseRetimingApproved)
                        .RecordingPip(new BeatState(0, 0, 0, 120, 4, 0, 0, 0, false), motionRecording.Descriptor.Clock);
                bool any = false;
                foreach (string channel in ChannelSlots)
                    if (!string.IsNullOrEmpty(channel) && motionRecording.Descriptor.TryChannelIndex(channel, out _))
                        any = true;
                if (!any) throw new InvalidOperationException("No selected motion channels are present in the recording.");
            }
            return beatMap;
        }

        public void ApplyTo(DanceFlowClockBehaviour clock, MotionSignalBus signalBus,
                            AudioSource player)
        {
            if (clock == null || clock.Transport == null || signalBus == null || player == null)
                throw new InvalidOperationException("A live musical clock, motion bus and AudioSource are required.");
            var map = ValidateForPlayback(); // validate FIRST, before mutating the scene
            if (clock.Transport.IsRunning)
                throw new InvalidOperationException("Stop the song before changing its source profile.");
            if (map == null) clock.UseFixedTempo(explicitBpm);
            else clock.UseVerifiedBeatMap(map);
            player.clip = audioClip;
            player.loop = false;
            if (motionRecording != null)
                signalBus.UseRecording(motionRecording, ChannelSlots, recordingTimeDomain,
                    originalSourceAssociationApproved, masterAnchorBeat, recordingAnchorPip,
                    recordingPipsPerMasterBeat, unverifiedPhaseRetimingApproved);
            else signalBus.UseSyntheticPreview();
        }

#if UNITY_EDITOR
        /// <summary>Editor-only creation: input identity is computed from real source-file bytes.</summary>
        public void SetAuthoring(SongShowcasePreset selectedSong, string title, AudioClip clip,
            SourceAudioIdentity identity, TextAsset map, float bpm, string evidence,
            MotionRecordingAsset recording, string[] channels, RecordingTimeDomain timeDomain,
            bool sameSourceAcknowledged, double masterBeat, double recordingPip,
            double pipsPerBeat, bool approveUnverifiedPhase)
        {
            if (channels == null || channels.Length != 4) throw new ArgumentException("Exactly four explicit channel names required.");
            song = selectedSong; displayTitle = title; audioClip = clip;
            originalMasterSha256 = identity.Sha256;
            originalSampleRate = identity.SampleRate;
            originalSampleFrames = identity.FrameCount;
            originalChannels = identity.Channels;
            correctedBeatTimeline = map; explicitBpm = bpm; bpmEvidence = evidence;
            motionRecording = recording; recordingTimeDomain = timeDomain;
            originalSourceAssociationApproved = sameSourceAcknowledged;
            masterAnchorBeat = masterBeat; recordingAnchorPip = recordingPip;
            recordingPipsPerMasterBeat = pipsPerBeat;
            unverifiedPhaseRetimingApproved = approveUnverifiedPhase;
            windChannel = channels[0]; gustChannel = channels[1];
            paletteChannel = channels[2]; shimmerChannel = channels[3];
            ValidateForPlayback();
        }
#endif
    }
}
