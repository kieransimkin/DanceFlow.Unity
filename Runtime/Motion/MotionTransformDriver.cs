using System;
using System.Collections.Generic;
using DanceFlow.Unity.Clock;
using UnityEngine;

namespace DanceFlow.Unity.Motion
{
    /// <summary>
    /// First, deliberately typed and opt-in motion binding: three independent source channels
    /// become local XYZ offsets from a captured neutral pose. Never alters the recording clock.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MotionTransformDriver : MonoBehaviour
    {
        [SerializeField] private DanceFlowClockBehaviour songClock;
        [SerializeField] private MotionRecordingAsset recordingAsset;
        [Header("Explicit source association / beat retiming")]
        [SerializeField] private RecordingTimeDomain timeDomain = RecordingTimeDomain.OriginalSourceSeconds;
        [Tooltip("The source recording JSON does not prove that it belongs to this song. Confirm this association.")]
        [SerializeField] private bool sameSourceTimelineAcknowledged;
        [SerializeField] private double masterAnchorBeat;
        [SerializeField] private double recordingAnchorPip;
        [SerializeField] private double recordingPipsPerMasterBeat = 64;
        [SerializeField] private bool unverifiedPhaseRetimingApproved;
        [Header("Channels (blank = unmapped axis)")]
        [SerializeField] private string xChannel = "x";
        [SerializeField] private string yChannel = "y";
        [SerializeField] private string zChannel = "z";
        [SerializeField] private Vector3 metresPerUnit = Vector3.one;
        [SerializeField] private RecordingInterpolation interpolation = RecordingInterpolation.Linear;
        [SerializeField] private bool restoreNeutralPositionOnDisable = true;

        private MotionRecordingAsset _ownedAsset;
        private MotionRecording _recording;
        private int _x, _y, _z;
        private Vector3 _neutralPosition;
        private bool _warned;

        private void OnEnable()
        {
            _neutralPosition = transform.localPosition;
            TryAcquire();
        }

        private static int FindAxis(MotionRecording recording, string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            return recording.Descriptor.ChannelIndex(name);
        }

        private void TryAcquire()
        {
            Release();
            if (recordingAsset == null) return;
            try
            {
                var candidate = MotionRecordingCache.Acquire(recordingAsset);
                try
                {
                    int x = FindAxis(candidate, xChannel);
                    int y = FindAxis(candidate, yChannel);
                    int z = FindAxis(candidate, zChannel);
                    _ownedAsset = recordingAsset;
                    _recording = candidate;
                    _x = x; _y = y; _z = z;
                    _warned = false;
                }
                catch
                {
                    MotionRecordingCache.Release(recordingAsset);
                    throw;
                }
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException ||
                                        ex is InvalidOperationException || ex is KeyNotFoundException)
            {
                Debug.LogError("DanceFlow recording could not be loaded: " + ex.Message, this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || songClock == null || songClock.Transport == null || _recording == null)
                return;
            if (timeDomain != RecordingTimeDomain.OriginalSourceSeconds &&
                timeDomain != RecordingTimeDomain.ExplicitMusicalRetiming)
            {
                WarnOnce("Invalid motion mapping mode.");
                return;
            }
            if (timeDomain == RecordingTimeDomain.OriginalSourceSeconds && !sameSourceTimelineAcknowledged)
            {
                WarnOnce("Confirm that the recording and active song share source timestamps before binding motion.");
                return;
            }
            RecordingTimeMapping sync;
            try
            {
                sync = timeDomain == RecordingTimeDomain.OriginalSourceSeconds ?
                    RecordingTimeMapping.OriginalSourceSeconds() :
                    RecordingTimeMapping.ExplicitBeatRetiming(masterAnchorBeat, recordingAnchorPip,
                        recordingPipsPerMasterBeat, unverifiedPhaseRetimingApproved);
                // Consume the Phase 1 cached state, not an independent per-component time accumulator.
                double pip = sync.RecordingPip(songClock.Current, _recording.Clock);
                double xv = 0, yv = 0, zv = 0;
                // An exhausted capture returns to its neutral state; it never implicitly loops.
                bool valid = SampleAxis(pip, _x, out xv) && SampleAxis(pip, _y, out yv) &&
                             SampleAxis(pip, _z, out zv);
                transform.localPosition = !valid ? _neutralPosition : _neutralPosition +
                    new Vector3((float)xv * metresPerUnit.x, (float)yv * metresPerUnit.y,
                                (float)zv * metresPerUnit.z);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
            {
                WarnOnce(ex.Message);
            }
        }

        private bool SampleAxis(double pip, int axis, out double value)
        {
            if (axis < 0) { value = 0; return true; }
            return _recording.TrySampleAtPip(pip, axis, interpolation, out value);
        }

        private void WarnOnce(string message)
        {
            if (_warned) return;
            Debug.LogWarning("DanceFlow motion: " + message, this);
            _warned = true;
        }

        private void Release()
        {
            if (_ownedAsset != null) MotionRecordingCache.Release(_ownedAsset);
            _ownedAsset = null;
            _recording = null;
        }

        private void OnDisable()
        {
            Release();
            if (restoreNeutralPositionOnDisable && Application.isPlaying)
                transform.localPosition = _neutralPosition;
        }
    }
}
