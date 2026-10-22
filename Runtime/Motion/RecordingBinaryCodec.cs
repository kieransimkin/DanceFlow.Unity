using System;
using System.IO;
using System.Security.Cryptography;

namespace DanceFlow.Unity.Motion
{
    /// <summary>
    /// Internal binary cache, not a replacement for the canonical source recording JSON.
    /// DFR1: bytes 'D','F','R','1'; int32 version/dimensions/rows/start_pip;
    /// binary64 BPM/origin; 32-byte SHA-256 of canonical descriptor metadata; followed by
    /// interleaved binary64 sample rows (little-endian).
    /// The accompanying metadata and independent SHA-256 bind this payload to its source.
    /// </summary>
    public static class RecordingBinaryCodec
    {
        public const int Version = 1;
        public const int HeaderBytes = 68;

        public static byte[] Encode(MotionRecording recording)
        {
            if (recording == null) throw new ArgumentNullException(nameof(recording));
            int count = recording.NativeValues.Length;
            int length = checked(HeaderBytes + checked(count * sizeof(double)));
            using (var output = new MemoryStream(length))
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(new byte[] { (byte)'D', (byte)'F', (byte)'R', (byte)'1' });
                writer.Write(Version);
                writer.Write(recording.DimensionCount);
                writer.Write(recording.SampleCount);
                writer.Write(recording.Clock.StartPip);
                writer.Write(recording.Clock.Bpm);
                writer.Write(recording.Clock.BeatZeroSeconds);
                writer.Write(DescriptorHash(recording.Descriptor));
                foreach (double value in recording.NativeValues) writer.Write(value);
                writer.Flush();
                return output.ToArray();
            }
        }

        /// <summary>Validate both SHA-256 and descriptor before exposing a cache to a sampler.</summary>
        public static MotionRecording Decode(byte[] bytes, RecordingDescriptor descriptor, string expectedSha256)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (!IsSha256(expectedSha256) || !string.Equals(Sha256(bytes), expectedSha256, StringComparison.Ordinal))
                throw new FormatException("Recording binary SHA-256 mismatch.");
            long expected = HeaderBytes + (long)descriptor.SampleCount * descriptor.DimensionCount * sizeof(double);
            if (bytes.Length != expected) throw new FormatException("Truncated or surplus recording sample payload.");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadByte() != 'D' || reader.ReadByte() != 'F' || reader.ReadByte() != 'R' || reader.ReadByte() != '1')
                    throw new FormatException("Invalid DFR1 cache magic.");
                if (reader.ReadInt32() != Version || reader.ReadInt32() != descriptor.DimensionCount ||
                    reader.ReadInt32() != descriptor.SampleCount || reader.ReadInt32() != descriptor.Clock.StartPip ||
                    reader.ReadDouble() != descriptor.Clock.Bpm || reader.ReadDouble() != descriptor.Clock.BeatZeroSeconds)
                    throw new FormatException("Binary header conflicts with its metadata descriptor.");
                byte[] actualDescriptorHash = reader.ReadBytes(32);
                byte[] expectedDescriptorHash = DescriptorHash(descriptor);
                if (actualDescriptorHash.Length != 32)
                    throw new FormatException("Missing binary descriptor hash.");
                for (int i = 0; i < 32; i++)
                    if (actualDescriptorHash[i] != expectedDescriptorHash[i])
                        throw new FormatException("Binary asset was paired with incorrect recording metadata.");
                var samples = new double[checked(descriptor.SampleCount * descriptor.DimensionCount)];
                for (int i = 0; i < samples.Length; i++)
                {
                    double value = reader.ReadDouble();
                    if (double.IsNaN(value) || double.IsInfinity(value) || value < -1 || value > 1)
                        throw new FormatException("Invalid float64 value in recording cache.");
                    samples[i] = value;
                }
                if (stream.Position != expected) throw new FormatException("Recording binary contains trailing data.");
                return new MotionRecording(descriptor, samples);
            }
        }

        private static byte[] DescriptorHash(RecordingDescriptor descriptor)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(descriptor.Name);
                writer.Write(descriptor.SampleCount);
                writer.Write(descriptor.DimensionCount);
                for (int i = 0; i < descriptor.DimensionCount; i++) writer.Write(descriptor.DimensionAt(i));
                writer.Write(descriptor.Clock.Bpm);
                writer.Write(descriptor.Clock.BpmStatus);
                writer.Write(descriptor.Clock.BpmSource);
                writer.Write(descriptor.Clock.BeatZeroSeconds);
                writer.Write(descriptor.Clock.PhaseStatus);
                writer.Write(descriptor.Clock.StartPip);
                writer.Write(descriptor.ProvenanceJson);
                writer.Flush();
                using (SHA256 sha = SHA256.Create()) return sha.ComputeHash(stream.ToArray());
            }
        }

        public static string Sha256(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static bool IsSha256(string hash)
        {
            if (hash == null || hash.Length != 64) return false;
            foreach (char c in hash)
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            return true;
        }
    }
}
