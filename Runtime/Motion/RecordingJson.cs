using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DanceFlow.Unity.Clock;

namespace DanceFlow.Unity.Motion
{
    /// <summary>
    /// Strict reader for the canonical DanceRudiments schema 1 recording, not the legacy XYZ pack.
    /// Reuses Phase 1's duplicate-key-aware JSON parser, with recording-specific size limit.
    /// The only rounding is upstream's 12-decimal authoring normalisation.
    /// </summary>
    public static class RecordingJson
    {
        public const int MaxBytes = 64 * 1024 * 1024;
        public const int MaxValues = 8_388_608;
        public const string Format = "dancerudiments.recording";
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly string[] RootKeys = {
            "format", "schema_version", "pips_per_beat", "sample_dtype", "sample_decimals",
            "name", "dimensions", "samples", "clock", "provenance"
        };
        private static readonly string[] ClockKeys = {
            "bpm", "bpm_source", "bpm_status", "beat_zero_seconds", "phase_status", "start_pip"
        };

        public static MotionRecording ParseUtf8(byte[] utf8)
        {
            if (utf8 == null || utf8.Length > MaxBytes) throw new FormatException("Recording JSON exceeds 64 MiB.");
            return Parse(StrictUtf8.GetString(utf8));
        }

        public static MotionRecording Parse(string json)
        {
            var doc = Obj(StrictJson.Parse(json, MaxBytes), "recording");
            RequiredKeys(doc, RootKeys, "recording");
            if (Str(doc["format"], "format") != Format ||
                Int(doc["schema_version"], "schema_version", 1, 1) != 1 ||
                Int(doc["pips_per_beat"], "pips_per_beat", 64, 64) != 64 ||
                Str(doc["sample_dtype"], "sample_dtype") != "float64" ||
                Int(doc["sample_decimals"], "sample_decimals", 12, 12) != 12)
                throw new FormatException("Unsupported DanceRudiments recording format/version.");

            string name = Str(doc["name"], "name");
            var rawDims = Arr(doc["dimensions"], "dimensions");
            if (rawDims.Count < 1 || rawDims.Count > MaxValues)
                throw new FormatException("Recording must contain at least one valid dimension.");
            var dimensions = new string[rawDims.Count];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < dimensions.Length; i++)
            {
                dimensions[i] = Str(rawDims[i], "dimensions[" + i + "]");
                RecordingDescriptor.ValidateName(dimensions[i], "dimensions[" + i + "]");
                if (!seen.Add(dimensions[i])) throw new FormatException("Duplicate recording channel: " + dimensions[i]);
            }

            var rawClock = Obj(doc["clock"], "clock");
            RequiredKeys(rawClock, ClockKeys, "clock");
            var clock = new RecordingClock(
                Number(rawClock["bpm"], "clock.bpm"),
                Str(rawClock["bpm_status"], "clock.bpm_status"),
                Str(rawClock["bpm_source"], "clock.bpm_source"),
                Number(rawClock["beat_zero_seconds"], "clock.beat_zero_seconds"),
                Str(rawClock["phase_status"], "clock.phase_status"),
                (int)Int(rawClock["start_pip"], "clock.start_pip", int.MinValue, int.MaxValue));

            var rows = Arr(doc["samples"], "samples");
            if (rows.Count < 1 || rows.Count > MaxValues / dimensions.Length)
                throw new FormatException("Empty recording or more than 8,388,608 scalar values.");
            if ((long)clock.StartPip + rows.Count > int.MaxValue)
                throw new FormatException("Recording extends outside the native signed pip range.");
            var values = new double[checked(rows.Count * dimensions.Length)];
            for (int row = 0; row < rows.Count; row++)
            {
                var channels = Arr(rows[row], "samples[" + row + "]");
                if (channels.Count != dimensions.Length)
                    throw new FormatException("Ragged sample row " + row + "; expected " + dimensions.Length + " channels.");
                for (int axis = 0; axis < dimensions.Length; axis++)
                {
                    double value = Number(channels[axis], "samples[" + row + "][" + axis + "]");
                    if (value < -1 || value > 1) throw new FormatException("Sample outside [-1,1].");
                    values[row * dimensions.Length + axis] = Math.Round(value, 12, MidpointRounding.ToEven);
                }
            }
            var provenance = Obj(doc["provenance"], "provenance");
            string provenanceJson = EncodeJson(provenance);
            return new MotionRecording(new RecordingDescriptor(name, dimensions, clock,
                rows.Count, provenanceJson), values);
        }

        private static Dictionary<string, object> Obj(object value, string path)
        {
            if (value is Dictionary<string, object> map) return map;
            throw new FormatException(path + " must be a JSON object.");
        }

        private static List<object> Arr(object value, string path)
        {
            if (value is List<object> array) return array;
            throw new FormatException(path + " must be a JSON array.");
        }

        private static string Str(object value, string path)
        {
            if (value is string text) return text;
            throw new FormatException(path + " must be a string.");
        }

        private static long Int(object value, string path, long min, long max)
        {
            if (value is long n && n >= min && n <= max) return n;
            throw new FormatException(path + " must be an integer in the supported range.");
        }

        private static double Number(object value, string path)
        {
            if (value is long n) return n;
            if (value is double d && !double.IsInfinity(d) && !double.IsNaN(d)) return d;
            throw new FormatException(path + " must be a finite number.");
        }

        private static void RequiredKeys(Dictionary<string, object> data, string[] expected, string path)
        {
            if (data.Count != expected.Length)
                throw new FormatException(path + " contains missing or unknown fields.");
            foreach (string name in expected)
                if (!data.ContainsKey(name)) throw new FormatException(path + " missing field " + name);
        }

        private static string EncodeJson(object source)
        {
            var builder = new StringBuilder();
            WriteValue(builder, source);
            return builder.ToString();
        }

        private static void WriteValue(StringBuilder b, object source)
        {
            if (source == null) { b.Append("null"); return; }
            if (source is string text)
            {
                b.Append('"');
                foreach (char c in text)
                {
                    switch (c)
                    {
                        case '"': b.Append("\\\""); break;
                        case '\\': b.Append("\\\\"); break;
                        case '\b': b.Append("\\b"); break;
                        case '\f': b.Append("\\f"); break;
                        case '\n': b.Append("\\n"); break;
                        case '\r': b.Append("\\r"); break;
                        case '\t': b.Append("\\t"); break;
                        default:
                            if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else b.Append(c);
                            break;
                    }
                }
                b.Append('"');
            }
            else if (source is Dictionary<string, object> obj)
            {
                b.Append('{');
                var keys = new List<string>(obj.Keys); keys.Sort(StringComparer.Ordinal);
                for (int i = 0; i < keys.Count; i++)
                {
                    if (i > 0) b.Append(',');
                    WriteValue(b, keys[i]); b.Append(':'); WriteValue(b, obj[keys[i]]);
                }
                b.Append('}');
            }
            else if (source is List<object> array)
            {
                b.Append('[');
                for (int i = 0; i < array.Count; i++)
                {
                    if (i > 0) b.Append(',');
                    WriteValue(b, array[i]);
                }
                b.Append(']');
            }
            else if (source is long n) b.Append(n.ToString(CultureInfo.InvariantCulture));
            else if (source is double d) b.Append(d.ToString("R", CultureInfo.InvariantCulture));
            else if (source is bool flag) b.Append(flag ? "true" : "false");
            else throw new FormatException("Unsupported JSON provenance value.");
        }
    }
}
