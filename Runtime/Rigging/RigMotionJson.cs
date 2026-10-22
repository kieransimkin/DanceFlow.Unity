using System;
using System.Collections.Generic;
using System.Text;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.Motion;

namespace DanceFlow.Unity.Rigging
{
    public sealed class RigMotionDocument
    {
        public readonly RigProfile Profile;
        public readonly MotionRecording Recording;
        public readonly string ProfileJson;
        public readonly string RecordingJson;
        internal RigMotionDocument(RigProfile profile, MotionRecording recording,
            string profileJson, string recordingJson)
        { Profile=profile; Recording=recording; ProfileJson=profileJson; RecordingJson=recordingJson; }
    }

    /// <summary>
    /// Import the canonical DanceRudiments rig-motion wrapper without reserializing floats
    /// or guessing a profile hash. Upstream emits sorted/minified canonical JSON; the nested
    /// profile object is SHA-pinned in exactly that byte form.
    /// </summary>
    public static class RigMotionJson
    {
        public const string Format = "dancerudiments.rig-motion";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static RigMotionDocument ParseUtf8(byte[] raw)
        {
            if (raw == null || raw.Length > RecordingJson.MaxBytes)
                throw new FormatException("Rig motion exceeds 64 MiB.");
            return Parse(Utf8.GetString(raw));
        }
        public static RigMotionDocument Parse(string json)
        {
            var root = StrictJson.Parse(json, RecordingJson.MaxBytes) as Dictionary<string, object>;
            if (root == null || root.Count != 6 ||
                !root.ContainsKey("format") || !root.ContainsKey("schema_version") ||
                !root.ContainsKey("type_uri") || !root.ContainsKey("profile_sha256") ||
                !root.ContainsKey("profile") || !root.ContainsKey("recording"))
                throw new FormatException("Invalid canonical rig-motion wrapper.");
            if (!(root["format"] is string format) || format != Format ||
                !(root["schema_version"] is long version) || version != 1)
                throw new FormatException("Unsupported rig-motion schema.");
            if (!(root["type_uri"] is string typeUri) || !(root["profile_sha256"] is string sha) ||
                sha.Length != 64)
                throw new FormatException("Missing rig-motion type or SHA-256 profile identity.");
            string profileJson = DirectObject(json, "profile");
            string recordingJson = DirectObject(json, "recording");
            var profile = RigProfileJson.Parse(profileJson, sha);
            if (RigProfileJson.CanonicalTypeUri(typeUri) != profile.TypeUri)
                throw new FormatException("Rig-motion type differs from embedded profile.");
            var recording = RecordingJson.Parse(recordingJson);
            profile.RequireMatchingRecording(recording.Descriptor);
            // The profile may be source data for real Rigify controls; it does NOT prove that
            // an arbitrary FBX has those controls. A Unity rig-mapping is always required.
            return new RigMotionDocument(profile, recording, profileJson, recordingJson);
        }

        /// <summary>Extract one top-level object value without altering its original UTF-8 spelling.</summary>
        private static string DirectObject(string json, string key)
        {
            int depth = 0, begin = -1, end = -1;
            bool inString = false, escape = false;
            int tokenStart = -1;
            for (int i=0;i<json.Length;i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (escape) { escape=false; continue; }
                    if (c == '\\') { escape=true; continue; }
                    if (c != '"') continue;
                    inString = false;
                    if (depth == 1 && tokenStart >= 0 &&
                        string.CompareOrdinal(json, tokenStart, "\"" + key + "\"", 0, key.Length + 2) == 0 &&
                        i == tokenStart + key.Length + 1)
                    {
                        int p = i + 1;
                        while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
                        if (p < json.Length && json[p] == ':')
                        {
                            p++; while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
                            if (p >= json.Length || json[p] != '{')
                                throw new FormatException("Rig-motion " + key + " must be a JSON object.");
                            begin = p;
                            int nested = 0; bool quoted = false, escaped = false;
                            for (int k=p;k<json.Length;k++)
                            {
                                char ch=json[k];
                                if (quoted)
                                {
                                    if (escaped) escaped=false;
                                    else if (ch=='\\') escaped=true;
                                    else if (ch=='"') quoted=false;
                                    continue;
                                }
                                if (ch=='"') { quoted=true; continue; }
                                if (ch=='{') nested++;
                                if (ch=='}' && --nested==0) { end=k; break; }
                            }
                            if (end < begin) throw new FormatException("Unterminated embedded " + key + " object.");
                            return json.Substring(begin, end-begin+1);
                        }
                    }
                    continue;
                }
                if (c=='"') { inString=true; tokenStart=i; continue; }
                if (c=='{' || c=='[') depth++;
                else if (c=='}' || c==']') depth--;
            }
            throw new FormatException("Missing top-level rig-motion " + key + " object.");
        }
    }
}
