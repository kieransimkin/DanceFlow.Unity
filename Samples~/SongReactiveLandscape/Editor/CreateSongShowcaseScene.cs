using System;
using System.IO;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.GPU;
using DanceFlow.Unity.Motion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DanceFlow.Unity.Samples.Editor
{
    /// <summary>
    /// Additive, opt-in sample scene generator: preserves modified open scenes and never
    /// overwrites an existing generated scene. Requires the separate URP sample import.
    /// </summary>
    public static class CreateSongShowcaseScene
    {
        private const string Folder = "Assets/DanceFlowSongShowcase";

        [MenuItem("Tools/DanceFlow/Samples/Create Star Lights Glow Landscape")]
        public static void CreateStarLightsLandscape() => Create(SongShowcasePreset.StarLightsGlowMysteryLiquidKick);

        [MenuItem("Tools/DanceFlow/Samples/Create Arcadians Landscape")]
        public static void CreateArcadiansLandscape() => Create(SongShowcasePreset.Arcadians);

        [MenuItem("Tools/DanceFlow/Samples/Create The Truth is a Virus Landscape")]
        public static void CreateTruthLandscape() => Create(SongShowcasePreset.TheTruthIsAVirus);

        [MenuItem("Tools/DanceFlow/Samples/Create Synthetic Motion Landscape")]
        public static void CreatePreviewLandscape() => Create(SongShowcasePreset.SyntheticPreview);

        [MenuItem("Tools/DanceFlow/Samples/Create Scene From Selected Song Profile")]
        public static void CreateFromSelectedProfile()
        {
            var profile = Selection.activeObject as SongShowcaseProfile;
            if (profile == null)
                throw new InvalidOperationException("Select a DanceFlow Song Showcase Profile in the Project window.");
            profile.ValidateForPlayback();
            Create(profile.Song, profile);
        }

        [MenuItem("Tools/DanceFlow/Samples/Create Scene From Selected Song Profile", true)]
        private static bool CanCreateFromSelectedProfile() => Selection.activeObject is SongShowcaseProfile;

        private static void Create(SongShowcasePreset preset, SongShowcaseProfile profile = null)
        {
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
                throw new InvalidOperationException("Assign a Universal Render Pipeline asset before creating this demo.");
            Shader shader = Shader.Find("DanceFlow/URP/MotionLandscape");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("Import the Song Reactive Landscape package sample (including MotionLandscape.shader) first.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "DanceFlowSongShowcase");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("DanceFlow Song Showcase — " + preset);
            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = false;
            DanceFlowClockBehaviour clock = root.AddComponent<DanceFlowClockBehaviour>();
            var serialized = new SerializedObject(clock);
            serialized.FindProperty("audioSource").objectReferenceValue = audio;
            serialized.FindProperty("nominalBpm").floatValue = 120;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            DanceFlowShaderBridge bridge = root.AddComponent<DanceFlowShaderBridge>();
            bridge.SetClock(clock);
            var bus = root.AddComponent<MotionSignalBus>();
            bus.Configure(clock);
            var gpu = root.AddComponent<MotionRecordingGpuBridge>();
            var gpuProps = new SerializedObject(gpu);
            gpuProps.FindProperty("songClock").objectReferenceValue = clock;
            gpuProps.FindProperty("signalBus").objectReferenceValue = bus;
            gpuProps.ApplyModifiedPropertiesWithoutUndo();
            var demo = root.AddComponent<SongShowcaseBootstrap>();
            demo.Configure(preset, clock, audio, bus);
            if (profile != null) demo.ConfigureProfile(profile);
            root.AddComponent<ProceduralLandscape>();

            var cameraObject = new GameObject("Main Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.015f, 0.025f, 0.10f);
            camera.farClipPlane = 220;
            camera.fieldOfView = 62;
            camera.transform.position = new Vector3(0, 15, -56);
            camera.transform.LookAt(new Vector3(0, 5, 6));
            var lightObject = new GameObject("Moonlight");
            Light moon = lightObject.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.52f, 0.70f, 1);
            moon.intensity = 0.55f;
            moon.transform.rotation = Quaternion.Euler(45, -30, 0);
            var atmosphere = root.AddComponent<SongShowcaseAtmosphere>();
            atmosphere.Configure(bus, camera, moon);
            var overlay = root.AddComponent<SongShowcaseOverlay>();
            overlay.Configure(bus, clock, demo, audio);
            string path = AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + preset + ".unity");
            if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("Unable to save generated demo scene.");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            Debug.Log("DanceFlow sample scene created at " + path + ". Starts in SYNTHETIC PREVIEW unless you import and wire actual song/recording assets.");
        }
    }
}
