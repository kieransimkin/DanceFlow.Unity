using System;
using System.Collections;
using System.IO;
using System.Reflection;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.Rigging;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DanceFlow.Unity.Tests
{
public sealed class RigSampleIntegrationTests
{
    [UnityTest]
    [Category("RiggedCharacterSample")]
    public IEnumerator SavedSyntheticSceneKeepsAssetsAndBothBackendsDeform()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Type installer = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            installer = installer ?? assembly.GetType("DanceFlow.Unity.Samples.Rigging.Editor.CreateRigDemoScenes");
        if (installer == null) Assert.Ignore("Import the Rigged Character Demos sample first; native rig acceptance remains incomplete without it.");
        installer.GetMethod("Build", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
        string scenePath = SceneManager.GetActiveScene().path;
        Assert.IsNotEmpty(scenePath);
        CheckSavedMeshes();
        EditorSceneManager.OpenScene(scenePath);
        CheckSavedMeshes();
        yield return new EnterPlayMode();
        yield return null;
        var clock = UnityEngine.Object.FindFirstObjectByType<DanceFlowClockBehaviour>();
        var players = UnityEngine.Object.FindObjectsByType<RigMotionPlayerBehaviour>(FindObjectsSortMode.None);
        Assert.AreEqual(2, players.Length);
        clock.Stop();
        clock.SeekSourceSeconds(0.1);
        yield return null;
        yield return null;
        var skins = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.InstanceID);
        var first = new Vector3[skins.Length][];
        for (int i = 0; i < skins.Length; i++) first[i] = Baked(skins[i]);
        Capture("rigs-early");
        clock.SeekSourceSeconds(0.6);
        yield return null;
        yield return null;
        for (int i = 0; i < skins.Length; i++)
        {
            var second = Baked(skins[i]);
            Assert.AreEqual(first[i].Length, second.Length);
            float maximum = 0;
            for (int v = 0; v < second.Length; v++) maximum = Mathf.Max(maximum, (second[v] - first[i][v]).magnitude);
            Assert.Greater(maximum, 0.001f, "Actual skinned vertices must deform: " + skins[i].transform.parent.name);
            Debug.Log("Native rig baked deformation " + skins[i].transform.parent.name + ": " + maximum);
        }
        foreach (var player in players)
        {
            Assert.IsTrue(player.IsInitialized);
            Assert.IsTrue(player.HasPoseThisFrame);
        }
        Capture("rigs-later");
        clock.SeekSourceSeconds(9);
        yield return null;
        yield return null;
        foreach (var player in players)
        {
            Assert.IsFalse(player.HasPoseThisFrame, "Finite recordings must end without wrapping.");
            Assert.Greater(player.LastAbsolutePip, 1023);
        }
        foreach (var skin in skins)
        {
            var neutral = skin.sharedMesh.vertices;
            var ended = Baked(skin);
            float residue = 0;
            for (int v = 0; v < ended.Length; v++) residue = Mathf.Max(residue, (ended[v] - neutral[v]).magnitude);
            Debug.Log("Native rig end-state residue " + skin.transform.parent.name + ": " + residue);
            Assert.Less(residue, 0.0001f, "Exhausted synthetic rigs must return actual skinned vertices to rest.");
        }
        yield return new ExitPlayMode();
    }

    private static void CheckSavedMeshes()
    {
        var skins = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        Assert.AreEqual(2, skins.Length);
        foreach (var skin in skins)
        {
            Assert.IsNotNull(skin.sharedMesh, "Generated mesh must survive scene save/reload.");
            Assert.Greater(skin.sharedMesh.vertexCount, 0);
            Assert.IsTrue(EditorUtility.IsPersistent(skin.sharedMesh), "Generated skinned mesh must be saved as an asset.");
            Assert.IsNotNull(skin.sharedMaterial);
            Assert.IsTrue(EditorUtility.IsPersistent(skin.sharedMaterial), "Generated material must be saved as an asset.");
            Assert.IsTrue(skin.sharedMaterial.shader.isSupported);
        }
    }

    private static Vector3[] Baked(SkinnedMeshRenderer skin)
    {
        var mesh = new Mesh();
        try { skin.BakeMesh(mesh); return mesh.vertices; }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }

    private static void Capture(string name)
    {
        string directory = Environment.GetEnvironmentVariable("DANCEFLOW_TEST_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        var camera = Camera.main;
        Assert.IsNotNull(camera);
        var target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(960, 540, TextureFormat.RGBA32, false);
        var prior = RenderTexture.active;
        try
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            Assert.IsTrue(RenderPipeline.SupportsRenderRequest(camera, request));
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            pixels.Apply();
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = prior;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(pixels);
        }
    }

    [UnityTearDown]
    public IEnumerator CleanOwnedScene()
    {
        if (EditorApplication.isPlaying) yield return new ExitPlayMode();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }
}

}
