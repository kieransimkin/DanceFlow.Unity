using System;
using System.Text;
using UnityEngine;

namespace DanceFlow.Unity.Rigging
{
    /// <summary>Lightweight metadata referencing a separately imported, SHA-pinned profile JSON.</summary>
    public sealed class RigProfileAsset : ScriptableObject
    {
        [SerializeField, HideInInspector] private string typeUri;
        [SerializeField, HideInInspector] private string model;
        [SerializeField, HideInInspector] private string fingerprint;
        [SerializeField, HideInInspector] private int channelCount;
        [SerializeField, HideInInspector] private TextAsset canonicalJson;
        public string TypeUri => typeUri;
        public string Model => model;
        public string Fingerprint => fingerprint;
        public int ChannelCount => channelCount;
        public RigProfile OpenVerified()
        {
            if (canonicalJson == null) throw new InvalidOperationException("Rig profile's JSON asset is missing.");
            var profile = RigProfileJson.ParseUtf8(canonicalJson.bytes, fingerprint);
            if (profile.TypeUri != typeUri || profile.Model != model || profile.ChannelCount != channelCount)
                throw new FormatException("Imported rig metadata does not match its verified source profile.");
            return profile;
        }
#if UNITY_EDITOR
        public void SetImported(RigProfile profile, TextAsset raw)
        {
            if (profile == null || raw == null) throw new ArgumentNullException(nameof(profile));
            var verified = RigProfileJson.ParseUtf8(raw.bytes, profile.Fingerprint);
            typeUri=verified.TypeUri; model=verified.Model; fingerprint=verified.Fingerprint;
            channelCount=verified.ChannelCount; canonicalJson=raw;
        }
#endif
    }
}
