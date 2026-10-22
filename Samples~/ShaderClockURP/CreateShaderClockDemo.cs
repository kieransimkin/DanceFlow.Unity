#if UNITY_EDITOR
using System.IO;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.GPU;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Generates a real example scene, avoiding hand-authored Unity YAML or shipped audio.</summary>
public static class CreateShaderClockDemo
{
    [MenuItem("Tools/DanceFlow/Create URP Shader Clock Demo Scene")]
    public static void Create()
    {
        if (GraphicsSettings.currentRenderPipeline == null)
        {
            Debug.LogError("Assign a Universal Render Pipeline asset in Graphics Settings first.");
            return;
        }
        var shader = Shader.Find("DanceFlow/URP/BeatPulseVertex");
        if (shader == null || !shader.isSupported)
        {
            Debug.LogError("Import the Shader Clock URP sample and enable URP before creating the scene.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        const string folder = "Assets/DanceFlowShaderClockDemo";
        const string path = folder + "/BeatPulseDemo.unity";
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "DanceFlowShaderClockDemo"));
        AssetDatabase.Refresh();
        var mat = AssetDatabase.LoadAssetAtPath<Material>(folder + "/BeatPulseDemo.mat");
        if (mat == null)
        {
            mat = new Material(shader);
            mat.SetColor("_BaseColor", new Color(0.04f, 0.32f, 0.85f));
            mat.SetFloat("_Displacement", 0.2f);
            mat.SetFloat("_Glow", 1.7f);
            AssetDatabase.CreateAsset(mat, folder + "/BeatPulseDemo.mat");
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var clockObject = new GameObject("DanceFlow Musical Clock");
        var clock = clockObject.AddComponent<DanceFlowClockBehaviour>();
        clockObject.AddComponent<SilentDemoClock>();
        var bridge = clockObject.AddComponent<DanceFlowShaderBridge>();
        bridge.SetClock(clock);
        var metrics = clockObject.AddComponent<DanceFlowShaderMetrics>();
        metrics.SetBridge(bridge);

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Beat-synchronised vertex cube";
        cube.transform.localScale = Vector3.one * 1.8f;
        cube.transform.position = new Vector3(-1.3f, 0, 0);
        cube.GetComponent<MeshRenderer>().sharedMaterial = mat;
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Beat-synchronised fragment sphere";
        sphere.transform.position = new Vector3(1.3f, 0, 0);
        sphere.transform.localScale = Vector3.one * 1.8f;
        sphere.GetComponent<MeshRenderer>().sharedMaterial = mat;

        var camera = new GameObject("Main Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0, 1.6f, -7.5f);
        camera.transform.LookAt(new Vector3(0, 0, 0));
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.02f, 0.03f, 0.08f);
        var light = new GameObject("Directional Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50, -30, 0);
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path);
        AssetDatabase.Refresh();
        Selection.activeGameObject = clockObject;
        Debug.Log("DanceFlow example created at " + path + ". Enter Play mode to see a silent 120 BPM shader clock.");
    }
}
#endif
