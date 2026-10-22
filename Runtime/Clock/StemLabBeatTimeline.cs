using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;

namespace DanceFlow.Unity.Clock
{
    /// <summary>Identity of the exact source audio bytes, not a URL or Unity clip title.</summary>
    public readonly struct SourceAudioIdentity
    {
        public readonly string Sha256;
        public readonly int SampleRate;
        public readonly long FrameCount;
        public readonly int Channels;

        public SourceAudioIdentity(string sha256, int sampleRate, long frameCount, int channels)
        {
            if (!StemLabBeatTimeline.IsSha256(sha256)) throw new ArgumentException("Expected lowercase SHA-256.", nameof(sha256));
            if (sampleRate < 1 || sampleRate > 768000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (frameCount < 2 || frameCount > 10000000000000L) throw new ArgumentOutOfRangeException(nameof(frameCount));
            if (channels < 1 || channels > 512) throw new ArgumentOutOfRangeException(nameof(channels));
            Sha256 = sha256; SampleRate = sampleRate; FrameCount = frameCount; Channels = channels;
        }

        /// <summary>Caller supplies authoritative audio metadata; bytes are hashed without changing stream position.</summary>
        public static SourceAudioIdentity FromAudioBytes(Stream audioBytes, int sampleRate, long frames, int channels)
        {
            if (audioBytes == null || !audioBytes.CanRead || !audioBytes.CanSeek)
                throw new ArgumentException("Audio stream must be readable and seekable.", nameof(audioBytes));
            long previous = audioBytes.Position;
            try
            {
                audioBytes.Position = 0;
                using (SHA256 hash = SHA256.Create())
                    return new SourceAudioIdentity(BitConverter.ToString(hash.ComputeHash(audioBytes))
                        .Replace("-", "").ToLowerInvariant(), sampleRate, frames, channels);
            }
            finally { audioBytes.Position = previous; }
        }
    }

    /// <summary>Validated version-one StemLab export. The source hash must be independently checked by the caller.</summary>
    public static class StemLabBeatTimeline
    {
        public const string Schema = "stemlab.beat-timeline.v1";
        public const int MaxBytes = 32 * 1024 * 1024;
        public const int MaxAnchors = 25000;

        internal static bool IsSha256(string hash)
        {
            if (hash == null || hash.Length != 64) return false;
            foreach (char c in hash)
                if ((c < '0' || c > '9') && (c < 'a' || c > 'f')) return false;
            return true;
        }

        private static Dictionary<string, object> Obj(object value, string name)
        {
            if (value is Dictionary<string, object> map) return map;
            throw new FormatException(name + " must be a JSON object.");
        }
        private static List<object> Array(object value, string name)
        {
            if (value is List<object> list) return list;
            throw new FormatException(name + " must be a JSON array.");
        }
        private static object Required(Dictionary<string, object> map, string name)
        {
            if (!map.TryGetValue(name, out object value) || value == null)
                throw new FormatException("Missing mandatory field: " + name);
            return value;
        }
        private static long Int(object value, string name, long min, long max)
        {
            if (!(value is long number) || number < min || number > max)
                throw new FormatException(name + " must be an integer in allowed bounds.");
            return number;
        }
        private static string Str(object value, string name)
        {
            if (value is string s) return s;
            throw new FormatException(name + " must be a string.");
        }
        private static double Number(object value, string name)
        {
            if (value is long integer) return integer;
            if (value is double d) return MusicalMath.Finite(d, name);
            throw new FormatException(name + " must be a finite number.");
        }
        private static void Equal(Dictionary<string, object> map, string key, long expected)
        {
            if (Int(Required(map, key), key, long.MinValue, long.MaxValue) != expected)
                throw new FormatException(key + " disagrees with source beat anchors.");
        }
        private static void Near(object value, double expected, string key)
        {
            if (Math.Abs(Number(value, key) - expected) > 1e-7)
                throw new FormatException(key + " disagrees with native source-frame timing.");
        }
        private static BigInteger[] Rational(string value, string name)
        {
            if (value == null || value.Length == 0 || value.Length > 162)
                throw new FormatException(name + " must be a bounded exact rational.");
            string[] parts = value.Split('/');
            if (parts.Length > 2) throw new FormatException(name + " is not a rational.");
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                int j = i == 0 && part.StartsWith("-", StringComparison.Ordinal) ? 1 : 0;
                if (part.Length - j < 1 || part.Length - j > 80)
                    throw new FormatException("Overlong rational: " + name);
                for (; j < part.Length; j++)
                    if (part[j] < '0' || part[j] > '9') throw new FormatException("Invalid rational: " + name);
            }
            BigInteger numerator = BigInteger.Parse(parts[0], CultureInfo.InvariantCulture);
            BigInteger denominator = parts.Length == 2 ? BigInteger.Parse(parts[1], CultureInfo.InvariantCulture) : BigInteger.One;
            if (denominator.Sign <= 0) throw new FormatException("Rational denominator must be positive.");
            return new[] { numerator, denominator };
        }
        private static void RationalEqual(string source, BigInteger numerator, BigInteger denominator, string key)
        {
            var r = Rational(source, key);
            if (r[0] * denominator != numerator * r[1])
                throw new FormatException(key + " disagrees with exact source-frame fraction.");
        }

        /// <summary>
        /// Validate a complete StemLab v1 JSON document against independently verified audio identity.
        /// Exported derived fields, when present, must agree with canonical beat/frame anchors.
        /// </summary>
        public static VariableBeatMap Parse(string json, SourceAudioIdentity actualAudio)
        {
            if (actualAudio.Sha256 == null) throw new ArgumentException("Verified audio identity is required.", nameof(actualAudio));
            var doc = Obj(StrictJson.Parse(json), "root");
            if (Str(Required(doc, "schema"), "schema") != Schema) throw new FormatException("Unsupported timeline schema.");
            Equal(doc, "pips_per_beat", 64);
            var audio = Obj(Required(doc, "source_audio"), "source_audio");
            string sha = Str(Required(audio, "sha256"), "source_audio.sha256");
            if (!IsSha256(sha) || sha != actualAudio.Sha256) throw new FormatException("Source SHA-256 mismatch.");
            int rate = (int)Int(Required(audio, "sample_rate"), "sample_rate", 1, 768000);
            long frames = Int(Required(audio, "num_frames"), "num_frames", 2, 10000000000000L);
            int channels = (int)Int(Required(audio, "num_channels"), "num_channels", 1, 512);
            if (rate != actualAudio.SampleRate || frames != actualAudio.FrameCount || channels != actualAudio.Channels)
                throw new FormatException("Beat map does not match selected master's audio metadata.");
            if (audio.TryGetValue("duration_seconds", out object duration))
                Near(duration, frames / (double)rate, "duration_seconds");
            string consensus = Str(Required(doc, "consensus_sha256"), "consensus_sha256");
            if (!IsSha256(consensus)) throw new FormatException("Invalid consensus SHA-256 provenance.");
            int origin = (int)Int(Required(doc, "origin_beat"), "origin_beat", 0, int.MaxValue);
            string status = Str(Required(doc, "review_status"), "review_status");
            if (status != "estimated" && status != "artist_confirmed")
                throw new FormatException("Beat map needs an explicit supported review status.");
            var beatRows = Array(Required(doc, "beats"), "beats");
            if (beatRows.Count < 2 || beatRows.Count > MaxAnchors)
                throw new FormatException("2..25000 authoritative beat anchors are required.");
            var anchors = new List<BeatAnchor>(beatRows.Count);
            var references = new List<long?>(beatRows.Count);
            long prevFrame = -1, prevBeat = -1;
            foreach (object item in beatRows)
            {
                var row = Obj(item, "beats row");
                int beat = (int)Int(Required(row, "beat"), "beat", 0, int.MaxValue);
                long frame = Int(Required(row, "frame"), "frame", 0, frames - 1);
                if (beat <= prevBeat || frame <= prevFrame)
                    throw new FormatException("Source frames and beat ordinals must both increase strictly.");
                string provenance = Str(Required(row, "source"), "source");
                if (provenance != "consensus" && provenance != "corrected" && provenance != "manual")
                    throw new FormatException("Invalid beat provenance.");
                long? reference = null;
                if (row.TryGetValue("reference_frame", out object referenceValue) && referenceValue != null)
                    reference = Int(referenceValue, "reference_frame", 0, frames - 1);
                if (provenance == "consensus" && (reference == null || reference.Value != frame))
                    throw new FormatException("Original detector anchor must preserve reference_frame.");
                if (provenance == "manual" && reference != null)
                    throw new FormatException("Manually inserted beats cannot claim detector evidence.");
                anchors.Add(new BeatAnchor(beat, frame));
                references.Add(reference);
                prevFrame = frame; prevBeat = beat;
            }
            var result = new VariableBeatMap(anchors, rate, frames, origin, sha, consensus, status);
            int originIndex = anchors.FindIndex(a => a.Beat == origin);
            var originFrame = anchors[originIndex].SourceFrame;
            // Exact rational pip(0) evaluated from the first source interval.
            var left = anchors[0]; var right = anchors[1];
            BigInteger denominator = right.SourceFrame - left.SourceFrame;
            BigInteger numerator = 64 * ((BigInteger)(left.Beat - (long)origin) * denominator -
                                          (BigInteger)left.SourceFrame * (right.Beat - (long)left.Beat));
            double offset = (double)numerator / (double)denominator;

            if (doc.TryGetValue("sync", out object syncValue))
            {
                var sync = Obj(syncValue, "sync");
                Equal(sync, "canonical_first_beat_pip", 0);
                Equal(sync, "pips_per_beat", 64);
                Equal(sync, "beat_1_index", origin);
                Equal(sync, "beat_1_source_frame", originFrame);
                Near(Required(sync, "beat_1_start_seconds"), originFrame / (double)rate, "beat_1_start_seconds");
                Near(Required(sync, "sync_phase_offset_pips"), offset, "sync_phase_offset_pips");
                Near(Required(sync, "beat_1_start_pips"), -offset, "beat_1_start_pips");
                RationalEqual(Str(Required(sync, "exact_phase_offset_pips"), "exact_phase_offset_pips"),
                              numerator, denominator, "exact_phase_offset_pips");
                if (sync.TryGetValue("sync_phase_offset_ticks", out object ticks)) Near(ticks, offset / 4, "sync_phase_offset_ticks");
                if (sync.TryGetValue("beat_1_start_ticks", out object starts)) Near(starts, -offset / 4, "beat_1_start_ticks");
            }
            if (doc.TryGetValue("beat_timeline", out object timelineValue))
            {
                var timeline = Array(timelineValue, "beat_timeline");
                if (timeline.Count != anchors.Count) throw new FormatException("Derived beat_timeline length mismatch.");
                for (int i = 0; i < timeline.Count; i++)
                {
                    var row = Obj(timeline[i], "beat_timeline row");
                    Equal(row, "beat", anchors[i].Beat);
                    Equal(row, "source_frame", anchors[i].SourceFrame);
                    Equal(row, "pip", 64L * (anchors[i].Beat - (long)origin));
                    Near(Required(row, "time_seconds"), anchors[i].SourceFrame / (double)rate, "time_seconds");
                    if (!row.TryGetValue("consensus_reference_frame", out object value) ||
                        (references[i] == null ? value != null :
                         value == null || Int(value, "consensus_reference_frame", 0, frames - 1) != references[i].Value))
                        throw new FormatException("Derived consensus reference mismatch.");
                }
            }
            if (doc.TryGetValue("tempo_segments", out object segmentsValue))
            {
                var segments = Array(segmentsValue, "tempo_segments");
                if (segments.Count != anchors.Count - 1) throw new FormatException("Derived tempo_segments length mismatch.");
                for (int i = 0; i < segments.Count; i++)
                {
                    var row = Obj(segments[i], "tempo_segments row");
                    var a = anchors[i]; var b = anchors[i + 1];
                    Equal(row, "from_beat", a.Beat); Equal(row, "to_beat", b.Beat);
                    Equal(row, "start_frame", a.SourceFrame); Equal(row, "end_frame", b.SourceFrame);
                    BigInteger bpNum = (BigInteger)60 * rate * (b.Beat - (long)a.Beat);
                    BigInteger bpDen = b.SourceFrame - a.SourceFrame;
                    Near(Required(row, "bpm"), (double)bpNum / (double)bpDen, "bpm");
                    if (row.TryGetValue("bpm_exact", out object exact))
                        RationalEqual(Str(exact, "bpm_exact"), bpNum, bpDen, "bpm_exact");
                    if (row.TryGetValue("start_seconds", out object first)) Near(first, a.SourceFrame / (double)rate, "start_seconds");
                    if (row.TryGetValue("end_seconds", out object last)) Near(last, b.SourceFrame / (double)rate, "end_seconds");
                }
            }
            return result;
        }
    }
}
