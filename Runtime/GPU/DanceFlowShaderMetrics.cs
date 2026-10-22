using System;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

namespace DanceFlow.Unity.GPU
{
    /// <summary>
    /// Opt-in, bounded frame/resource observations. GPU frame time is NOT GPU utilisation,
    /// graphics-driver allocation is NOT VRAM capacity, and zero readings are unavailable.
    /// Reports are written only when ExportToJson is called explicitly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DanceFlowShaderMetrics : MonoBehaviour
    {
        [SerializeField] private DanceFlowShaderBridge bridge;
        [SerializeField] private bool captureFrameTimings;
        [SerializeField, Range(1, 60)] private int sampleEveryNFrames = 4;
        private readonly FrameTiming[] _frameTimings = new FrameTiming[1];
        private readonly double[] _recentFrameMs = new double[256];
        private int _frameCount;
        private int _frameHead;
        private int _gpuSamples;
        private double _gpuSumMs;
        private double _cpuSumMs;
        private int _cpuSamples;
        private double _gpuPeakMs;
        private double _cpuPeakMs;
        private long _lastReservedBytes;
        private long _lastDriverBytes;
        private bool _hasDriverBytes;

        public void SetBridge(DanceFlowShaderBridge source) { bridge = source; }

        public bool HasGpuTiming => _gpuSamples > 0;
        public bool HasCpuTiming => _cpuSamples > 0;
        public double MeanGpuFrameMs => _gpuSamples > 0 ? _gpuSumMs / _gpuSamples : 0;
        public double MeanCpuFrameMs => _cpuSamples > 0 ? _cpuSumMs / _cpuSamples : 0;
        public int SamplesTaken => _frameCount;

        private void LateUpdate()
        {
            if (!captureFrameTimings || !Application.isPlaying) return;
            if (Time.frameCount % Mathf.Max(1, sampleEveryNFrames) != 0) return;
            _frameCount++;
            _lastReservedBytes = Profiler.GetTotalReservedMemoryLong();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _lastDriverBytes = Profiler.GetAllocatedMemoryForGraphicsDriver();
            _hasDriverBytes = _lastDriverBytes > 0;
#endif
            if (!FrameTimingManager.IsFeatureEnabled()) return;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _frameTimings) < 1) return;
            FrameTiming sample = _frameTimings[0];
            if (sample.cpuFrameTime > 0 && !double.IsNaN(sample.cpuFrameTime))
            {
                _cpuSumMs += sample.cpuFrameTime;
                _cpuPeakMs = Math.Max(_cpuPeakMs, sample.cpuFrameTime);
                _cpuSamples++;
                _recentFrameMs[_frameHead++ % _recentFrameMs.Length] = sample.cpuFrameTime;
            }
            if (sample.gpuFrameTime > 0 && !double.IsNaN(sample.gpuFrameTime))
            {
                _gpuSumMs += sample.gpuFrameTime;
                _gpuPeakMs = Math.Max(_gpuPeakMs, sample.gpuFrameTime);
                _gpuSamples++;
            }
        }

        [Serializable]
        private sealed class MetricsReport
        {
            public string schema = "danceflow.unity.shader-metrics.v1";
            public string unityVersion;
            public string graphicsDevice;
            public string graphicsApi;
            public string utcRecorded;
            public string publisherCpuUnit = "microseconds_per_publish";
            public string frameTimingUnit = "milliseconds_per_frame";
            public string resourceUnit = "bytes";
            public string driverMemoryScope = "graphics_driver_allocated_not_total_vram";
            public bool frameTimingSupported;
            public bool gpuTimingAvailable;
            public bool cpuTimingAvailable;
            public bool driverAllocatedMemoryAvailable;
            public bool reservedMemoryAvailable;
            public long publisherCount;
            public long publisherMeasuredCount;
            public double publisherMeanMicroseconds;
            public double publisherPeakMicroseconds;
            public int framesSampled;
            public double gpuMeanMs;
            public double gpuPeakMs;
            public double cpuMeanMs;
            public double cpuPeakMs;
            public long unityTotalReservedBytes;
            public long graphicsDriverAllocatedBytes;
            public string gpuUtilisation = "unavailable: frame time is not GPU core utilisation";
            public string cpuUtilisation = "unavailable: main thread/frame time is not whole-process CPU utilisation";
        }

        /// <summary>Explicit export only. Use paired baseline/plugin runs on identical hardware.</summary>
        public void ExportToJson(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath) || !Path.IsPathRooted(absolutePath))
                throw new ArgumentException("Provide an absolute output file path.", nameof(absolutePath));
            var report = new MetricsReport {
                unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                utcRecorded = DateTime.UtcNow.ToString("o"),
                frameTimingSupported = FrameTimingManager.IsFeatureEnabled(),
                gpuTimingAvailable = HasGpuTiming,
                cpuTimingAvailable = HasCpuTiming,
                driverAllocatedMemoryAvailable = _hasDriverBytes,
                reservedMemoryAvailable = _lastReservedBytes > 0,
                publisherCount = bridge != null ? bridge.PublishedStates : 0,
                publisherMeasuredCount = bridge != null ? bridge.PublisherMeasuredCount : 0,
                publisherMeanMicroseconds = bridge != null ? bridge.PublisherAverageMicroseconds : 0,
                publisherPeakMicroseconds = bridge != null ? bridge.PublisherPeakMicroseconds : 0,
                framesSampled = _frameCount,
                gpuMeanMs = MeanGpuFrameMs,
                gpuPeakMs = _gpuPeakMs,
                cpuMeanMs = MeanCpuFrameMs,
                cpuPeakMs = _cpuPeakMs,
                unityTotalReservedBytes = _lastReservedBytes,
                graphicsDriverAllocatedBytes = _hasDriverBytes ? _lastDriverBytes : 0
            };
            string directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(absolutePath, JsonUtility.ToJson(report, true));
        }
    }
}
