using System;
using System.IO;
using System.Text;
using DanceFlow.Unity.Rigging;
using DanceFlow.Unity.Motion;
using UnityEditor;
using UnityEngine;

namespace DanceFlow.Unity.Editor
{
    /// <summary>
    /// Explicit upstream rig-motion/profile import. Checks the rig-profile fingerprint and
    /// complete N-dimensional channel identity before creating any Unity assets.
    /// All imports use a unique directory; on error only their own new directory is deleted.
    /// </summary>
    public static class RigMotionImporter
    {
        private const string Destination = "Assets/DanceFlowRigMotions";

        [MenuItem("Assets/DanceFlow/Import DanceRudiments Rig Motion...")]
        public static void ShowRigMotionPicker()
        {
            string path = EditorUtility.OpenFilePanel("Import DanceRudiments rig-motion", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                RigMotionAsset result = ImportRigMotion(path);
                Selection.activeObject = result; EditorGUIUtility.PingObject(result);
                Debug.Log("Imported source-verified rig motion: " + AssetDatabase.GetAssetPath(result));
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is FormatException || e is InvalidOperationException)
            { Debug.LogError("Rig motion import rejected: " + e.Message); }
        }

        [MenuItem("Assets/DanceFlow/Import Rigify Rig Profile...")]
        public static void ShowRigProfilePicker()
        {
            string path = EditorUtility.OpenFilePanel("Import canonical DanceRudiments rig-profile", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                RigProfileAsset profile = ImportProfile(path);
                Selection.activeObject = profile; EditorGUIUtility.PingObject(profile);
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is FormatException)
            { Debug.LogError("Rig profile import rejected: " + e.Message); }
        }

        public static RigMotionAsset ImportRigMotion(string sourcePath)
        {
            byte[] raw = BoundedRead(sourcePath, RecordingJson.MaxBytes);
            string label = Path.GetFileNameWithoutExtension(sourcePath);
            return ImportRigMotionBytes(raw, label);
        }

        public static RigMotionAsset ImportRigMotionBytes(byte[] original, string label)
        {
            RigMotionDocument motion = RigMotionJson.ParseUtf8(original);
            byte[] recordingBytes = new UTF8Encoding(false, true).GetBytes(motion.RecordingJson);
            byte[] cache = RecordingBinaryCodec.Encode(motion.Recording);
            string recordedSha = RecordingBinaryCodec.Sha256(recordingBytes);
            string cachedSha = RecordingBinaryCodec.Sha256(cache);
            byte[] profileBytes = new UTF8Encoding(false, true).GetBytes(motion.ProfileJson);
            string unique = ReserveUniqueFolder(label);
            string dir = Destination + "/" + unique;
            bool created = false;
            try
            {
                string absoluteDir = Path.Combine(Application.dataPath, "DanceFlowRigMotions", unique);
                Directory.CreateDirectory(absoluteDir);
                created = true;
                File.WriteAllBytes(Path.Combine(absoluteDir, "profile.json"), profileBytes);
                File.WriteAllBytes(Path.Combine(absoluteDir, "samples.bytes"), cache);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                TextAsset profileJson = AssetDatabase.LoadAssetAtPath<TextAsset>(dir + "/profile.json");
                TextAsset data = AssetDatabase.LoadAssetAtPath<TextAsset>(dir + "/samples.bytes");
                if (profileJson == null || data == null) throw new IOException("Unity failed to import companion rig bytes.");
                var profileAsset = ScriptableObject.CreateInstance<RigProfileAsset>();
                profileAsset.SetImported(motion.Profile, profileJson);
                AssetDatabase.CreateAsset(profileAsset, dir + "/rig-profile.asset");
                var recordingAsset = ScriptableObject.CreateInstance<MotionRecordingAsset>();
                recordingAsset.SetImported(motion.Recording.Descriptor, data, recordedSha, cachedSha);
                recordingAsset.OpenVerified();
                AssetDatabase.CreateAsset(recordingAsset, dir + "/recording.asset");
                var linked = ScriptableObject.CreateInstance<RigMotionAsset>();
                linked.SetImported(profileAsset, recordingAsset);
                AssetDatabase.CreateAsset(linked, dir + "/rig-motion.asset");
                linked.ValidateLink();
                AssetDatabase.SaveAssets();
                return linked;
            }
            catch
            {
                if (created) AssetDatabase.DeleteAsset(dir);
                throw;
            }
        }

        public static RigProfileAsset ImportProfile(string sourcePath)
        {
            byte[] raw = BoundedRead(sourcePath, RigProfileJson.MaxBytes);
            RigProfile verified = RigProfileJson.ParseUtf8(raw);
            string unique = ReserveUniqueFolder(Path.GetFileNameWithoutExtension(sourcePath));
            string dir = Destination + "/" + unique;
            bool created = false;
            try
            {
                string absoluteDir = Path.Combine(Application.dataPath, "DanceFlowRigMotions", unique);
                Directory.CreateDirectory(absoluteDir); created=true;
                File.WriteAllBytes(Path.Combine(absoluteDir, "profile.json"), raw);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                TextAsset txt=AssetDatabase.LoadAssetAtPath<TextAsset>(dir + "/profile.json");
                if (txt==null) throw new IOException("Unable to import profile.json as TextAsset.");
                var asset=ScriptableObject.CreateInstance<RigProfileAsset>();
                asset.SetImported(verified, txt);
                AssetDatabase.CreateAsset(asset, dir + "/rig-profile.asset");
                AssetDatabase.SaveAssets(); return asset;
            }
            catch { if(created) AssetDatabase.DeleteAsset(dir); throw; }
        }

        private static string ReserveUniqueFolder(string name)
        {
            if (!AssetDatabase.IsValidFolder(Destination))
                AssetDatabase.CreateFolder("Assets", "DanceFlowRigMotions");
            var safe=new StringBuilder();
            foreach(char c in name ?? "rig_motion")
                safe.Append(char.IsLetterOrDigit(c) && c < 128 || c=='_' || c=='-' ? c : '_');
            string seed = safe.Length == 0 ? "rig_motion" : safe.ToString();
            if (seed.Length > 64) seed=seed.Substring(0,64);
            for (int i=0;i<10000;i++)
            {
                string candidate = seed + (i == 0 ? "" : "_" + i);
                string path = Destination + "/" + candidate;
                string absolute = Path.Combine(Application.dataPath, "DanceFlowRigMotions", candidate);
                if (!Directory.Exists(absolute) && !AssetDatabase.IsValidFolder(path)) return candidate;
            }
            throw new IOException("Cannot find a unique rig-motion import folder.");
        }
        private static byte[] BoundedRead(string path, long limit)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.GetExtension(path).ToLowerInvariant() != ".json")
                throw new ArgumentException("Select an explicit .json rig document.");
            var file=new FileInfo(path);
            if (!file.Exists || file.Length > limit || file.Length == 0)
                throw new IOException("Rig source missing or exceeds its bounded JSON size.");
            return File.ReadAllBytes(path);
        }
    }
}
