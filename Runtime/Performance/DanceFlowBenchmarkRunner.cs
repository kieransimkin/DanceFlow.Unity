using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.GPU;
using UnityEngine;
using UnityEngine.Rendering;

namespace DanceFlow.Unity.Performance
{
    public enum BenchmarkVariant
    {
        Baseline = 0, Clock = 1, Globals = 2, Shader = 3,
        GlobalsMonitored = 4, ShaderMonitored = 5
    }

    /// <summary>
    /// Controlled same-geometry benchmark. Never fabricates GPU readings when graphics timers
    /// are unavailable. Use a built Player on the target hardware for release evidence.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DanceFlowBenchmarkRunner : MonoBehaviour
    {
        [SerializeField] private DanceFlowClockBehaviour clock;
        [SerializeField] private DanceFlowShaderBridge bridge;
        [SerializeField] private DanceFlowShaderMetrics metrics;
        [SerializeField] private Material baselineMaterial;
        [SerializeField] private Material danceFlowMaterial;
        [SerializeField] private BenchmarkVariant variant = BenchmarkVariant.ShaderMonitored;
        [SerializeField, Range(32, 16384)] private int objectCount = 1024;
        [SerializeField, Range(30, 3600)] private int warmupFrames = 600;
        [SerializeField, Range(60, 12000)] private int measurementFrames = 1800;
        [SerializeField] private bool quitPlayerWhenDone = true;

        private readonly List<GameObject> _createdObjects = new List<GameObject>();
        private string _outputPath;
        private string _runId;
        private string _revision;
        private bool _ownsRunningBenchmark;
        private bool _savedFrameRateSettings;
        private int _previousVsync;
        private int _previousFrameRate;

        public void Configure(DanceFlowClockBehaviour musicalClock, DanceFlowShaderBridge publisher,
                              DanceFlowShaderMetrics observer, Material normal, Material musical)
        {
            clock = musicalClock; bridge = publisher; metrics = observer;
            baselineMaterial = normal; danceFlowMaterial = musical;
        }

        private static string ArgumentValue(string key, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == key) return args[i + 1];
            return fallback;
        }

        private void Awake()
        {
            string selection = ArgumentValue("--dfu-mode", variant.ToString());
            if (!Enum.TryParse(selection, true, out variant) || !Enum.IsDefined(typeof(BenchmarkVariant), variant))
                throw new ArgumentException("Unknown DanceFlow benchmark variant: " + selection);
            if (!int.TryParse(ArgumentValue("--dfu-objects", objectCount.ToString()), out objectCount) || objectCount < 32 || objectCount > 16384)
                throw new ArgumentException("--dfu-objects must be 32..16384");
            if (!int.TryParse(ArgumentValue("--dfu-warmup", warmupFrames.ToString()), out warmupFrames) || warmupFrames < 30 || warmupFrames > 3600)
                throw new ArgumentException("--dfu-warmup must be 30..3600");
            if (!int.TryParse(ArgumentValue("--dfu-frames", measurementFrames.ToString()), out measurementFrames) || measurementFrames < 60 || measurementFrames > 12000)
                throw new ArgumentException("--dfu-frames must be 60..12000");
            _outputPath = ArgumentValue("--dfu-output", "");
            _runId = ArgumentValue("--dfu-run", "interactive");
            _revision = ArgumentValue("--dfu-revision", "unrecorded");
        }

        private IEnumerator Start()
        {
            if (_ownsRunningBenchmark) yield break;
            _ownsRunningBenchmark = true;
            if (clock == null || bridge == null || metrics == null || baselineMaterial == null || danceFlowMaterial == null)
            {
                Fail("Benchmark scene is incomplete. Use Tools > DanceFlow > Create URP Benchmark Scene.");
                yield break;
            }
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Fail("No real graphics device detected. Do not run GPU benchmarks with -nographics.");
                yield break;
            }
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                Fail("A Universal Render Pipeline asset is required for this benchmark scene.");
                yield break;
            }
            bool hasClock = variant != BenchmarkVariant.Baseline;
            bool hasPublisher = variant >= BenchmarkVariant.Globals;
            bool shaderEffect = variant == BenchmarkVariant.Shader || variant == BenchmarkVariant.ShaderMonitored;
            bool resources = variant == BenchmarkVariant.GlobalsMonitored || variant == BenchmarkVariant.ShaderMonitored;
            bridge.enabled = hasPublisher;
            clock.enabled = hasClock;
            bridge.SetBenchmarkPublisherTiming(resources);
            BuildGeometry(shaderEffect ? danceFlowMaterial : baselineMaterial);
            // Arrange the SAME number of visible spheres at all object counts. The old
            // 32x32 repeated XY layout occluded everything beyond 1,024 objects.
            var view = Camera.main;
            if (view != null && view.orthographic)
            {
                int columns = Mathf.CeilToInt(Mathf.Sqrt(objectCount));
                int rows = (objectCount + columns - 1) / columns;
                view.orthographicSize = Mathf.Max(6f, 0.75f * (rows + 1));
            }
            // Avoid persisting benchmark-wide graphics settings into the Editor session.
            _previousVsync = QualitySettings.vSyncCount;
            _previousFrameRate = Application.targetFrameRate;
            _savedFrameRateSettings = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            if (hasClock) clock.Transport.Play(); // synthetic/silent, no source-audio accuracy claim
            var eof = new WaitForEndOfFrame();
            for (int i = 0; i < warmupFrames; i++) yield return eof;
            metrics.BeginCapture(variant.ToString(), _runId, _revision,
                warmupFrames, measurementFrames, objectCount, resources,
                everyNFrames: 1, maxSamples: 16384);
            for (int i = 0; i < measurementFrames; i++) yield return eof;
            // FrameTimingManager results are delayed: capture additional frames for the final results.
            for (int i = 0; i < 6; i++) yield return eof;
            metrics.StopCapture();
            string finalPath = !string.IsNullOrEmpty(_outputPath) ? _outputPath :
                Path.Combine(Application.persistentDataPath, "danceflow-benchmark-" + variant + ".json");
            try
            {
                metrics.ExportToJson(Path.GetFullPath(finalPath));
                Debug.Log("DanceFlow benchmark saved to " + Path.GetFullPath(finalPath));
            }
            catch (Exception exception) { Fail("Benchmark export failed: " + exception); yield break; }
            if (quitPlayerWhenDone && !Application.isEditor) Application.Quit(0);
        }

        private void BuildGeometry(Material material)
        {
            var temporary = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh shared = temporary.GetComponent<MeshFilter>().sharedMesh;
            temporary.SetActive(false);
            // Keep the primitive mesh alive through the benchmark; it remains shared by all objects.
            int columns = Mathf.CeilToInt(Mathf.Sqrt(objectCount));
            int rows = (objectCount + columns - 1) / columns;
            for (int i = 0; i < objectCount; i++)
            {
                var item = new GameObject("Benchmark sphere " + i);
                // Fixed spacing, no overlapping coordinates, one depth plane. Preserve
                // identical placement in every paired baseline/instrumented variant.
                item.transform.position = new Vector3(
                    (i % columns - (columns - 1) * 0.5f) * 1.35f,
                    (i / columns - (rows - 1) * 0.5f) * 1.35f, 0);
                item.transform.localScale = Vector3.one * 0.9f;
                item.AddComponent<MeshFilter>().sharedMesh = shared;
                item.AddComponent<MeshRenderer>().sharedMaterial = material;
                _createdObjects.Add(item);
            }
            _createdObjects.Add(temporary);
        }

        private void Fail(string message)
        {
            Debug.LogError(message, this);
            if (!Application.isEditor) Application.Quit(3);
        }
        private void OnDestroy()
        {
            if (_savedFrameRateSettings)
            {
                QualitySettings.vSyncCount = _previousVsync;
                Application.targetFrameRate = _previousFrameRate;
            }
            if (metrics != null) metrics.StopCapture();
            foreach (var obj in _createdObjects)
                if (obj != null) Destroy(obj);
            _createdObjects.Clear();
        }
    }
}
