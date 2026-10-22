using System;
using System.IO;
using DanceFlow.Unity.Performance;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace DanceFlow.Unity.GPU
{
    /// <summary>
    /// Opt-in, bounded hardware observations. A GPU frame time is NOT GPU utilisation;
    /// video memory counters are estimated usage, never free physical VRAM.
    /// Capturing is disabled by default; only explicit ExportToJson writes a file.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DanceFlowShaderMetrics : MonoBehaviour
    {
        [Serializable]
        public struct FrameObservation
        {
            public int unityFrame;
            // Delayed FrameTimingManager values belong to the frame identified by this timestamp.
            public long timingFrameStartTimestamp;
            public bool cpuFrameAvailable;
            public double cpuFrameMs;
            public bool gpuFrameAvailable;
            public double gpuFrameMs;
            public bool mainThreadAvailable;
            public double mainThreadMs;
            public bool gcAllocatedAvailable;
            public long gcAllocatedBytes;
            public bool reservedMemoryAvailable;
            public long totalReservedBytes;
            public bool videoUsedMemoryAvailable;
            public long videoUsedBytes;
            public bool driverAllocatedMemoryAvailable;
            public long graphicsDriverAllocatedBytes;
            public bool publisherAvailable;
            public double publisherMicroseconds;
        }

        [Serializable]
        public sealed class MetricSummary
        {
            public int count;
            public double mean;
            public double minimum;
            public double maximum;
            public double p50;
            public double p95;
            public double p99;
        }

        [Serializable]
        public sealed class MetricsReport
        {
            public string schema = "danceflow.unity.benchmark.v2";
            public string scenario;
            public string runId;
            public string revision;
            public string utcStarted;
            public string utcEnded;
            public string unityVersion;
            public string graphicsDevice;
            public string graphicsApi;
            public string processor;
            public string operatingSystem;
            public string scene;
            public string qualityLevel;
            public string platform;
            public int width;
            public int height;
            public int refreshRateTarget;
            public int vsyncCount;
            public bool frameTimingSupported;
            public bool gpuTimingAvailable;
            public bool cpuTimingAvailable;
            public bool gcCounterAvailable;
            public bool videoUsedMemoryAvailable;
            public bool reservedMemoryAvailable;
            public bool driverAllocatedMemoryAvailable;
            public bool recordResourceCounters;
            public int sampleEveryNFrames;
            public int warmupFrames;
            public int requestedMeasuredFrames;
            public int framesSampled;
            public long publisherCount;
            public long publisherTimedCount;
            public int observationsOverwritten;
            public int renderObjectCount;
            public int graphicsMemoryCapacityReportedMb;
            public bool runningInEditor;
            public string gpuUtilisation = "unavailable: GPU frame time is not core utilisation";
            public string cpuUtilisation = "unavailable: frame time is not host CPU utilisation";
            public string memoryScope = "video used/reserved are Unity estimates, not GPU capacity or free VRAM";
            public MetricSummary cpuFrameMs;
            public MetricSummary gpuFrameMs;
            public MetricSummary mainThreadMs;
            public MetricSummary gcAllocatedBytes;
            public MetricSummary publisherMicroseconds;
            public long totalReservedBytesLast;
            public long videoUsedBytesLast;
            public long graphicsDriverAllocatedBytesLast;
            public FrameObservation[] observations;
        }

        [SerializeField] private DanceFlowShaderBridge bridge;
        [SerializeField] private bool captureFrameTimings;
        [SerializeField, Range(1, 60)] private int sampleEveryNFrames = 4;
        [SerializeField] private bool captureResourceCounters;
        [SerializeField, Range(128, 16384)] private int capacity = 8192;

        private FrameObservation[] _observations;
        private readonly FrameTiming[] _frameTimings = new FrameTiming[1];
        private int _count, _writeIndex, _overwritten, _frameCount;
        private ulong _lastFrameTimingTimestamp;
        private ProfilerRecorder _gcAllocated;
        private ProfilerRecorder _reserved;
        private ProfilerRecorder _videoUsed;
        private bool _counterCaptureActive;
        private string _scenario = "manual";
        private string _runId = "manual";
        private string _revision = "unrecorded";
        private string _startedUtc;
        private long _publishedAtStart, _publisherTimedAtStart;
        private int _warmupFrames, _measuredFrames, _renderObjectCount;
        private double _gpuSum, _cpuSum, _cpuMainSum;
        private int _gpuSamples, _cpuSamples, _mainSamples;
        private double _gpuPeak, _cpuPeak;
        private long _lastReserved, _lastVideo, _lastDriver, _lastGc;
        private bool _hasReserved, _hasVideo, _hasDriver, _hasGc;

        public void SetBridge(DanceFlowShaderBridge source) => bridge = source;
        public bool HasGpuTiming => _gpuSamples > 0;
        public bool HasCpuTiming => _cpuSamples > 0;
        public double MeanGpuFrameMs => _gpuSamples > 0 ? _gpuSum / _gpuSamples : 0;
        public double MeanCpuFrameMs => _cpuSamples > 0 ? _cpuSum / _cpuSamples : 0;
        public int SamplesTaken => _frameCount;
        public bool IsCapturing => captureFrameTimings;
        public bool HasGcAllocationCounter => _hasGc;
        public long LastGcAllocatedBytes => _lastGc;
        public bool HasVideoMemoryEstimate => _hasVideo;
        public long LastVideoUsedBytes => _lastVideo;
        public bool HasReservedMemoryCounter => _hasReserved;
        public long LastReservedBytes => _lastReserved;

        public void BeginCapture(string scenario, string runId, string revision = "unrecorded",
                                 int warmupFrames = 0, int measuredFrames = 0, int renderObjectCount = 0,
                                 bool resourceCounters = true, int everyNFrames = 1, int maxSamples = 8192)
        {
            StopCapture();
            if (string.IsNullOrEmpty(scenario) || string.IsNullOrEmpty(runId))
                throw new ArgumentException("Scenario and run ID are mandatory for a formal capture.");
            _scenario = scenario;
            _runId = runId;
            _revision = revision;
            _warmupFrames = warmupFrames;
            _measuredFrames = measuredFrames;
            _renderObjectCount = renderObjectCount;
            sampleEveryNFrames = Mathf.Clamp(everyNFrames, 1, 60);
            capacity = Mathf.Clamp(maxSamples, 128, 16384);
            _observations = new FrameObservation[capacity];
            _count = _writeIndex = _overwritten = _frameCount = 0;
            _lastFrameTimingTimestamp = 0;
            _gpuSum = _cpuSum = _cpuMainSum = _gpuPeak = _cpuPeak = 0;
            _gpuSamples = _cpuSamples = _mainSamples = 0;
            _lastReserved = _lastVideo = _lastDriver = _lastGc = 0;
            _hasReserved = _hasVideo = _hasDriver = _hasGc = false;
            _startedUtc = DateTime.UtcNow.ToString("o");
            _publishedAtStart = bridge != null ? bridge.PublishedStates : 0;
            _publisherTimedAtStart = bridge != null ? bridge.PublisherMeasuredCount : 0;
            captureResourceCounters = resourceCounters;
            if (captureResourceCounters)
            {
                _gcAllocated = TryRecorder("GC Allocated In Frame");
                _reserved = TryRecorder("Total Reserved Memory");
                _videoUsed = TryRecorder("Video Used Memory");
                _counterCaptureActive = true;
            }
            captureFrameTimings = true;
        }

        private static ProfilerRecorder TryRecorder(string name)
        {
            try { return ProfilerRecorder.StartNew(ProfilerCategory.Memory, name, 1); }
            catch (Exception) { return default; } // unsupported counter; report as unavailable
        }

        public void StopCapture()
        {
            captureFrameTimings = false;
            if (_counterCaptureActive)
            {
                if (_gcAllocated.Valid) _gcAllocated.Dispose();
                if (_reserved.Valid) _reserved.Dispose();
                if (_videoUsed.Valid) _videoUsed.Dispose();
                _counterCaptureActive = false;
            }
        }
        private void OnDisable() => StopCapture();

        private static bool ReadCounter(ProfilerRecorder counter, out long value)
        {
            value = 0;
            if (!counter.Valid || counter.Count < 1) return false;
            value = counter.LastValue;
            // A zero GC allocation is valid; other unsupported counters can also return zero.
            return true;
        }

        private void LateUpdate()
        {
            if (!captureFrameTimings || !Application.isPlaying) return;
            if (Time.frameCount % Mathf.Max(1, sampleEveryNFrames) != 0) return;
            var sample = new FrameObservation { unityFrame = Time.frameCount };
            _frameCount++;
            if (captureResourceCounters)
            {
                sample.gcAllocatedAvailable = ReadCounter(_gcAllocated, out sample.gcAllocatedBytes);
                _hasGc |= sample.gcAllocatedAvailable;
                if (sample.gcAllocatedAvailable) _lastGc = sample.gcAllocatedBytes;
                sample.reservedMemoryAvailable = ReadCounter(_reserved, out sample.totalReservedBytes) && sample.totalReservedBytes > 0;
                if (sample.reservedMemoryAvailable)
                {
                    _lastReserved = sample.totalReservedBytes;
                    _hasReserved = true;
                }
                sample.videoUsedMemoryAvailable = ReadCounter(_videoUsed, out sample.videoUsedBytes) && sample.videoUsedBytes > 0;
                if (sample.videoUsedMemoryAvailable)
                {
                    _lastVideo = sample.videoUsedBytes;
                    _hasVideo = true;
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _lastDriver = Profiler.GetAllocatedMemoryForGraphicsDriver();
                sample.driverAllocatedMemoryAvailable = _lastDriver > 0;
                sample.graphicsDriverAllocatedBytes = _lastDriver;
                _hasDriver |= sample.driverAllocatedMemoryAvailable;
#endif
            }
            if (bridge != null && bridge.PublisherMeasuredCount > 0)
            {
                sample.publisherAvailable = true;
                sample.publisherMicroseconds = bridge.PublisherLastMicroseconds;
            }
            if (FrameTimingManager.IsFeatureEnabled())
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _frameTimings) > 0)
                {
                    FrameTiming timing = _frameTimings[0];
                    // The engine returns delayed timings; don't count one frame repeatedly.
                    if (timing.frameStartTimestamp != _lastFrameTimingTimestamp && timing.frameStartTimestamp != 0)
                    {
                        _lastFrameTimingTimestamp = timing.frameStartTimestamp;
                        sample.timingFrameStartTimestamp = unchecked((long)timing.frameStartTimestamp);
                        if (timing.cpuFrameTime > 0 && !double.IsNaN(timing.cpuFrameTime) && !double.IsInfinity(timing.cpuFrameTime))
                        {
                            sample.cpuFrameAvailable = true;
                            sample.cpuFrameMs = timing.cpuFrameTime;
                            _cpuSum += timing.cpuFrameTime;
                            _cpuPeak = Math.Max(_cpuPeak, timing.cpuFrameTime);
                            _cpuSamples++;
                        }
                        if (timing.cpuMainThreadFrameTime > 0 && !double.IsNaN(timing.cpuMainThreadFrameTime) && !double.IsInfinity(timing.cpuMainThreadFrameTime))
                        {
                            sample.mainThreadAvailable = true;
                            sample.mainThreadMs = timing.cpuMainThreadFrameTime;
                            _cpuMainSum += timing.cpuMainThreadFrameTime;
                            _mainSamples++;
                        }
                        if (timing.gpuFrameTime > 0 && !double.IsNaN(timing.gpuFrameTime) && !double.IsInfinity(timing.gpuFrameTime))
                        {
                            sample.gpuFrameAvailable = true;
                            sample.gpuFrameMs = timing.gpuFrameTime;
                            _gpuSum += timing.gpuFrameTime;
                            _gpuPeak = Math.Max(_gpuPeak, timing.gpuFrameTime);
                            _gpuSamples++;
                        }
                    }
                }
            }
            if (_observations == null || _observations.Length == 0) _observations = new FrameObservation[capacity];
            if (_count == _observations.Length) _overwritten++;
            else _count++;
            _observations[_writeIndex] = sample;
            _writeIndex = (_writeIndex + 1) % _observations.Length;
        }

        private static MetricSummary Summarize(FrameObservation[] values, int metric)
        {
            var list = new double[values.Length];
            int used = 0;
            foreach (var row in values)
            {
                bool valid;
                double value;
                switch (metric)
                {
                    case 0: valid = row.cpuFrameAvailable; value = row.cpuFrameMs; break;
                    case 1: valid = row.gpuFrameAvailable; value = row.gpuFrameMs; break;
                    case 2: valid = row.mainThreadAvailable; value = row.mainThreadMs; break;
                    case 3: valid = row.gcAllocatedAvailable; value = row.gcAllocatedBytes; break;
                    default: valid = row.publisherAvailable; value = row.publisherMicroseconds; break;
                }
                if (valid) list[used++] = value;
            }
            var s = FrameSeriesStats.Calculate(list, used);
            return new MetricSummary { count = s.Count, mean = s.Mean, minimum = s.Minimum,
                maximum = s.Maximum, p50 = s.P50, p95 = s.P95, p99 = s.P99 };
        }

        public MetricsReport GetReport()
        {
            var rows = new FrameObservation[_count];
            if (_observations != null && _count > 0)
            {
                int start = _count == _observations.Length ? _writeIndex : 0;
                for (int i = 0; i < _count; i++)
                    rows[i] = _observations[(start + i) % _observations.Length];
            }
            return new MetricsReport {
                scenario = _scenario, runId = _runId, revision = _revision,
                utcStarted = _startedUtc, utcEnded = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                processor = SystemInfo.processorType,
                operatingSystem = SystemInfo.operatingSystem,
                scene = SceneManager.GetActiveScene().name,
                qualityLevel = QualitySettings.names[QualitySettings.GetQualityLevel()],
                platform = Application.platform.ToString(),
                width = Screen.width, height = Screen.height,
                refreshRateTarget = Application.targetFrameRate,
                vsyncCount = QualitySettings.vSyncCount,
                frameTimingSupported = FrameTimingManager.IsFeatureEnabled(),
                gpuTimingAvailable = HasGpuTiming, cpuTimingAvailable = HasCpuTiming,
                gcCounterAvailable = _hasGc, videoUsedMemoryAvailable = _hasVideo,
                reservedMemoryAvailable = _hasReserved, driverAllocatedMemoryAvailable = _hasDriver,
                recordResourceCounters = captureResourceCounters,
                sampleEveryNFrames = sampleEveryNFrames,
                warmupFrames = _warmupFrames, requestedMeasuredFrames = _measuredFrames,
                framesSampled = _frameCount, observationsOverwritten = _overwritten,
                publisherCount = bridge != null ? Math.Max(0L, bridge.PublishedStates - _publishedAtStart) : 0,
                publisherTimedCount = bridge != null ? Math.Max(0L, bridge.PublisherMeasuredCount - _publisherTimedAtStart) : 0,
                renderObjectCount = _renderObjectCount,
                graphicsMemoryCapacityReportedMb = SystemInfo.graphicsMemorySize,
                runningInEditor = Application.isEditor,
                cpuFrameMs = Summarize(rows, 0), gpuFrameMs = Summarize(rows, 1),
                mainThreadMs = Summarize(rows, 2), gcAllocatedBytes = Summarize(rows, 3),
                publisherMicroseconds = Summarize(rows, 4),
                totalReservedBytesLast = _lastReserved,
                videoUsedBytesLast = _lastVideo,
                graphicsDriverAllocatedBytesLast = _lastDriver,
                observations = rows
            };
        }

        /// <summary>Explicit, opt-in export only. Does not transmit or upload anything.</summary>
        public void ExportToJson(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath) || !Path.IsPathRooted(absolutePath))
                throw new ArgumentException("An absolute output path is required.", nameof(absolutePath));
            var report = GetReport();
            string directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(absolutePath, JsonUtility.ToJson(report, true));
        }
    }
}
