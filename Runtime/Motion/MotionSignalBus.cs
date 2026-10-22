using System;
using DanceFlow.Unity.Clock;
using UnityEngine;

namespace DanceFlow.Unity.Motion
{
    public enum MotionSignalSource { SyntheticPreview = 0, ImportedRecording = 1 }

    /// <summary>
    /// Four semantic channel slots driving an entire single-song scene. Samples the existing
    /// DanceRudiments recording exactly once per rendered frame and publishes uniform values
    /// to shaders. Importantly: synthetic preview is never advertised as a matched recording.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(9000)]
    public sealed class MotionSignalBus : MonoBehaviour
    {
        private static readonly int ValuesId = Shader.PropertyToID("_DanceFlow_MotionA");
        private static readonly int MagnitudesId = Shader.PropertyToID("_DanceFlow_MotionB");
        private static readonly int AvailableId = Shader.PropertyToID("_DanceFlow_MotionAvailable");
        private static readonly int RecordedId = Shader.PropertyToID("_DanceFlow_MotionIsRecording");
        private static MotionSignalBus _active;

        [SerializeField] private DanceFlowClockBehaviour songClock;
        [SerializeField] private MotionSignalSource source = MotionSignalSource.SyntheticPreview;
        [SerializeField] private MotionRecordingAsset recordingAsset;
        [Header("Explicit source association / beat retiming")]
        [SerializeField] private RecordingTimeDomain timeDomain = RecordingTimeDomain.OriginalSourceSeconds;
        [SerializeField, Tooltip("The recording JSON does not prove it belongs to this song.")]
        private bool sameSourceTimelineAcknowledged;
        [SerializeField] private double masterAnchorBeat;
        [SerializeField] private double recordingAnchorPip;
        [SerializeField] private double recordingPipsPerMasterBeat = 64;
        [SerializeField] private bool unverifiedPhaseRetimingApproved;
        [Header("The four scene-wide shader channels")]
        [SerializeField] private string windChannel = "x";
        [SerializeField] private string gustChannel = "y";
        [SerializeField] private string paletteChannel = "z";
        [SerializeField] private string sparkleChannel = "x";
        [SerializeField] private RecordingInterpolation interpolation = RecordingInterpolation.Linear;

        private MotionRecording _recording;
        private MotionRecordingAsset _ownedAsset;
        private int[] _axis = new int[4];
        private readonly double[] _preview = new double[4];
        private readonly float[] _values = new float[4];
        private bool _warned;
        public static MotionSignalBus Active => _active;
        public bool IsRecordedSample { get; private set; }
        public bool HasSample { get; private set; }
        public MotionRecording Recording => _recording;
        /// <summary>Last source recording pip evaluated by the authoritative CPU clock; NaN if absent.</summary>
        public double LastRecordingPip { get; private set; } = double.NaN;
        public MotionSignalSource Source => source;
        public float Value(int slot) => slot >= 0 && slot < 4 ? _values[slot] : throw new ArgumentOutOfRangeException(nameof(slot));
        public string SlotName(int slot) => slot switch
        {
            0 => windChannel, 1 => gustChannel, 2 => paletteChannel, 3 => sparkleChannel,
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };

        public void Configure(DanceFlowClockBehaviour clock, MotionRecordingAsset asset = null)
        {
            songClock = clock;
            if (asset != null)
            {
                recordingAsset = asset;
                source = MotionSignalSource.ImportedRecording;
            }
            if (isActiveAndEnabled) Acquire();
        }

        /// <summary>
        /// Explicitly bind four semantic channels to a finite recording. A matching source
        /// clock must be acknowledged; unverified musical phase cannot be guessed.
        /// </summary>
        public void UseRecording(MotionRecordingAsset asset, string[] channels,
            RecordingTimeDomain domain, bool approvedSameSource, double masterBeat,
            double recordingPip, double pipsPerMasterBeat, bool approvedUnverifiedPhase)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (channels == null || channels.Length != 4)
                throw new ArgumentException("Four channel slots must be explicitly named.", nameof(channels));
            var descriptor = asset.Descriptor;
            bool any = false;
            foreach (string channel in channels)
                if (channel != null && descriptor.TryChannelIndex(channel, out _)) any = true;
            if (!any) throw new ArgumentException("None of the requested channels exists in the recording.", nameof(channels));
            if (domain == RecordingTimeDomain.OriginalSourceSeconds && !approvedSameSource)
                throw new InvalidOperationException("The recording and master require an explicitly approved source-clock association.");
            if (domain == RecordingTimeDomain.ExplicitMusicalRetiming)
                RecordingTimeMapping.ExplicitBeatRetiming(masterBeat, recordingPip, pipsPerMasterBeat, approvedUnverifiedPhase)
                    .RecordingPip(new BeatState(0, 0, 0, 120, 4, 0, 0, 0, false), descriptor.Clock);
            else if (domain != RecordingTimeDomain.OriginalSourceSeconds)
                throw new ArgumentOutOfRangeException(nameof(domain));
            windChannel = channels[0]; gustChannel = channels[1];
            paletteChannel = channels[2]; sparkleChannel = channels[3];
            sameSourceTimelineAcknowledged = approvedSameSource;
            timeDomain = domain;
            masterAnchorBeat = masterBeat;
            recordingAnchorPip = recordingPip;
            recordingPipsPerMasterBeat = pipsPerMasterBeat;
            unverifiedPhaseRetimingApproved = approvedUnverifiedPhase;
            recordingAsset = asset;
            source = MotionSignalSource.ImportedRecording;
            _warned = false;
            LastRecordingPip = double.NaN;
            if (isActiveAndEnabled) Acquire();
        }

        public void UseSyntheticPreview()
        {
            source = MotionSignalSource.SyntheticPreview;
            recordingAsset = null;
            Release();
            HasSample = false;
            IsRecordedSample = false;
            LastRecordingPip = double.NaN;
            _warned = false;
        }

        private void OnEnable()
        {
            if (_active != null && _active != this)
            {
                Debug.LogError("Only one global DanceFlow MotionSignalBus may own the global shader slots. Use local bindings for other songs.", this);
                enabled = false;
                return;
            }
            _active = this;
            Acquire();
        }

        private void Acquire()
        {
            Release();
            if (source != MotionSignalSource.ImportedRecording) return;
            if (recordingAsset == null)
            {
                WarnOnce("ImportedRecording selected but there is no recording asset; motion output stays silent.");
                return;
            }
            try
            {
                var candidate = MotionRecordingCache.Acquire(recordingAsset);
                try
                {
                    string[] channels = { windChannel, gustChannel, paletteChannel, sparkleChannel };
                    for (int i = 0; i < 4; i++)
                        _axis[i] = !string.IsNullOrEmpty(channels[i]) && candidate.Descriptor.TryChannelIndex(channels[i], out int index) ? index : -1;
                    if (_axis[0] < 0 && _axis[1] < 0 && _axis[2] < 0 && _axis[3] < 0)
                        WarnOnce("None of the selected channel names exist in this recording; configure the four slots in the Inspector.");
                    _recording = candidate;
                    _ownedAsset = recordingAsset;
                    _warned = false;
                }
                catch { MotionRecordingCache.Release(recordingAsset); throw; }
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
            {
                WarnOnce("Cannot open recording: " + ex.Message);
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || songClock == null || songClock.Transport == null) return;
            BeatState state = songClock.Current; // single Phase 1 source of musical truth
            HasSample = false;
            IsRecordedSample = false;
            LastRecordingPip = double.NaN;
            for (int i = 0; i < _values.Length; i++) _values[i] = 0;
            if (source == MotionSignalSource.SyntheticPreview)
            {
                MotionSignalMath.SyntheticPreview(state.AbsoluteBeat, _preview);
                for (int i = 0; i < 4; i++) _values[i] = (float)_preview[i];
                HasSample = true;
            }
            else if (_recording != null)
            {
                if (timeDomain == RecordingTimeDomain.OriginalSourceSeconds && !sameSourceTimelineAcknowledged)
                    WarnOnce("Original-source recording selected; confirm the song/recording share original source timestamps in the Inspector.");
                else
                {
                    try
                    {
                        var mapping = timeDomain == RecordingTimeDomain.OriginalSourceSeconds
                            ? RecordingTimeMapping.OriginalSourceSeconds()
                            : RecordingTimeMapping.ExplicitBeatRetiming(masterAnchorBeat, recordingAnchorPip,
                                  recordingPipsPerMasterBeat, unverifiedPhaseRetimingApproved);
                        double pip = mapping.RecordingPip(state, _recording.Clock);
                        // Only publish an authoritative cursor while inside the finite capture.
                        if (pip >= _recording.Clock.StartPip && pip < _recording.EndPipExclusive)
                            LastRecordingPip = pip;
                        // The recording is finite: never wrap, repeat, or extrapolate.
                        for (int i = 0; i < 4; i++)
                            _values[i] = _axis[i] < 0 ? 0 :
                                _recording.TrySampleAtPip(pip, _axis[i], interpolation, out double value)
                                    ? (float)value : 0;
                        HasSample = pip >= _recording.Clock.StartPip && pip < _recording.EndPipExclusive;
                        IsRecordedSample = HasSample;
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
                    {
                        WarnOnce("Motion mapping rejected: " + ex.Message);
                    }
                }
            }
            PublishGlobals();
        }

        private void PublishGlobals()
        {
            Shader.SetGlobalVector(ValuesId, new Vector4(_values[0], _values[1], _values[2], _values[3]));
            Shader.SetGlobalVector(MagnitudesId,
                new Vector4(Mathf.Abs(_values[0]), Mathf.Abs(_values[1]), Mathf.Abs(_values[2]), Mathf.Abs(_values[3])));
            Shader.SetGlobalFloat(AvailableId, HasSample ? 1 : 0);
            Shader.SetGlobalFloat(RecordedId, IsRecordedSample ? 1 : 0);
        }

        private void WarnOnce(string message)
        {
            if (_warned) return;
            _warned = true;
            Debug.LogWarning("DanceFlow motion: " + message, this);
        }
        private void Release()
        {
            if (_ownedAsset == null) return;
            MotionRecordingCache.Release(_ownedAsset);
            _ownedAsset = null;
            _recording = null;
        }
        private void OnDisable()
        {
            Release();
            if (_active != this) return;
            _active = null;
            Array.Clear(_values, 0, _values.Length);
            HasSample = IsRecordedSample = false;
            LastRecordingPip = double.NaN;
            PublishGlobals();
        }
    }
}
