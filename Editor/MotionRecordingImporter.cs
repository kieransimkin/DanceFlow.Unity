using System;
using System.IO;
using System.Text;
using DanceFlow.Unity.Motion;
using UnityEditor;
using UnityEngine;

namespace DanceFlow.Unity.Editor
{
    /// <summary>
    /// Explicit import action for upstream .json files. Does NOT hijack Unity's normal JSON
    /// asset importer, write recursively in an AssetPostprocessor, or overwrite existing assets.
    /// </summary>
    public static class MotionRecordingImporter
    {
        private const string OutputFolder = "Assets/DanceFlowRecordings";

        [MenuItem("Assets/DanceFlow/Import DanceRudiments Recording JSON...")]
        public static void ImportDialog()
        {
            string source = EditorUtility.OpenFilePanel("Import DanceRudiments recording", "", "json");
            if (string.IsNullOrEmpty(source)) return;
            try
            {
                MotionRecordingAsset asset = Import(source);
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                Debug.Log("DanceFlow imported: " + AssetDatabase.GetAssetPath(asset));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                        ex is ArgumentException || ex is FormatException || ex is OverflowException)
            {
                Debug.LogError("DanceFlow recording import failed: " + ex.Message);
                EditorUtility.DisplayDialog("DanceFlow import failed", ex.Message, "OK");
            }
        }

        /// <summary>Import into Assets/DanceFlowRecordings; every import gets a unique pair of asset paths.</summary>
        public static MotionRecordingAsset Import(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !string.Equals(Path.GetExtension(sourcePath), ".json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a canonical .json recording.", nameof(sourcePath));
            var info = new FileInfo(sourcePath);
            if (!info.Exists || info.Length > RecordingJson.MaxBytes)
                throw new FormatException("Recording JSON missing or exceeds 64 MiB.");
            byte[] original = File.ReadAllBytes(sourcePath);
            MotionRecording recording = RecordingJson.ParseUtf8(original);
            byte[] encoded = RecordingBinaryCodec.Encode(recording);
            string sourceSha = RecordingBinaryCodec.Sha256(original);
            string cacheSha = RecordingBinaryCodec.Sha256(encoded);

            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder("Assets", "DanceFlowRecordings");
            string requested = Path.GetFileNameWithoutExtension(sourcePath);
            var safe = new StringBuilder();
            foreach (char c in requested)
                safe.Append((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                            (c >= '0' && c <= '9') || c == '_' || c == '-' ? c : '_');
            string label = safe.Length == 0 ? "recording" : safe.ToString();
            if (label.Length > 80) label = label.Substring(0, 80);
            string basePath = null;
            for (int suffix = 0; suffix < 10000; suffix++)
            {
                string candidate = OutputFolder + "/" + label + (suffix == 0 ? "" : "_" + suffix);
                if (AssetDatabase.LoadMainAssetAtPath(candidate + ".asset") == null &&
                    AssetDatabase.LoadMainAssetAtPath(candidate + ".bytes") == null &&
                    !File.Exists(Path.Combine(Application.dataPath, "DanceFlowRecordings", Path.GetFileName(candidate) + ".asset")) &&
                    !File.Exists(Path.Combine(Application.dataPath, "DanceFlowRecordings", Path.GetFileName(candidate) + ".bytes")))
                {
                    basePath = candidate;
                    break;
                }
            }
            if (basePath == null) throw new IOException("Could not allocate a unique recording import path.");
            string dataPath = basePath + ".bytes";
            string assetPath = basePath + ".asset";
            string absoluteDataPath = Path.Combine(Application.dataPath, "DanceFlowRecordings",
                                                    Path.GetFileName(basePath) + ".bytes");
            bool dataCreated = false, assetCreated = false;
            try
            {
                // Create only after full validation. An exception removes only this import's new files.
                using (var stream = new FileStream(absoluteDataPath, FileMode.CreateNew, FileAccess.Write))
                {
                    dataCreated = true;
                    stream.Write(encoded, 0, encoded.Length);
                }
                AssetDatabase.ImportAsset(dataPath, ImportAssetOptions.ForceSynchronousImport);
                TextAsset binary = AssetDatabase.LoadAssetAtPath<TextAsset>(dataPath);
                if (binary == null) throw new IOException("Unity did not import the .bytes recording payload.");
                var asset = ScriptableObject.CreateInstance<MotionRecordingAsset>();
                try
                {
                    asset.SetImported(recording.Descriptor, binary, sourceSha, cacheSha);
                    // Verify the materialized asset and descriptor before publishing the Unity metadata.
                    asset.OpenVerified();
                    AssetDatabase.CreateAsset(asset, assetPath);
                    assetCreated = true;
                }
                finally
                {
                    if (!assetCreated) UnityEngine.Object.DestroyImmediate(asset);
                }
                AssetDatabase.SaveAssets();
                return asset;
            }
            catch
            {
                if (assetCreated) AssetDatabase.DeleteAsset(assetPath);
                if (dataCreated)
                {
                    if (!AssetDatabase.DeleteAsset(dataPath) && File.Exists(absoluteDataPath))
                        File.Delete(absoluteDataPath);
                }
                throw;
            }
        }
    }
}
