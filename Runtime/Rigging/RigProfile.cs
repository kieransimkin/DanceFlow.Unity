using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.Motion;

namespace DanceFlow.Unity.Rigging
{
    public enum RigValueType { Float, Integer, Boolean }
    public enum RigInterpolation { Linear, Step, Quaternion }

    /// <summary>A source control parameter; Number is one-based and immutable across a profile version.</summary>
    public sealed class RigChannel
    {
        public int Number { get; }
        public string Name { get; }
        public string Bone { get; }
        public string DataPath { get; }
        public int ArrayIndex { get; }
        public string Unit { get; }
        public double Minimum { get; }
        public double Maximum { get; }
        public double Neutral { get; }
        public RigValueType ValueType { get; }
        public RigInterpolation Interpolation { get; }
        public string RotationMode { get; }
        private readonly HashSet<int> _enumValues;

        internal RigChannel(int number, string name, string bone, string dataPath, int arrayIndex,
            string unit, double minimum, double maximum, double neutral,
            RigValueType valueType, RigInterpolation interpolation, string rotationMode,
            HashSet<int> enumValues)
        {
            Number = number; Name = name; Bone = bone; DataPath = dataPath;
            ArrayIndex = arrayIndex; Unit = unit; Minimum = minimum; Maximum = maximum;
            Neutral = neutral; ValueType = valueType; Interpolation = interpolation;
            RotationMode = rotationMode; _enumValues = enumValues;
        }

        public double Decode(double encoded)
        {
            if (double.IsNaN(encoded) || double.IsInfinity(encoded) || encoded < -1 || encoded > 1)
                throw new FormatException("Encoded rig control outside finite [-1,1]: " + Name);
            double physical = Minimum + (encoded + 1) * 0.5 * (Maximum - Minimum);
            if (ValueType == RigValueType.Float) return physical;
            double integral = Math.Round(physical);
            double tolerance = Math.Max(1e-9, (Maximum - Minimum) * 1e-12);
            if (Math.Abs(physical - integral) > tolerance ||
                (_enumValues != null && !_enumValues.Contains((int)integral)))
                throw new FormatException("Discrete rig control is not a declared integer: " + Name);
            return integral;
        }

        public bool IsEnumValue(int value) => _enumValues == null || _enumValues.Contains(value);
    }

    /// <summary>
    /// Immutable upstream Rigify or custom rig channel specification. Fingerprint is SHA-256
    /// of the EXACT canonical UTF-8 profile JSON bytes, not a hash of a reshuffled C# object.
    /// </summary>
    public sealed class RigProfile
    {
        private readonly RigChannel[] _channels;
        private readonly Dictionary<string, int> _lookup;
        public string TypeUri { get; }
        public string Model { get; }
        public int Version { get; }
        public string Fingerprint { get; }
        public int ChannelCount => _channels.Length;
        public RigChannel ChannelAt(int column) => _channels[column];
        public RigChannel ChannelNumber(int oneBased) => _channels[checked(oneBased - 1)];
        public bool TryColumn(string name, out int index) => _lookup.TryGetValue(name, out index);
        public string[] DimensionNames
        {
            get { var result = new string[_channels.Length]; for (int i=0;i<result.Length;i++) result[i]=_channels[i].Name; return result; }
        }

        internal RigProfile(string typeUri, string model, int version, RigChannel[] channels, string fingerprint)
        {
            TypeUri = typeUri; Model = model; Version = version; Fingerprint = fingerprint;
            _channels = channels; _lookup = new Dictionary<string, int>(channels.Length, StringComparer.Ordinal);
            for (int i = 0; i < channels.Length; i++) _lookup.Add(channels[i].Name, i);
        }

        public void RequireMatchingRecording(RecordingDescriptor recording)
        {
            if (recording == null || recording.DimensionCount != _channels.Length)
                throw new InvalidOperationException("Rig motion requires the profile's complete ordered channel set.");
            for (int i = 0; i < _channels.Length; i++)
                if (!string.Equals(recording.DimensionAt(i), _channels[i].Name, StringComparison.Ordinal))
                    throw new InvalidOperationException("Recording column " + i + " differs from rig profile channel " + _channels[i].Name);
        }
    }

    public static class RigProfileJson
    {
        public const string Format = "dancerudiments.rig-profile";
        public const int MaxBytes = 32 * 1024 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly string[] RootKeys = { "format", "schema_version", "type_uri", "model", "version", "channels", "source" };
        private static readonly string[] RequiredChannelKeys = {
            "number", "name", "bone", "data_path", "array_index", "unit", "minimum", "maximum", "neutral"
        };
        private static readonly HashSet<string> OptionalChannelKeys = new HashSet<string>(StringComparer.Ordinal) {
            "value_type", "interpolation", "rotation_mode", "enum_items"
        };
        private static readonly HashSet<string> RotationModes = new HashSet<string>(StringComparer.Ordinal) {
            "", "QUATERNION", "AXIS_ANGLE", "XYZ", "XZY", "YXZ", "YZX", "ZXY", "ZYX"
        };

        public static RigProfile ParseUtf8(byte[] source, string expectedFingerprint = null)
        {
            if (source == null || source.Length > MaxBytes) throw new FormatException("Rig profile exceeds 32 MiB.");
            string json = Utf8.GetString(source);
            string digest;
            using (var sha = SHA256.Create()) digest = BitConverter.ToString(sha.ComputeHash(source)).Replace("-", "").ToLowerInvariant();
            if (expectedFingerprint != null &&
                !string.Equals(expectedFingerprint, digest, StringComparison.Ordinal))
                throw new FormatException("Rig profile fingerprint does not match exact upstream canonical bytes.");
            return ParseValidated(json, digest);
        }

        public static RigProfile Parse(string json, string expectedFingerprint = null) =>
            ParseUtf8(json == null ? null : Utf8.GetBytes(json), expectedFingerprint);

        private static RigProfile ParseValidated(string json, string fingerprint)
        {
            var doc = Obj(StrictJson.Parse(json, MaxBytes), "rig-profile");
            ExactKeys(doc, RootKeys, "rig-profile");
            if (Str(doc["format"], "format") != Format || Int(doc["schema_version"], "schema_version", 1, 1) != 1)
                throw new FormatException("Unsupported rig-profile schema.");
            string uri = CanonicalTypeUri(Str(doc["type_uri"], "type_uri"));
            string model = Str(doc["model"], "model");
            if (string.IsNullOrWhiteSpace(model) || model.Length > 1024)
                throw new FormatException("Rig profile model must be a bounded nonempty name.");
            int version = (int)Int(doc["version"], "version", 1, int.MaxValue);
            Obj(doc["source"], "source"); // inert evidence; never executable code
            var raw = Arr(doc["channels"], "channels");
            if (raw.Count < 1 || raw.Count > 1000000) throw new FormatException("Rig channels must be nonempty and bounded.");
            var channels = new RigChannel[raw.Count];
            var names = new HashSet<string>(StringComparer.Ordinal);
            var bindings = new HashSet<string>(StringComparer.Ordinal);
            var perBoneModes = new Dictionary<string, string>(StringComparer.Ordinal);
            var quaternions = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            for (int n = 0; n < raw.Count; n++)
            {
                var o = Obj(raw[n], "channels[" + n + "]");
                foreach (string required in RequiredChannelKeys)
                    if (!o.ContainsKey(required)) throw new FormatException("Missing rig control field: " + required);
                foreach (string key in o.Keys)
                    if (Array.IndexOf(RequiredChannelKeys, key) < 0 && !OptionalChannelKeys.Contains(key))
                        throw new FormatException("Unknown rig control field: " + key);
                int number = (int)Int(o["number"], "number", 1, 1000000);
                if (number != n + 1) throw new FormatException("Rig channel numbers must be contiguous and one-based.");
                string name = Str(o["name"], "name");
                RecordingDescriptor.ValidateName(name, "name");
                string bone = Str(o["bone"], "bone");
                string path = Str(o["data_path"], "data_path");
                string unit = Str(o["unit"], "unit");
                if (string.IsNullOrEmpty(bone) || bone.Length > 1024 ||
                    string.IsNullOrEmpty(path) || path.Length > 2048 ||
                    string.IsNullOrEmpty(unit) || unit.Length > 128)
                    throw new FormatException("Rig control bone/path/unit is missing or oversized.");
                int axis = (int)Int(o["array_index"], "array_index", -1, 1024);
                double minimum = Num(o["minimum"], "minimum");
                double maximum = Num(o["maximum"], "maximum");
                double neutral = Num(o["neutral"], "neutral");
                if (!(minimum < maximum) || double.IsInfinity(maximum - minimum) || neutral < minimum || neutral > maximum)
                    throw new FormatException("Rig control range or neutral value is invalid.");
                string kind = o.ContainsKey("value_type") ? Str(o["value_type"], "value_type") : "float";
                string interpolation = o.ContainsKey("interpolation") ? Str(o["interpolation"], "interpolation") : "linear";
                string rotation = o.ContainsKey("rotation_mode") ? Str(o["rotation_mode"], "rotation_mode") : "";
                if (!RotationModes.Contains(rotation)) throw new FormatException("Unsupported Blender rotation mode.");
                RigValueType valueType = kind == "float" ? RigValueType.Float :
                    kind == "integer" ? RigValueType.Integer : kind == "boolean" ? RigValueType.Boolean :
                    throw new FormatException("Unsupported rig value_type.");
                RigInterpolation mode = interpolation == "linear" ? RigInterpolation.Linear :
                    interpolation == "step" ? RigInterpolation.Step : interpolation == "quaternion" ? RigInterpolation.Quaternion :
                    throw new FormatException("Unsupported rig interpolation.");
                if (valueType != RigValueType.Float && mode != RigInterpolation.Step)
                    throw new FormatException("Discrete rig controls must use step interpolation.");
                if (valueType != RigValueType.Float)
                {
                    if (minimum != Math.Truncate(minimum) || maximum != Math.Truncate(maximum) ||
                        neutral != Math.Truncate(neutral) || maximum - minimum > 1e11 ||
                        (valueType == RigValueType.Boolean && (minimum != 0 || maximum != 1)))
                        throw new FormatException("Invalid discrete rig control range.");
                }
                if (path == "rotation_quaternion" && mode != RigInterpolation.Quaternion)
                    throw new FormatException("Quaternion rotation needs quaternion interpolation.");
                if (mode == RigInterpolation.Quaternion &&
                    (path != "rotation_quaternion" || rotation != "QUATERNION" ||
                     axis < 0 || axis > 3 || minimum != -1 || maximum != 1))
                    throw new FormatException("Quaternion components must be wxyz values in [-1,1].");
                if (!names.Add(name) || !bindings.Add(bone + "\u0000" + path + "\u0000" + axis))
                    throw new FormatException("Duplicate rig control name or bone/path/index.");
                if (rotation.Length > 0)
                {
                    if (perBoneModes.TryGetValue(bone, out string previous) && previous != rotation)
                        throw new FormatException("Control bone has inconsistent rotation modes.");
                    perBoneModes[bone] = rotation;
                }
                HashSet<int> enumValues = null;
                if (o.TryGetValue("enum_items", out object entries))
                {
                    var items = Arr(entries, "enum_items");
                    if (items.Count > 0)
                    {
                        if (valueType != RigValueType.Integer) throw new FormatException("Enums require integer controls.");
                        enumValues = new HashSet<int>();
                        foreach (object entry in items)
                        {
                            var pair = Arr(entry, "enum_items entry");
                            if (pair.Count != 2) throw new FormatException("Enum item must be [integer,label].");
                            int v = (int)Int(pair[0], "enum code", int.MinValue, int.MaxValue);
                            string label = Str(pair[1], "enum label");
                            if (string.IsNullOrEmpty(label) || label.Length > 1024 || v < minimum || v > maximum || !enumValues.Add(v))
                                throw new FormatException("Invalid or duplicate enum entry.");
                        }
                        if (!enumValues.Contains((int)neutral)) throw new FormatException("Enum neutral value must exist.");
                    }
                }
                channels[n] = new RigChannel(number, name, bone, path, axis, unit, minimum, maximum, neutral,
                    valueType, mode, rotation, enumValues);
                if (mode == RigInterpolation.Quaternion)
                {
                    if (!quaternions.TryGetValue(bone, out HashSet<int> indices)) quaternions[bone] = indices = new HashSet<int>();
                    if (!indices.Add(axis)) throw new FormatException("Duplicate quaternion component.");
                }
            }
            foreach (var pair in quaternions)
                if (pair.Value.Count != 4 || !pair.Value.Contains(0) || !pair.Value.Contains(1) ||
                    !pair.Value.Contains(2) || !pair.Value.Contains(3))
                    throw new FormatException("Quaternion controls require complete wxyz components: " + pair.Key);
            return new RigProfile(uri, model, version, channels, fingerprint);
        }

        public static string CanonicalTypeUri(string uri)
        {
            if (uri == null) throw new FormatException("Rig type must be a syn://type URL.");
            if (uri.StartsWith("synny://", StringComparison.Ordinal)) uri = "syn://" + uri.Substring(8);
            if (!uri.StartsWith("syn://type/", StringComparison.Ordinal) || uri.Length <= 11)
                throw new FormatException("Rig type must be an absolute syn://type URL.");
            string[] segments = uri.Substring("syn://type/".Length).Split('/');
            foreach (string segment in segments)
            {
                if (segment.Length == 0) throw new FormatException("Empty rig type segment.");
                foreach (char c in segment)
                    if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_' && c != '-')
                        throw new FormatException("Invalid rig type segment.");
            }
            return uri;
        }

        private static Dictionary<string, object> Obj(object o, string path) => o is Dictionary<string, object> v ? v :
            throw new FormatException(path + " must be an object.");
        private static List<object> Arr(object o, string path) => o is List<object> v ? v :
            throw new FormatException(path + " must be an array.");
        private static string Str(object o, string path) => o is string v ? v :
            throw new FormatException(path + " must be a string.");
        private static long Int(object o, string path, long min, long max) => o is long v && v >= min && v <= max ? v :
            throw new FormatException(path + " must be a bounded integer.");
        private static double Num(object o, string path)
        {
            if (o is long i) return i;
            if (o is double d && !double.IsNaN(d) && !double.IsInfinity(d)) return d;
            throw new FormatException(path + " must be a finite number.");
        }
        private static void ExactKeys(Dictionary<string, object> o, string[] expected, string path)
        {
            if (o.Count != expected.Length) throw new FormatException("Unknown/missing " + path + " fields.");
            foreach (string key in expected) if (!o.ContainsKey(key)) throw new FormatException("Missing " + path + " field: " + key);
        }
    }
}
