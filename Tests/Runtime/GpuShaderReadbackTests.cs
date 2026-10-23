using System;
using System.Collections;
using System.IO;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.GPU;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace DanceFlow.Unity.Tests
{
    /// <summary>Real GPU rendering test. URP and the Shader Clock sample MUST be imported.</summary>
    public sealed class GpuShaderReadbackTests
    {
        [UnityTest]
        public IEnumerator RenderedVertexAndPixelEffectsFollowManualBeatAndSeek()
        {
            var shader = Shader.Find("DanceFlow/URP/BeatPulseVertex");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || shader == null || !shader.isSupported ||
                GraphicsSettings.currentRenderPipeline == null)
            {
                Assert.Inconclusive("A graphics-capable Unity 6 URP project with Shader Clock (URP) sample imported is required.");
                yield break;
            }
            var host = new GameObject("GPU readback host");
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var cameraObject = new GameObject("GPU readback camera");
            var material = new Material(shader);
            var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            try
            {
                material.SetColor("_BaseColor", new Color(0.15f, 0.5f, 0.9f));
                material.SetFloat("_Displacement", 0.6f);
                material.SetFloat("_Glow", 3f);
                sphere.GetComponent<MeshRenderer>().sharedMaterial = material;
                sphere.transform.position = Vector3.zero;
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -4);
                camera.transform.LookAt(Vector3.zero);
                camera.orthographic = true;
                camera.orthographicSize = 2;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.enabled = false;
                var bridge = host.AddComponent<DanceFlowShaderBridge>();
                // Editor batch mode does not resume WaitForEndOfFrame. Rendering below
                // is an explicit synchronous URP request, so one update is sufficient.
                yield return null;

                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (!RenderPipeline.SupportsRenderRequest(camera, request))
                {
                    Assert.Inconclusive("This rendering pipeline does not support synchronous camera render requests.");
                    yield break;
                }

                // Derive samples through the actual Phase 1 transport, never from a second GPU clock.
                var manualClock = new ManualClockSource();
                var transport = new MusicalTransport(new FixedBeatMap(120), manualClock);
                transport.Play();
                bridge.Publish(transport.EvaluateSourceSeconds(0));
                Capture(camera, request, rt, readback, out float bright, out int big);
                SaveEvidence(readback, "beat-on");
                // Seek to halfway through the beat: the same shader becomes dimmer and contracts.
                bridge.Publish(transport.EvaluateSourceSeconds(0.25));
                Capture(camera, request, rt, readback, out float dim, out int small);
                SaveEvidence(readback, "beat-half");

                Assert.Greater(bright, dim + 0.025f,
                    "Fragment emission must follow GPU-visible BeatPulse across seeks.");
                Assert.Greater(big, small + 100,
                    "Rendered silhouette must change via the vertex-stage beat pulse.");
                // And verify a variable-tempo map is consumed through this same Phase 1 transport.
                // Beat 1 occurs at source frame 5000; the next interval has a different BPM.
                var irregular = new VariableBeatMap(
                    new[] { new BeatAnchor(0, 1000), new BeatAnchor(1, 5000), new BeatAnchor(2, 12000) },
                    8000, 16000, 0, new string('a', 64), new string('b', 64), "estimated");
                transport.ReplaceMap(irregular);
                bridge.Publish(transport.EvaluateSourceSeconds(5000.0 / 8000));
                Capture(camera, request, rt, readback, out float variableBright, out _);
                bridge.Publish(transport.EvaluateSourceSeconds(8500.0 / 8000));
                Capture(camera, request, rt, readback, out float variableDim, out _);
                Assert.Greater(variableBright, variableDim + 0.025f,
                    "Fragment shader must follow piecewise variable-tempo beat phase.");
                Debug.Log($"DanceFlow GPU readback: {SystemInfo.graphicsDeviceName}; " +
                    $"brightness {bright:F4}/{dim:F4}; silhouette {big}/{small}; " +
                    $"variable-tempo brightness {variableBright:F4}/{variableDim:F4}");
            }
            finally
            {
                RenderTexture.active = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(sphere);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void SaveEvidence(Texture2D output, string name)
        {
            string directory = Environment.GetEnvironmentVariable("DANCEFLOW_TEST_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory)) return;
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), output.EncodeToPNG());
        }

        private static void Capture(Camera camera, RenderPipeline.StandardRequest request,
                                    RenderTexture rt, Texture2D output,
                                    out float center, out int litPixels)
        {
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            output.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            output.Apply(false, false);
            RenderTexture.active = previous;
            var colours = output.GetPixels32();
            var middle = colours[128 * 256 + 128];
            center = (middle.r + middle.g + middle.b) / (3f * 255f);
            litPixels = 0;
            for (int i = 0; i < colours.Length; i++)
                if (colours[i].b > 35) litPixels++;
        }
    }
}
