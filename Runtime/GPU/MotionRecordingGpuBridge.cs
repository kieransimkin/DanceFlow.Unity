using System;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.Motion;
using UnityEngine;

namespace DanceFlow.Unity.GPU
{
    /// <summary>
    /// Optional float32 GPU mirror of a validated N-dimensional recording (sampled on demand).
    /// The CPU recording remains the float64 authority. The GPU mirror is limited to a bounded
    /// number of samples, with an explicit cursor; no hidden phase retiming or looping.
    /// For multiple songs, use material-local buffers in a later renderer adapter.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(9100)]
    public sealed class MotionRecordingGpuBridge : MonoBehaviour
    {
        public const int MaximumGpuScalars = 1024 * 1024;
        private static readonly int BufferId = Shader.PropertyToID("_DanceFlow_RecordingSamples");
        private static readonly int ValidId = Shader.PropertyToID("_DanceFlow_RecordingBufferValid");
        private static readonly int CursorId = Shader.PropertyToID("_DanceFlow_RecordingCursor");
        [SerializeField] private MotionSignalBus signalBus;
        [SerializeField] private DanceFlowClockBehaviour songClock;
        [SerializeField] private bool enableFullRecordingGpuBuffer;
        [Header("GPU cursor mapping (must match MotionSignalBus)")]
        [SerializeField] private RecordingTimeDomain timeDomain = RecordingTimeDomain.OriginalSourceSeconds;
        [SerializeField] private bool sameSourceTimelineAcknowledged;
        [SerializeField] private double masterAnchorBeat;
        [SerializeField] private double recordingAnchorPip;
        [SerializeField] private double recordingPipsPerMasterBeat = 64;
        [SerializeField] private bool unverifiedPhaseRetimingApproved;
        private GraphicsBuffer _buffer;
        private MotionRecording _bufferedRecording;
        private static MotionRecordingGpuBridge _active;

        private void OnEnable()
        {
            if (_active != null && _active != this)
            {
                Debug.LogError("Only one global recording GPU buffer owner is allowed.", this);
                enabled = false;
                return;
            }
            _active = this;
        }

        private void LateUpdate()
        {
            if (_active != this || !Application.isPlaying || signalBus == null || songClock == null) return;
            if (!enableFullRecordingGpuBuffer || !SystemInfo.supportsComputeShaders ||
                !signalBus.IsRecordedSample || signalBus.Recording == null)
            {
                SetInvalid();
                return;
            }
            MotionRecording recording = signalBus.Recording;
            long values = (long)recording.SampleCount * recording.DimensionCount;
            if (values > MaximumGpuScalars) { SetInvalid(); return; }
            if (_buffer == null || _bufferedRecording != recording) Upload(recording);
            try
            {
                var mapping = timeDomain == RecordingTimeDomain.OriginalSourceSeconds
                    ? RecordingTimeMapping.OriginalSourceSeconds()
                    : RecordingTimeMapping.ExplicitBeatRetiming(masterAnchorBeat, recordingAnchorPip,
                          recordingPipsPerMasterBeat, unverifiedPhaseRetimingApproved);
                if (timeDomain == RecordingTimeDomain.OriginalSourceSeconds && !sameSourceTimelineAcknowledged)
                {
                    SetInvalid(); return;
                }
                double pip = mapping.RecordingPip(songClock.Current, recording.Clock);
                double row = pip - recording.Clock.StartPip;
                if (row < 0 || row >= recording.SampleCount) { SetInvalid(); return; }
                float index = (float)Math.Floor(row);
                float fraction = (float)(row - Math.Floor(row));
                Shader.SetGlobalVector(CursorId, new Vector4(index, fraction,
                    recording.DimensionCount, recording.SampleCount));
                Shader.SetGlobalBuffer(BufferId, _buffer);
                Shader.SetGlobalFloat(ValidId, 1);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is OverflowException)
            {
                Debug.LogWarning("GPU recording cursor is invalid: " + ex.Message, this);
                SetInvalid();
            }
        }

        private void Upload(MotionRecording recording)
        {
            DestroyBuffer();
            long count = (long)recording.SampleCount * recording.DimensionCount;
            if (count <= 0 || count > MaximumGpuScalars) return;
            var values = new float[(int)count];
            for (int i = 0; i < values.Length; i++) values[i] = (float)recording.NativeValues[i];
            _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, values.Length, sizeof(float));
            _buffer.SetData(values);
            _bufferedRecording = recording;
        }

        private static void SetInvalid()
        {
            Shader.SetGlobalFloat(ValidId, 0);
            Shader.SetGlobalVector(CursorId, Vector4.zero);
        }
        private void DestroyBuffer()
        {
            if (_buffer != null) { _buffer.Dispose(); _buffer = null; }
            _bufferedRecording = null;
        }
        private void OnDisable()
        {
            if (_active == this)
            {
                SetInvalid();
                Shader.SetGlobalBuffer(BufferId, (GraphicsBuffer)null);
                _active = null;
            }
            DestroyBuffer();
        }
    }
}
