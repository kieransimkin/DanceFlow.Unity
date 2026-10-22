using System;
using System.Collections.Generic;
using System.IO;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.Editor;
using DanceFlow.Unity.Rigging;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DanceFlow.Unity.Samples.Rigging.Editor
{
    /// <summary>
    /// Makes TWO real SkinnedMeshRenderer characters with different bone layouts and two
    /// distinct validated, locally generated rig-motion recordings. No CMU/Kandi data used.
    /// </summary>
    public static class CreateRigDemoScenes
    {
        private const string Output = "Assets/DanceFlowRigDemos";
        [MenuItem("Tools/DanceFlow/Stage 4/Create Two Synthetic Skinned Rig Demos")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string human=FindSource("unity_humanoid_demo.rig-motion.json");
            string creature=FindSource("unity_quadruped_demo.rig-motion.json");
            var humanAsset=RigMotionImporter.ImportRigMotion(human);
            var creatureAsset=RigMotionImporter.ImportRigMotion(creature);
            if (!AssetDatabase.IsValidFolder(Output)) AssetDatabase.CreateFolder("Assets", "DanceFlowRigDemos");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject clockNode=new GameObject("DanceFlow synthetic rig clock (120 BPM)");
            var clock=clockNode.AddComponent<DanceFlowClockBehaviour>();
            var silent=clockNode.AddComponent<SilentRigDemoClock>();silent.SetClock(clock);
            CreateCharacter("Humanoid Robot — direct transforms",new Vector3(-2.3f,0,0),
                new[] {"hips","spine","arm_l","arm_r"},humanAsset,clock,false);
            CreateCharacter("Quadruped Robot — Animation Playables",new Vector3(2.3f,0,0),
                new[] {"hips","spine","tail"},creatureAsset,clock,true);
            var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";
            camera.transform.position=new Vector3(0,3,-12);camera.transform.LookAt(new Vector3(0,1,0));
            camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.02f,.03f,.08f);
            var light=new GameObject("Key Light").AddComponent<Light>();light.type=LightType.Directional;
            light.transform.rotation=Quaternion.Euler(35,-30,0);light.intensity=1.2f;
            string path=AssetDatabase.GenerateUniqueAssetPath(Output+"/TwoSkinnedRigDemos.unity");
            if (!EditorSceneManager.SaveScene(scene,path)) throw new IOException("Failed to save generated Stage 4 scene.");
            Selection.activeObject=AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            Debug.Log("Generated two distinct real SkinnedMeshRenderer rigs, with source-verified synthetic motion and playable/direct playback: " + path);
        }

        private static string FindSource(string suffix)
        {
            foreach(string guid in AssetDatabase.FindAssets("t:TextAsset"))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/" + suffix, StringComparison.OrdinalIgnoreCase))
                    return Path.GetFullPath(path);
            }
            throw new FileNotFoundException("Import the Rigged Character Demos UPM sample via Package Manager before running Stage 4.", suffix);
        }
        private static void CreateCharacter(string name,Vector3 origin,string[] boneNames,
            RigMotionAsset motion,DanceFlowClockBehaviour clock,bool playables)
        {
            var node=new GameObject(name);node.transform.position=origin;
            var skinNode=new GameObject("Skinned Robot");skinNode.transform.SetParent(node.transform,false);
            var bones=new Transform[boneNames.Length];
            for(int i=0;i<bones.Length;i++)
            {
                var b=new GameObject(boneNames[i]).transform;
                Transform parent=i==0 ? skinNode.transform : i==1 ? bones[0] : bones[1];
                b.SetParent(parent,false);
                b.localPosition=i==0?Vector3.zero:i==1?new Vector3(0,1,0):
                    boneNames[i].StartsWith("arm_")?new Vector3(boneNames[i].EndsWith("_l")?-.65f:.65f,.2f,0):
                    new Vector3(0,.30f,.70f);
                bones[i]=b;
            }
            var skin=skinNode.AddComponent<SkinnedMeshRenderer>();
            skin.bones=bones;skin.rootBone=bones[0];
            Mesh mesh=MakeRobotMesh(skinNode.transform,bones);
            mesh.name="Synthetic skinned robot " + name;
            skin.sharedMesh=mesh;
            var shader=Shader.Find("Universal Render Pipeline/Lit");
            if (shader==null) shader=Shader.Find("Standard");
            if(shader!=null) skin.sharedMaterial=new Material(shader){color=playables?new Color(.3f,.85f,.65f):new Color(.5f,.55f,.97f)};
            Animator animator=playables?node.AddComponent<Animator>():null;
            if (playables)
            {
                var avatar=AvatarBuilder.BuildGenericAvatar(node, "Skinned Robot/hips");
                if (avatar == null || !avatar.isValid)
                    throw new InvalidOperationException("Unable to construct a valid generic avatar for the synthetic quadruped.");
                string avatarPath=AssetDatabase.GenerateUniqueAssetPath(Output + "/SyntheticQuadrupedAvatar.asset");
                AssetDatabase.CreateAsset(avatar,avatarPath);
                animator.avatar=avatar;
            }
            var player=node.AddComponent<RigMotionPlayerBehaviour>();
            var so=new SerializedObject(player);
            so.FindProperty("songClock").objectReferenceValue=clock;
            so.FindProperty("recordingAsset").objectReferenceValue=motion.Recording;
            so.FindProperty("rigProfileAsset").objectReferenceValue=motion.Profile;
            so.FindProperty("rigHierarchyRoot").objectReferenceValue=skinNode.transform;
            so.FindProperty("skin").objectReferenceValue=skin;
            so.FindProperty("animator").objectReferenceValue=animator;
            so.FindProperty("mappingReviewedAndApproved").boolValue=true; // controlled synthetic fixtures only
            so.FindProperty("sameOriginalSourceClockApproved").boolValue=true; // authored silent time source
            so.FindProperty("backend").enumValueIndex=playables?1:0;
            so.FindProperty("coordinateSpace").enumValueIndex=0;
            so.FindProperty("rootMotion").enumValueIndex=playables?1:0;
            var mappings=so.FindProperty("boneMappings");mappings.arraySize=bones.Length;
            for(int i=0;i<bones.Length;i++)
            {
                var entry=mappings.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("sourceBone").stringValue=boneNames[i];
                entry.FindPropertyRelative("target").objectReferenceValue=bones[i];
                entry.FindPropertyRelative("maskWeight").floatValue=1f;
                entry.FindPropertyRelative("translationScale").floatValue=1f;
                entry.FindPropertyRelative("isRootMotion").boolValue=i==0;
                entry.FindPropertyRelative("localBasisCorrection").quaternionValue=Quaternion.identity;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Mesh MakeRobotMesh(Transform root,Transform[] bones)
        {
            var verts=new List<Vector3>();var tris=new List<int>();var weights=new List<BoneWeight>();
            foreach(var bone in bones)
            {
                int boneIndex=Array.IndexOf(bones,bone);
                Vector3 center=root.InverseTransformPoint(bone.position);
                float half=bone.name=="hips"?.4f:.26f;
                var corners=new Vector3[] {
                    new Vector3(-half,-half,-half),new Vector3(half,-half,-half),
                    new Vector3(half,half,-half),new Vector3(-half,half,-half),
                    new Vector3(-half,-half,half),new Vector3(half,-half,half),
                    new Vector3(half,half,half),new Vector3(-half,half,half)};
                int first=verts.Count;
                foreach(var corner in corners)
                {
                    verts.Add(center+corner);
                    weights.Add(new BoneWeight {boneIndex0=boneIndex,weight0=1f});
                }
                int[] faces={0,2,1,0,3,2, 4,5,6,4,6,7, 0,1,5,0,5,4,
                             3,7,6,3,6,2, 0,4,7,0,7,3, 1,2,6,1,6,5};
                foreach(int index in faces)tris.Add(first+index);
            }
            var mesh=new Mesh();
            mesh.SetVertices(verts);mesh.SetTriangles(tris,0);mesh.boneWeights=weights.ToArray();
            var poses=new Matrix4x4[bones.Length];
            for(int i=0;i<bones.Length;i++)poses[i]=bones[i].worldToLocalMatrix * root.localToWorldMatrix;
            mesh.bindposes=poses;mesh.RecalculateNormals();mesh.RecalculateBounds();
            return mesh;
        }
    }
}
