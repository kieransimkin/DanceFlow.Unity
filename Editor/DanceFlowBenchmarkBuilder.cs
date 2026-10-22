using System;
using System.IO;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.GPU;
using DanceFlow.Unity.Performance;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DanceFlow.Unity.Editor
{
    /// <summary>Editor-only deterministic scene and development-Player builder.</summary>
    public static class DanceFlowBenchmarkBuilder
    {
        private const string Folder = "Assets/DanceFlowBenchmark";
        private const string ScenePath = Folder + "/Benchmark.unity";

        [MenuItem("Tools/DanceFlow/Create URP Benchmark Scene")]
        public static void CreateScene()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
                throw new InvalidOperationException("Assign a URP asset before generating the benchmark scene.");
            var normal = Shader.Find("Universal Render Pipeline/Lit");
            var dance = Shader.Find("DanceFlow/URP/BeatPulseVertex");
            if (normal == null || dance == null || !normal.isSupported || !dance.isSupported)
                throw new InvalidOperationException("Import the Shader Clock (URP) sample first and use a supported URP graphics API.");
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "DanceFlowBenchmark"));
            AssetDatabase.Refresh();
            var neutral = GetMaterial(Folder + "/Baseline.mat", normal);
            var musical = GetMaterial(Folder + "/DanceFlow.mat", dance);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var clockObject = new GameObject("DanceFlow benchmark timing");
            var clock = clockObject.AddComponent<DanceFlowClockBehaviour>();
            var bridge = clockObject.AddComponent<DanceFlowShaderBridge>();
            bridge.SetClock(clock);
            var metrics = clockObject.AddComponent<DanceFlowShaderMetrics>();
            metrics.SetBridge(bridge);
            var runner = clockObject.AddComponent<DanceFlowBenchmarkRunner>();
            runner.Configure(clock, bridge, metrics, neutral, musical);

            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 0, -47);
            camera.orthographic = true;
            camera.orthographicSize = 27;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.03f, 0.08f);
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(35, -28, 0);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            Debug.Log("DanceFlow benchmark scene generated at " + ScenePath);
        }

        private static Material GetMaterial(string path, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
                return material;
            }
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        [MenuItem("Tools/DanceFlow/Build URP Benchmark Player")]
        public static void BuildPlayer()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (scene == null)
                CreateScene();
            PlayerSettings.enableFrameTimingStats = true;
            string outputRoot = Path.Combine(Directory.GetCurrentDirectory(), "Build", "DanceFlowBenchmark");
            Directory.CreateDirectory(outputRoot);
            string location;
            switch (EditorUserBuildSettings.activeBuildTarget)
            {
                case BuildTarget.StandaloneWindows64: location = Path.Combine(outputRoot, "DanceFlowBenchmark.exe"); break;
                case BuildTarget.StandaloneOSX: location = Path.Combine(outputRoot, "DanceFlowBenchmark.app"); break;
                case BuildTarget.StandaloneLinux64: location = Path.Combine(outputRoot, "DanceFlowBenchmark"); break;
                default: throw new NotSupportedException("Benchmark Player supports Windows, macOS and Linux standalone targets.");
            }
            var options = new BuildPlayerOptions {
                scenes = new[] { ScenePath },
                locationPathName = location,
                target = EditorUserBuildSettings.activeBuildTarget,
                options = BuildOptions.Development
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Unity benchmark Player build failed: " + report.summary.result);
            Debug.Log("DanceFlow benchmark built at " + location);
        }
    }
}
