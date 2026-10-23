using System;
using System.Collections.Generic;
using DanceFlow.Unity.Clock;
using UnityEngine;

namespace DanceFlow.Unity.WebGLDemo
{
    /// <summary>
    /// Property-specific bindings: each output chooses an actual analysed audio band,
    /// one exact DanceRudiments compiled pattern, axis and modulation gain.
    /// The patterns are procedural CATALOGUE RUDIMENTS, not verified live-matched motion.
    /// No extra clock or free-running oscillator is introduced: all samples use
    /// the same source seconds and absolute 64-pip musical position as the song.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(9400)]
    public sealed class KandiBoomMotionMatrix : MonoBehaviour
    {
        private static readonly int MotionA = Shader.PropertyToID("_DanceFlow_MotionA");
        private static readonly int MotionB = Shader.PropertyToID("_DanceFlow_MotionB");
        private static readonly int SceneMotion = Shader.PropertyToID("_DanceFlow_SceneMotion");
        private static readonly int Available = Shader.PropertyToID("_DanceFlow_MotionAvailable");
        private static readonly int MatrixFlag = Shader.PropertyToID("_DanceFlow_WebGLMotionMatrix");
        private static readonly int IsRecording = Shader.PropertyToID("_DanceFlow_MotionIsRecording");
        private static readonly string[] Names = {
            "Grass wind", "Tree sway", "Terrain ripples", "Foliage palette",
            "Star shimmer", "Helix warp", "Kandi hips", "Left arm",
            "Right arm", "Kandi head", "Kandi legs", "Fill light"
        };

        [Serializable]
        private sealed class Binding
        {
            public int band;
            public int pattern;
            public int axis;
            public float gain;
            public Binding(int audioBand, int sourcePattern, float amplitude)
            { band = audioBand; pattern = sourcePattern; axis = -1; gain = amplitude; }
        }

        private readonly Binding[] _bindings = {
            new Binding(0, 4, 0.85f),   // Grass sub-energy x LFO breathe
            new Binding(2, 0, 0.65f),   // Trees low-mid x groove_b_played
            new Binding(1, 6, 1.05f),   // Terrain bass x Euclidean rhythm
            new Binding(3, 2, 0.85f),   // Palette mid x LFO morph
            new Binding(4, 5, 1.2f),    // Star shimmer highs x LFO flower
            new Binding(5, 7, 1.0f),    // Warp onsets x six stroke rudiment
            new Binding(0, 0, 0.95f),   // Hips sub x groove_b_played
            new Binding(5, 1, 0.9f),    // Left arm onsets x groove_b_grid
            new Binding(3, 3, 0.9f),    // Right arm mids x groove_a_played
            new Binding(4, 8, 0.75f),   // Head high x overshoot
            new Binding(1, 9, 0.8f),    // Knees bass x half-time rhythm
            new Binding(7, 3, 0.75f),   // Light energy x groove_a_played
        };
        private readonly float[] _outputs = new float[12];
        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly Dictionary<string, Quaternion> _rest = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
        [SerializeField] private KandiBoomSongPlayer songs;
        [SerializeField] private DanceFlowClockBehaviour clock;
        [SerializeField] private Transform characterRoot;
        [SerializeField] private Light fillLight;
        private float _restLight;

        public void Configure(KandiBoomSongPlayer songPlayer, DanceFlowClockBehaviour sourceClock,
                              Transform actualKandiRoot, Light localFillLight)
        { songs = songPlayer; clock = sourceClock; characterRoot = actualKandiRoot; fillLight = localFillLight; }

        public int BindingCount => _bindings.Length;
        public string Name(int index) => Names[index];
        public string BandName(int index) => KandiBoomSongPlayer.BandNames[_bindings[index].band];
        public string PatternName(int index) => songs == null ? "" : songs.PatternName(_bindings[index].pattern);
        public float Gain(int index) => _bindings[index].gain;
        public float Output(int index) => _outputs[index];
        public void CycleBand(int index) => _bindings[index].band = (_bindings[index].band + 1) % KandiBoomSongPlayer.BandNames.Length;
        public void CyclePattern(int index)
        {
            if (songs == null || songs.PatternCount == 0) return;
            var b = _bindings[index];
            b.pattern = (b.pattern + 1) % songs.PatternCount;
            b.axis = -1; // choose the official pattern's most active axis after switching
        }
        public void CycleAxis(int index)
        {
            var b = _bindings[index];
            b.axis = ((b.axis < 0 && songs != null ? songs.PrimaryAxis(b.pattern) : b.axis) + 1) % 3;
        }
        public string AxisName(int index)
        {
            var b = _bindings[index];
            int axis = b.axis >= 0 ? b.axis : songs == null ? 0 : songs.PrimaryAxis(b.pattern);
            return axis == 0 ? "X" : axis == 1 ? "Y" : "Z";
        }
        public void ChangeGain(int index, float delta) => _bindings[index].gain = Mathf.Clamp(_bindings[index].gain + delta, 0, 2.5f);

        private void Start()
        {
            if (characterRoot == null || songs == null || clock == null)
            {
                Debug.LogError("Kandi motion matrix requires song player, clock and authentic FBX root.", this);
                enabled = false;
                return;
            }
            foreach (Transform bone in characterRoot.GetComponentsInChildren<Transform>(true))
            {
                if (_bones.ContainsKey(bone.name)) continue;
                _bones.Add(bone.name, bone);
                _rest.Add(bone.name, bone.localRotation);
            }
            foreach (string name in new[] { "Hips", "Spine", "Head", "LeftArm", "RightArm", "LeftForeArm",
                                            "RightForeArm", "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg" })
                if (!_bones.ContainsKey(name))
                {
                    Debug.LogError("Verified Kandi Boom rig is missing bone " + name, this);
                    enabled = false; return;
                }
            if (fillLight != null) _restLight = fillLight.intensity;
        }

        private void LateUpdate()
        {
            if (clock == null || songs == null || clock.Transport == null) return;
            bool audioReady = songs.Ready && songs.HasStarted;
            double sourceSeconds = clock.Current.SourceSeconds;
            double absoluteBeat = clock.Current.AbsoluteBeat;
            for (int i = 0; i < _outputs.Length; i++)
            {
                _outputs[i] = 0;
                if (!audioReady) continue;
                Binding b = _bindings[i];
                float energy = songs.Signal(b.band, sourceSeconds);
                int axis = b.axis < 0 ? songs.PrimaryAxis(b.pattern) : b.axis;
                float officialRudiment = songs.PatternValue(b.pattern, axis, absoluteBeat);
                // Distinct actual audio envelope modulates the native, exact catalogue
                // pattern sample; absent audio produces NO synthetic 'matched' movement.
                _outputs[i] = Mathf.Clamp(officialRudiment * (0.18f + 0.82f * energy) * b.gain, -1, 1);
            }
            Shader.SetGlobalVector(MotionA, new Vector4(_outputs[0], _outputs[1], _outputs[3], _outputs[4]));
            Shader.SetGlobalVector(MotionB, new Vector4(Mathf.Abs(_outputs[0]), Mathf.Abs(_outputs[1]),
                Mathf.Abs(_outputs[3]), Mathf.Abs(_outputs[4])));
            Shader.SetGlobalVector(SceneMotion, new Vector4(_outputs[2], _outputs[5], _outputs[11], 0));
            Shader.SetGlobalFloat(Available, audioReady ? 1 : 0);
            Shader.SetGlobalFloat(MatrixFlag, 1);
            Shader.SetGlobalFloat(IsRecording, 0); // no unverified capture is ever claimed
            ApplyKandiPose();
            if (fillLight != null) fillLight.intensity = _restLight * (1 + 0.32f * Mathf.Abs(_outputs[11]));
        }

        private void ApplyKandiPose()
        {
            // Authored angles are deliberately restrained to avoid self-intersecting limbs.
            Rotate("Hips", Vector3.up, _outputs[6] * 7f);
            Rotate("Spine", Vector3.forward, _outputs[6] * 3.5f);
            Rotate("LeftArm", Vector3.forward, _outputs[7] * 11f);
            Rotate("RightArm", Vector3.forward, _outputs[8] * -11f);
            Rotate("LeftForeArm", Vector3.right, _outputs[7] * 7f);
            Rotate("RightForeArm", Vector3.right, _outputs[8] * -7f);
            Rotate("Head", Vector3.right, _outputs[9] * 5f);
            Rotate("LeftUpLeg", Vector3.right, _outputs[10] * 5f);
            Rotate("RightUpLeg", Vector3.right, _outputs[10] * -5f);
            Rotate("LeftLeg", Vector3.right, Mathf.Max(0, _outputs[10]) * 3f);
            Rotate("RightLeg", Vector3.right, Mathf.Max(0, -_outputs[10]) * 3f);
        }

        private void Rotate(string name, Vector3 axis, float degrees)
        {
            if (_bones.TryGetValue(name, out Transform bone) && bone != null)
                bone.localRotation = _rest[name] * Quaternion.AngleAxis(degrees, axis);
        }
        private void OnDisable()
        {
            foreach (var b in _bones)
                if (b.Value != null && _rest.TryGetValue(b.Key, out Quaternion rest)) b.Value.localRotation = rest;
            if (fillLight != null) fillLight.intensity = _restLight;
            Shader.SetGlobalFloat(Available, 0);
            Shader.SetGlobalFloat(IsRecording, 0);
            Shader.SetGlobalFloat(MatrixFlag, 0);
        }
    }
}
