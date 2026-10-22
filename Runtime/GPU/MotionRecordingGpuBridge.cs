using System;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.Motion;
using UnityEngine;

namespace DanceFlow.Unity.GPU
{
    /// <summary>
    /// Optional seek-safe GPU mirror of the CPU-authoritative recording. Long captures
    /// upload overlapping <=4 MiB windows, never truncating or looping the source.
    /// The MotionSignalBus owns all source-time/beat-retiming decisions.
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
        [SerializeField, Tooltip("Opt-in bounded GPU recording windows; disabled by default.")]
        private bool enableFullRecordingGpuBuffer;
        private GraphicsBuffer _buffer;
        private MotionRecording _bufferedRecording;
        private int _uploadedStartRow = -1;
        private int _uploadedRowCount;
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
            try
            {
                // Exactly the same source pip used by the CPU motion signals; never another clock.
                double pip = signalBus.LastRecordingPip;
                RecordingGpuWindow window = RecordingGpuWindows.AtPip(
                    recording.SampleCount, recording.DimensionCount,
                    recording.Clock.StartPip, pip, MaximumGpuScalars);
                if (_buffer == null || _bufferedRecording != recording ||
                    _uploadedStartRow != window.StartRow || _uploadedRowCount != window.RowCount)
                    Upload(recording, window);
                Shader.SetGlobalVector(CursorId, new Vector4(window.RelativeRow,
                    window.Fraction, recording.DimensionCount, window.RowCount));
                Shader.SetGlobalBuffer(BufferId, _buffer);
                Shader.SetGlobalFloat(ValidId, 1);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is OverflowException)
            {
                Debug.LogWarning("GPU recording cursor or buffer rejected: " + ex.Message, this);
                SetInvalid();
            }
        }

        private void Upload(MotionRecording recording, RecordingGpuWindow window)
        {
            long count = (long)window.RowCount * recording.DimensionCount;
            if (count <= 0 || count > MaximumGpuScalars)
                throw new ArgumentOutOfRangeException(nameof(window), "GPU motion window exceeds fixed budget.");
            if (_buffer == null || _buffer.count != (int)count)
            {
                DestroyBuffer();
                _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)count, sizeof(float));
            }
            var values = new float[(int)count];
            int offset = checked(window.StartRow * recording.DimensionCount);
            for (int i = 0; i < values.Length; i++)
                values[i] = (float)recording.NativeValues[offset + i];
            _buffer.SetData(values);
            _bufferedRecording = recording;
            _uploadedStartRow = window.StartRow;
            _uploadedRowCount = window.RowCount;
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
            _uploadedStartRow = -1;
            _uploadedRowCount = 0;
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
