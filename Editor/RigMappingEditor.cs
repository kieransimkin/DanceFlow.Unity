using System;
using System.Collections.Generic;
using DanceFlow.Unity.Rigging;
using UnityEditor;
using UnityEngine;

namespace DanceFlow.Unity.Editor
{
    public static class RigMappingEditor
    {
        /// <summary>
        /// Optional exact-name proposal ONLY. Sets approval back to false; the user must
        /// review profile identity, missing bones, retarget basis and target skin.
        /// </summary>
        [MenuItem("Tools/DanceFlow/Stage 4/Propose Exact-Name Bone Mappings")]
        public static void ProposeExactNames()
        {
            var player = Selection.activeGameObject == null ? null :
                Selection.activeGameObject.GetComponent<RigMotionPlayerBehaviour>();
            if (player == null) throw new InvalidOperationException("Select a GameObject with RigMotionPlayerBehaviour.");
            var so=new SerializedObject(player);
            var asset=so.FindProperty("rigProfileAsset").objectReferenceValue as RigProfileAsset;
            var skin=so.FindProperty("skin").objectReferenceValue as SkinnedMeshRenderer;
            if (asset == null || skin == null)
                throw new InvalidOperationException("Assign the validated profile and target SkinnedMeshRenderer first.");
            var profile=asset.OpenVerified();
            var available=new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach(var bone in skin.bones)
            {
                if (bone == null) continue;
                if (available.ContainsKey(bone.name))
                    throw new InvalidOperationException("Duplicate target bone name: " + bone.name + "; map explicitly.");
                available.Add(bone.name,bone);
            }
            var matched=new Dictionary<string, Transform>(StringComparer.Ordinal);
            for(int i=0;i<profile.ChannelCount;i++)
            {
                var channel=profile.ChannelAt(i);
                if(available.TryGetValue(channel.Bone,out Transform target)) matched[channel.Bone]=target;
            }
            if(matched.Count==0) throw new InvalidOperationException("No exact source control names match skinned bones. An explicit retarget map is required.");
            Undo.RecordObject(player,"Propose DanceFlow rig bindings");
            var mappings=so.FindProperty("boneMappings");
            mappings.arraySize=matched.Count;
            int index=0;
            foreach(var match in matched)
            {
                var element=mappings.GetArrayElementAtIndex(index++);
                element.FindPropertyRelative("sourceBone").stringValue=match.Key;
                element.FindPropertyRelative("target").objectReferenceValue=match.Value;
                element.FindPropertyRelative("maskWeight").floatValue=1f;
                element.FindPropertyRelative("translationScale").floatValue=1f;
                element.FindPropertyRelative("isRootMotion").boolValue=false;
                element.FindPropertyRelative("localBasisCorrection").quaternionValue=Quaternion.identity;
            }
            so.FindProperty("mappingReviewedAndApproved").boolValue=false;
            so.ApplyModifiedProperties();
            Debug.Log($"Proposed {matched.Count} exact-name bindings from {profile.ChannelCount} channels; REVIEW REQUIRED before enabling playback.");
        }
        [MenuItem("Tools/DanceFlow/Stage 4/Propose Exact-Name Bone Mappings", true)]
        private static bool CanPropose() => Selection.activeGameObject != null &&
            Selection.activeGameObject.GetComponent<RigMotionPlayerBehaviour>() != null;
    }
}
