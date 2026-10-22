using System;
using DanceFlow.Unity.Motion;
using UnityEngine;

namespace DanceFlow.Unity.Rigging
{
    /// <summary>Explicit linked profile/recording pair. No heavy data decoded until use.</summary>
    public sealed class RigMotionAsset : ScriptableObject
    {
        [SerializeField, HideInInspector] private RigProfileAsset rigProfile;
        [SerializeField, HideInInspector] private MotionRecordingAsset motionRecording;
        [SerializeField, HideInInspector] private string typeUri;
        [SerializeField, HideInInspector] private string profileFingerprint;
        public RigProfileAsset Profile => rigProfile;
        public MotionRecordingAsset Recording => motionRecording;
        public string TypeUri => typeUri;
        public string Fingerprint => profileFingerprint;
        public void ValidateLink()
        {
            if (rigProfile == null || motionRecording == null)
                throw new InvalidOperationException("Rig motion asset has missing profile or recording.");
            var profile = rigProfile.OpenVerified();
            if (profile.TypeUri != typeUri || profile.Fingerprint != profileFingerprint)
                throw new InvalidOperationException("Rig motion asset's profile identity changed.");
            profile.RequireMatchingRecording(motionRecording.Descriptor);
        }
#if UNITY_EDITOR
        public void SetImported(RigProfileAsset profile, MotionRecordingAsset recording)
        {
            if (profile == null || recording == null) throw new ArgumentNullException(nameof(profile));
            var checkedProfile = profile.OpenVerified();
            checkedProfile.RequireMatchingRecording(recording.Descriptor);
            rigProfile=profile; motionRecording=recording;
            typeUri=checkedProfile.TypeUri; profileFingerprint=checkedProfile.Fingerprint;
        }
#endif
    }
}
