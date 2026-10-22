using System;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.Motion;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DanceFlow.Unity.Rigging
{
    /// <summary>
    /// Drives a real skinned mesh using a version-pinned rig profile and explicit bone mapping.
    /// Pose timing comes exclusively from DanceFlowClockBehaviour.Current. Captures never
    /// loop or invent a beat origin. Supports deterministic direct pose and AnimationScriptPlayable
    /// post-processing over an optional AnimatorControllerPlayable base input.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(9600)]
    public sealed class RigMotionPlayerBehaviour : MonoBehaviour
    {
        [Header("Authority / capture")]
        [SerializeField] private DanceFlowClockBehaviour songClock;
        [SerializeField] private MotionRecordingAsset recordingAsset;
        [SerializeField] private RigProfileAsset rigProfileAsset;
        [SerializeField] private RecordingTimeDomain timeDomain = RecordingTimeDomain.OriginalSourceSeconds;
        [SerializeField] private bool sameOriginalSourceClockApproved;
        [SerializeField] private double masterAnchorBeat;
        [SerializeField] private double recordingAnchorPip;
        [SerializeField] private double pipsPerMasterBeat = 64;
        [SerializeField] private bool unverifiedPhaseRetimingApproved;

        [Header("Reviewed skeleton / mapping")]
        [SerializeField] private Transform rigHierarchyRoot;
        [SerializeField] private SkinnedMeshRenderer skin;
        [SerializeField] private Animator animator;
        [SerializeField] private RigBoneBinding[] boneMappings = Array.Empty<RigBoneBinding>();
        [SerializeField] private bool mappingReviewedAndApproved;
        [SerializeField] private bool requireAllSkinnedBonesMapped;
        [SerializeField] private RigCoordinateSpace coordinateSpace = RigCoordinateSpace.UnityLocalAlreadyRetargeted;
        [SerializeField] private RigRootMotionMode rootMotion = RigRootMotionMode.InPlace;
        [SerializeField] private RigPlaybackBackend backend = RigPlaybackBackend.DirectTransforms;
        [SerializeField, Range(0, 1)] private float blendWeight = 1f;

        private RigProfile _profile;
        private RigPoseSampler _sampler;
        private RigBindingPlan[] _plans;
        private MotionRecording _recording;
        private RigTargetPose[] _poses;
        private double[] _physical;
        private bool _ownsRecording;
        private MotionRecordingAsset _ownedRecordingAsset;
        private PlayableGraph _graph;
        private AnimationScriptPlayable _playable;
        private NativeArray<TransformStreamHandle> _handles;
        private NativeArray<Vector3> _positions;
        private NativeArray<Quaternion> _rotations;
        private NativeArray<Vector3> _scales;
        private NativeArray<byte> _flags;
        private NativeArray<float> _weights;
        private bool _ready;
        public bool IsInitialized => _ready;
        public bool HasPoseThisFrame { get; private set; }
        public double LastAbsolutePip { get; private set; } = double.NaN;
        public string RigFingerprint => _profile == null ? null : _profile.Fingerprint;

        private void Start()
        {
            try { Initialize(); }
            catch (Exception e)
            {
                Debug.LogError("DanceFlow rig playback refused: " + e.Message, this);
                enabled = false;
            }
        }

        public void Initialize()
        {
            if (_ready) return;
            if (!mappingReviewedAndApproved)
                throw new InvalidOperationException("Review and approve this exact target skeleton and bone mapping before playback.");
            if (songClock == null || songClock.Transport == null || recordingAsset == null || rigProfileAsset == null)
                throw new InvalidOperationException("A live clock, validated source recording and imported rig profile are required.");
            if (timeDomain == RecordingTimeDomain.OriginalSourceSeconds && !sameOriginalSourceClockApproved)
                throw new InvalidOperationException("The original-source association must be approved.");
            if (timeDomain != RecordingTimeDomain.OriginalSourceSeconds && timeDomain != RecordingTimeDomain.ExplicitMusicalRetiming)
                throw new InvalidOperationException("Unknown recording time domain.");
            _profile = rigProfileAsset.OpenVerified();
            // Validate mapping *before* acquiring the large cache or creating Unity animation jobs.
            _plans = RigRetargetValidator.Build(_profile, boneMappings, rigHierarchyRoot,
                skin, requireAllSkinnedBonesMapped);
            if (backend == RigPlaybackBackend.AnimationPlayables && animator == null)
                throw new InvalidOperationException("Animation Playables requires an Animator bound to the selected skeleton.");
            if (backend == RigPlaybackBackend.DirectTransforms && animator != null && animator.runtimeAnimatorController != null)
                throw new InvalidOperationException("Use Animation Playables to blend against an Animator Controller, not direct transforms.");
            try
            {
                _recording = MotionRecordingCache.Acquire(recordingAsset);
                _ownedRecordingAsset = recordingAsset;
                _ownsRecording = true;
                _sampler = new RigPoseSampler(_profile, _recording);
                _physical = new double[_profile.ChannelCount];
                _poses = new RigTargetPose[_plans.Length];
                if (backend == RigPlaybackBackend.AnimationPlayables) CreatePlayable();
                _ready = true;
            }
            catch { Shutdown(); throw; }
        }

        private RecordingTimeMapping MakeTimeMapping() => timeDomain == RecordingTimeDomain.OriginalSourceSeconds
            ? RecordingTimeMapping.OriginalSourceSeconds()
            : RecordingTimeMapping.ExplicitBeatRetiming(masterAnchorBeat, recordingAnchorPip,
                pipsPerMasterBeat, unverifiedPhaseRetimingApproved);

        private void LateUpdate()
        {
            if (!_ready || songClock == null || songClock.Transport == null) return;
            var state = songClock.Current;
            double pip;
            try { pip = MakeTimeMapping().RecordingPip(state, _recording.Clock); }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is OverflowException)
            {
                HasPoseThisFrame = false;
                LastAbsolutePip = double.NaN;
                ClearOrRestore();
                return;
            }
            LastAbsolutePip = pip;
            HasPoseThisFrame = pip >= _sampler.StartPip && pip < _sampler.EndPipExclusive;
            if (HasPoseThisFrame)
            {
                try
                {
                    _sampler.SampleAtPipInto(pip, _physical);
                    for (int i=0;i<_plans.Length;i++)
                        _plans[i].CapturePose(_profile, _physical, coordinateSpace, rootMotion, ref _poses[i]);
                }
                catch (Exception error) when (error is FormatException || error is NotSupportedException || error is ArgumentException)
                {
                    Debug.LogWarning("Invalid rig pose; restoring neutral mapping: " + error.Message, this);
                    HasPoseThisFrame = false;
                }
            }
            if (backend == RigPlaybackBackend.DirectTransforms)
            {
                if (!HasPoseThisFrame) { RestoreRestPose(); return; }
                for (int i=0;i<_plans.Length;i++) ApplyDirect(_plans[i], _poses[i]);
            }
            else
            {
                RefreshJobValues();
                if (_graph.IsValid()) _graph.Evaluate(state.IsPlaying ? Time.deltaTime : 0f);
            }
        }

        private void ApplyDirect(RigBindingPlan plan, RigTargetPose pose)
        {
            var target = plan.Binding.target;
            float weight = Mathf.Clamp01(blendWeight * plan.Binding.maskWeight);
            if (weight <= 0) return;
            if ((pose.Flags & 1) != 0) target.localPosition = Vector3.Lerp(plan.RestPosition, pose.Position, weight);
            if ((pose.Flags & 2) != 0) target.localRotation = Quaternion.Slerp(plan.RestRotation, pose.Rotation, weight);
            if ((pose.Flags & 4) != 0) target.localScale = Vector3.Lerp(plan.RestScale, pose.Scale, weight);
        }

        private void CreatePlayable()
        {
            int count = _plans.Length;
            _handles = new NativeArray<TransformStreamHandle>(count, Allocator.Persistent);
            _positions = new NativeArray<Vector3>(count, Allocator.Persistent);
            _rotations = new NativeArray<Quaternion>(count, Allocator.Persistent);
            _scales = new NativeArray<Vector3>(count, Allocator.Persistent);
            _flags = new NativeArray<byte>(count, Allocator.Persistent);
            _weights = new NativeArray<float>(count, Allocator.Persistent);
            for (int i=0;i<count;i++) _handles[i] = animator.BindStreamTransform(_plans[i].Binding.target);
            var job = new RigMotionAnimationJob {
                Handles=_handles, Positions=_positions, Rotations=_rotations,
                Scales=_scales, ChannelFlags=_flags, Weights=_weights
            };
            _graph = PlayableGraph.Create("DanceFlow.RigMotion." + gameObject.name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            int inputs = animator.runtimeAnimatorController != null ? 1 : 0;
            _playable = AnimationScriptPlayable.Create(_graph, job, inputs);
            if (inputs == 1)
            {
                var baseController = AnimatorControllerPlayable.Create(_graph, animator.runtimeAnimatorController);
                _graph.Connect(baseController, 0, _playable, 0);
                _playable.SetInputWeight(0, 1);
            }
            var output = AnimationPlayableOutput.Create(_graph, "DanceFlowRig", animator);
            output.SetSourcePlayable(_playable);
            _graph.Play();
        }

        private void RefreshJobValues()
        {
            for (int i=0;i<_plans.Length;i++)
            {
                _flags[i] = HasPoseThisFrame ? _poses[i].Flags : (byte)0;
                _weights[i] = HasPoseThisFrame ? Mathf.Clamp01(blendWeight * _plans[i].Binding.maskWeight) : 0;
                if (HasPoseThisFrame)
                {
                    _positions[i] = _poses[i].Position;
                    _rotations[i] = _poses[i].Rotation;
                    _scales[i] = _poses[i].Scale;
                }
            }
        }

        private void ClearOrRestore()
        {
            if (backend == RigPlaybackBackend.DirectTransforms) RestoreRestPose();
            else if (_ready)
            {
                RefreshJobValues();
                if (_graph.IsValid()) _graph.Evaluate(0f);
            }
        }
        private void RestoreRestPose()
        {
            if (_plans == null) return;
            foreach (var p in _plans)
            {
                if (p?.Binding?.target == null) continue;
                p.Binding.target.localPosition=p.RestPosition;
                p.Binding.target.localRotation=p.RestRotation;
                p.Binding.target.localScale=p.RestScale;
            }
        }
        private void OnDisable() => Shutdown();
        private void OnDestroy() => Shutdown();
        private void Shutdown()
        {
            _ready = false;
            if (_graph.IsValid()) _graph.Destroy();
            if (_handles.IsCreated) _handles.Dispose();
            if (_positions.IsCreated) _positions.Dispose();
            if (_rotations.IsCreated) _rotations.Dispose();
            if (_scales.IsCreated) _scales.Dispose();
            if (_flags.IsCreated) _flags.Dispose();
            if (_weights.IsCreated) _weights.Dispose();
            if (backend == RigPlaybackBackend.DirectTransforms) RestoreRestPose();
            if (_ownsRecording) { MotionRecordingCache.Release(_ownedRecordingAsset); _ownsRecording=false; _ownedRecordingAsset=null; }
            _recording=null; _sampler=null; _profile=null;
            _plans=null; _poses=null; _physical=null;
            HasPoseThisFrame=false; LastAbsolutePip=double.NaN;
        }
    }
}
