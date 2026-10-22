using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace DanceFlow.Unity.Samples.Rigging.Editor
{
    /// <summary>Fail-closed audit of the optional original Kandi Boom FBX skeleton.</summary>
    public static class KandiBoomRigAudit
    {
        private const string FbxSha = "714b6a182f2a5684487cd0227b6c9175da604d9c1f98416d44ee23f465ca488e";
        private static readonly string[] Expected = {
            "Hips","LeftUpLeg","LeftLeg","LeftFoot","LeftToeBase","RightUpLeg","RightLeg",
            "RightFoot","RightToeBase","Spine02","Spine01","Spine","LeftShoulder",
            "LeftArm","LeftForeArm","LeftHand","RightShoulder","RightArm","RightForeArm",
            "RightHand","neck","Head","head_end","headfront"
        };

        [MenuItem("Tools/DanceFlow/Stage 4/Validate Selected Kandi Boom Target")]
        public static void ValidateSelected()
        {
            GameObject candidate=Selection.activeGameObject;
            if (candidate == null) throw new InvalidOperationException("Select an imported Kandi Boom prefab or scene instance.");
            var skins=candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length != 1) throw new InvalidOperationException("Expected exactly one Kandi Boom skinned mesh, found " + skins.Length);
            var skin=skins[0];
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var bone in skin.bones)
            {
                if (bone == null || !names.Add(bone.name))
                    throw new InvalidOperationException("Missing or duplicate skinned bone in Kandi Boom target.");
            }
            if (names.Count != Expected.Length) throw new InvalidOperationException("Expected 24 Kandi Boom bones, found " + names.Count);
            foreach(string name in Expected) if (!names.Contains(name))
                throw new InvalidOperationException("Kandi Boom source bone is missing: " + name);
            var source=PrefabUtility.GetCorrespondingObjectFromSource(candidate) ?? candidate;
            string path=AssetDatabase.GetAssetPath(source);
            if (path.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                string found;
                using(var sha=SHA256.Create())
                using(var stream=File.OpenRead(path))
                    found=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
                if (found != FbxSha) throw new InvalidOperationException("FBX source bytes differ from upstream Kandi Boom; review/recalibrate this rig explicitly.");
            }
            else
                Debug.LogWarning("Kandi Boom bone names matched, but original FBX byte identity was not checked. Verify the imported source FBX SHA-256 independently.");
            Debug.Log("Kandi Boom 24-bone skinned skeleton audit passed. This does NOT establish a CMU retarget, correct textures, IK or musical alignment.");
        }
    }
}
