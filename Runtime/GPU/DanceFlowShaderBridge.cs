using System;
using DanceFlow.Unity.Clock;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace DanceFlow.Unity.GPU
{
    /// <summary>
    /// Publishes the authoritative DanceFlow beat state to the *global* Unity shader namespace.
    /// One active bridge owns globals in a process; other clocks require explicit local buffers
    /// (a later phase). Does not schedule audio, accumulate time or dispatch musical events.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class DanceFlowShaderBridge : MonoBehaviour
    {
        private static DanceFlowShaderBridge _active;
        private static readonly ProfilerMarker EvaluateMarker = new ProfilerMarker("DanceFlow.Clock.RenderEvaluate");
        private static readonly ProfilerMarker PackMarker = new ProfilerMarker("DanceFlow.GPU.EncodeState");
        private static readonly ProfilerMarker UploadMarker = new ProfilerMarker("DanceFlow.GPU.PublishGlobals");


        [SerializeField] private DanceFlowClockBehaviour clock;
        [SerializeField] private ShaderBeatDomain beatDomain = ShaderBeatDomain.Source;
        [Header("Editor-only silent preview (off by default)")]
        [SerializeField] private bool previewOutsidePlayMode;
        [SerializeField, Min(1)] private float previewBpm = 120f;
        [SerializeField] private float previewSourceSeconds;
        [SerializeField] private int previewBeatsPerBar = 4;
        [SerializeField] private float previewBarOriginBeat;
        [Header("Optional performance instrumentation")]
        [SerializeField] private bool measurePublisherCpuTime;
        private double _publisherTotalMicroseconds;
        private double _publisherPeakMicroseconds;
        private double _publisherLastMicroseconds;
        private long _publisherMeasuredCount;
        private long _publishedStates;


        private int _lastFrame = -1;
        private bool _explicitManualOverride;
        private ShaderClockSample _lastSample;
        private bool _hasSample;

        public static DanceFlowShaderBridge Active => _active;
        public void SetClock(DanceFlowClockBehaviour source) { clock = source; _lastFrame = -1; }
        public bool HasPublishedState => _hasSample;
        public long PublishedStates => _publishedStates;
        public long PublisherMeasuredCount => _publisherMeasuredCount;
        public double PublisherLastMicroseconds => _publisherLastMicroseconds;
        public double PublisherPeakMicroseconds => _publisherPeakMicroseconds;
        public double PublisherAverageMicroseconds => _publisherMeasuredCount > 0 ? _publisherTotalMicroseconds / _publisherMeasuredCount : 0;

        public ShaderClockSample LastSample => _lastSample;

        /// <summary>Opt-in benchmark timing; publication otherwise runs without a Stopwatch.</summary>
        public void SetBenchmarkPublisherTiming(bool enabled)
        {
            measurePublisherCpuTime = enabled;
            _publisherTotalMicroseconds = _publisherPeakMicroseconds = _publisherLastMicroseconds = 0;
            _publisherMeasuredCount = 0;
        }


        private static readonly int BpmId = Shader.PropertyToID("_DanceFlow_BPM");
        private static readonly int BeatPhaseId = Shader.PropertyToID("_DanceFlow_BeatPhase");
        private static readonly int BarPhaseId = Shader.PropertyToID("_DanceFlow_BarPhase");
        private static readonly int TickPhaseId = Shader.PropertyToID("_DanceFlow_TickPhase");
        private static readonly int PipPhaseId = Shader.PropertyToID("_DanceFlow_PipPhase");
        private static readonly int BeatPulseId = Shader.PropertyToID("_DanceFlow_BeatPulse");
        private static readonly int DownbeatPulseId = Shader.PropertyToID("_DanceFlow_DownbeatPulse");
        private static readonly int BeatSinId = Shader.PropertyToID("_DanceFlow_BeatSin");
        private static readonly int BeatCosId = Shader.PropertyToID("_DanceFlow_BeatCos");
        private static readonly int MeterId = Shader.PropertyToID("_DanceFlow_Meter");
        private static readonly int PlayingId = Shader.PropertyToID("_DanceFlow_IsPlaying");
        private static readonly int ApiVersionId = Shader.PropertyToID("_DanceFlow_ShaderApiVersion");
        private static readonly int BeatIndexHiLoId = Shader.PropertyToID("_DanceFlow_BeatIndexHiLo");
        private static readonly int SecondsHiLoId = Shader.PropertyToID("_DanceFlow_SourceSecondsHiLo");
        private static readonly int TransportHiLoId = Shader.PropertyToID("_DanceFlow_TransportGenerationHiLo");
        private static readonly int BeatRemainderId = Shader.PropertyToID("_DanceFlow_BeatRemainder256");

        private void OnEnable()
        {
            if (_active != null && _active != this)
            {
                Debug.LogError("Only one active global DanceFlowShaderBridge is permitted. Disable the duplicate.", this);
                enabled = false;
                return;
            }
            _active = this;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            Camera.onPreCull += OnBuiltinPreCull;
            _lastFrame = -1;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Camera.onPreCull -= OnBuiltinPreCull;
            if (_active != this) return;
            _active = null;
            _hasSample = false;
            _explicitManualOverride = false;
            ClearGlobals();
        }

        private void LateUpdate() => PublishForFrame();

        private void OnBeginCamera(ScriptableRenderContext context, Camera renderingCamera)
        {
            if (GraphicsSettings.currentRenderPipeline != null) PublishForFrame();
        }

        private void OnBuiltinPreCull(Camera renderingCamera)
        {
            if (GraphicsSettings.currentRenderPipeline == null) PublishForFrame();
        }

        private void PublishForFrame()
        {
            if (_active != this || _explicitManualOverride || _lastFrame == Time.frameCount) return;
            if (Application.isPlaying)
            {
                if (clock == null || clock.Transport == null) return;
                // Re-evaluate at render time; the Phase 1 scheduler remains solely owned by
                // the clock component. Multiple cameras in one frame share the SAME snapshot.
                BeatState state;
                using (EvaluateMarker.Auto()) state = clock.Transport.Evaluate();
                ShaderClockSample sample;
                using (PackMarker.Auto()) sample = ShaderClockMath.Pack(state, beatDomain);
                PublishValues(sample);
            }
            else if (previewOutsidePlayMode)
            {
                var map = new FixedBeatMap(Math.Max(1, previewBpm));
                var p = map.Evaluate(previewSourceSeconds);
                var state = new BeatState(previewSourceSeconds, p.Beat, p.Beat, p.Bpm,
                    Math.Max(1, Math.Min(64, previewBeatsPerBar)), previewBarOriginBeat, 0, 0, false);
                PublishValues(ShaderClockMath.Pack(state, beatDomain));
            }
            _lastFrame = Time.frameCount;
        }

        /// <summary>
        /// Explicit seek-safe publishing for offline rendering and Editor scrubbing. The bridge
        /// retains this snapshot until ReleaseManualOverride() is called. No audio is altered.
        /// </summary>
        public void Publish(BeatState state)
        {
            if (_active != this) throw new InvalidOperationException("Bridge must be enabled and be the active global owner.");
            _explicitManualOverride = true;
            PublishValues(ShaderClockMath.Pack(state, beatDomain));
        }

        public void PublishAtSourceSeconds(double seconds)
        {
            if (clock == null || clock.Transport == null)
                throw new InvalidOperationException("A running DanceFlowClockBehaviour is needed to preview a source-time map.");
            Publish(clock.Transport.EvaluateSourceSeconds(seconds));
        }

        public void ReleaseManualOverride()
        {
            _explicitManualOverride = false;
            _lastFrame = -1;
        }

        /// <summary>Public data publishing for deterministic renderers, no dependence on GameObject clock.</summary>
        private void PublishValues(ShaderClockSample s)
        {
            long startTicks = measurePublisherCpuTime ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            using (UploadMarker.Auto())
            {
            Shader.SetGlobalFloat(BpmId, s.Bpm);
            Shader.SetGlobalFloat(BeatPhaseId, s.BeatPhase);
            Shader.SetGlobalFloat(BarPhaseId, s.BarPhase);
            Shader.SetGlobalFloat(TickPhaseId, s.TickPhase);
            Shader.SetGlobalFloat(PipPhaseId, s.PipPhase);
            Shader.SetGlobalFloat(BeatPulseId, s.BeatPulse);
            Shader.SetGlobalFloat(DownbeatPulseId, s.DownbeatPulse);
            Shader.SetGlobalFloat(BeatSinId, s.BeatSin);
            Shader.SetGlobalFloat(BeatCosId, s.BeatCos);
            Shader.SetGlobalFloat(MeterId, s.BeatsPerBar);
            Shader.SetGlobalFloat(PlayingId, s.IsPlaying);
            Shader.SetGlobalVector(BeatIndexHiLoId, new Vector4(s.BeatIndexHigh, s.BeatIndexLow, 0, 0));
            Shader.SetGlobalVector(SecondsHiLoId, new Vector4(s.SourceSecondsHigh, s.SourceSecondsLow, 0, 0));
            Shader.SetGlobalVector(TransportHiLoId, new Vector4(s.TransportGenerationHigh, s.TransportGenerationLow, 0, 0));
            Shader.SetGlobalFloat(BeatRemainderId, s.BeatRemainder256);
            // Set version last so consumers never treat cleared globals as a valid clock.
            Shader.SetGlobalFloat(ApiVersionId, ShaderClockSample.ShaderApiVersion);
            }
            _lastSample = s;
            _hasSample = true;
            _publishedStates++;
            if (measurePublisherCpuTime)
            {
                double micros = (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) *
                                (1000000.0 / System.Diagnostics.Stopwatch.Frequency);
                _publisherLastMicroseconds = micros;
                _publisherTotalMicroseconds += micros;
                _publisherPeakMicroseconds = Math.Max(_publisherPeakMicroseconds, micros);
                _publisherMeasuredCount++;
            }
        }

        private static void ClearGlobals()
        {
            Shader.SetGlobalFloat(ApiVersionId, 0);
            Shader.SetGlobalFloat(BpmId, 0);
            Shader.SetGlobalFloat(BeatPhaseId, 0);
            Shader.SetGlobalFloat(BarPhaseId, 0);
            Shader.SetGlobalFloat(TickPhaseId, 0);
            Shader.SetGlobalFloat(PipPhaseId, 0);
            Shader.SetGlobalFloat(BeatPulseId, 0);
            Shader.SetGlobalFloat(DownbeatPulseId, 0);
            Shader.SetGlobalFloat(BeatSinId, 0);
            Shader.SetGlobalFloat(BeatCosId, 0);
            Shader.SetGlobalFloat(MeterId, 0);
            Shader.SetGlobalFloat(PlayingId, 0);
            Shader.SetGlobalVector(BeatIndexHiLoId, Vector4.zero);
            Shader.SetGlobalVector(SecondsHiLoId, Vector4.zero);
            Shader.SetGlobalVector(TransportHiLoId, Vector4.zero);
            Shader.SetGlobalFloat(BeatRemainderId, 0);
        }
    }
}
