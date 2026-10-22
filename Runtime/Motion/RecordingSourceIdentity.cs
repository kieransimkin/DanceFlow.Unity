using System;
using System.Collections.Generic;
using DanceFlow.Unity.Clock;

namespace DanceFlow.Unity.Motion
{
    /// <summary>
    /// A canonical recording is not required to contain an audio hash, so absence of a claim
    /// is NOT verification. Any explicitly asserted provenance hash must match the master.
    /// </summary>
    public static class RecordingSourceIdentity
    {
        public static bool HasMatchingProvenanceHash(RecordingDescriptor descriptor, string masterSha256)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (!RecordingBinaryCodec.IsSha256(masterSha256))
                throw new ArgumentException("Master hash must be lowercase SHA-256.", nameof(masterSha256));
            var root = StrictJson.Parse(descriptor.ProvenanceJson) as Dictionary<string, object>;
            if (root == null) throw new FormatException("Recording provenance must be a JSON object.");
            bool found = false;
            foreach (string key in new[] { "source_sha256", "audio_sha256", "master_sha256" })
            {
                if (!root.TryGetValue(key, out object value)) continue;
                if (!(value is string hash) || !RecordingBinaryCodec.IsSha256(hash))
                    throw new FormatException("Recording provenance contains an invalid " + key + " claim.");
                if (!string.Equals(hash, masterSha256, StringComparison.Ordinal))
                    throw new InvalidOperationException("Recording " + key + " claim conflicts with the selected master file.");
                found = true;
            }
            // StemLab evidence may nest the master reference under source_audio.
            foreach (string container in new[] { "source_audio", "master_audio" })
            {
                if (!root.TryGetValue(container, out object nested)) continue;
                if (!(nested is Dictionary<string, object> obj)) continue;
                if (!obj.TryGetValue("sha256", out object hashValue)) continue;
                if (!(hashValue is string hash) || !RecordingBinaryCodec.IsSha256(hash))
                    throw new FormatException("Recording provenance has an invalid " + container + ".sha256 claim.");
                if (!string.Equals(hash, masterSha256, StringComparison.Ordinal))
                    throw new InvalidOperationException("Recording " + container + ".sha256 conflicts with the selected master.");
                found = true;
            }
            return found;
        }
    }
}
