using System;
using System.Collections.Generic;
using DanceFlow.Unity.Clock;
using UnityEngine;

namespace DanceFlow.Unity.WebGLDemo
{
    /// <summary>
    /// Moves the genuine 24-bone Kandi Boom FBX skeleton. This is explicitly a synthetic
    /// beat-driven movement preview, NOT the time-only CMU salsa or ballet recordings.
    /// Source-local rest pose is retained; no synthetic mesh or approximate skeleton.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(9300)]
    public sealed class KandiBoomBeatDance : MonoBehaviour
    {
        [SerializeField] private DanceFlowClockBehaviour clock;
        [SerializeField] private Transform characterRoot;
        [SerializeField] private float armSwingDegrees = 12f;
        [SerializeField] private float hipSwayDegrees = 7f;
        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly Dictionary<string, Quaternion> _rest = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
        private bool _ready;

        public void Configure(DanceFlowClockBehaviour source, Transform realCharacterRoot)
        {
            clock = source;
            characterRoot = realCharacterRoot;
            _ready = false;
        }

        private void Start()
        {
            if (clock == null || characterRoot == null)
            {
                Debug.LogError("Kandi Boom preview requires real FBX and DanceFlow clock.", this);
                enabled = false;
                return;
            }
            foreach (Transform bone in characterRoot.GetComponentsInChildren<Transform>(true))
            {
                if (_bones.ContainsKey(bone.name)) continue;
                _bones.Add(bone.name, bone);
                _rest.Add(bone.name, bone.localRotation);
            }
            foreach (string required in new[] { "Hips", "Spine", "LeftArm", "RightArm", "LeftForeArm", "RightForeArm", "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg", "Head" })
            {
                if (_bones.ContainsKey(required)) continue;
                Debug.LogError("Kandi Boom's verified bone is missing: " + required, this);
                enabled = false;
                return;
            }
            _ready = true;
        }

        private void LateUpdate()
        {
            if (!_ready || clock == null || clock.Transport == null) return;
            // Every pose derives directly from absolute beat position: seek/pause/frame skips cannot drift.
            double beat = clock.Current.AbsoluteBeat;
            float groove = Mathf.Sin((float)(beat * Math.PI / 2d));
            float left = Mathf.Sin((float)(beat * Math.PI));
            float right = Mathf.Sin((float)(beat * Math.PI + Math.PI));
            float pulse = Mathf.Pow(Mathf.Max(0, Mathf.Cos((float)(beat * 2d * Math.PI))), 4f);
            Rotate("Hips", Vector3.up, hipSwayDegrees * groove);
            Rotate("Spine", Vector3.forward, 3.0f * groove);
            Rotate("Head", Vector3.right, 2.5f * left);
            // Restrained swings because Kandi's authored bind arms angle downward significantly.
            Rotate("LeftArm", Vector3.forward, armSwingDegrees * left);
            Rotate("RightArm", Vector3.forward, -armSwingDegrees * right);
            Rotate("LeftForeArm", Vector3.right, 7.0f * pulse);
            Rotate("RightForeArm", Vector3.right, -7.0f * pulse);
            Rotate("LeftUpLeg", Vector3.right, 5f * right);
            Rotate("RightUpLeg", Vector3.right, 5f * left);
            Rotate("LeftLeg", Vector3.right, 3f * Mathf.Max(0, left));
            Rotate("RightLeg", Vector3.right, 3f * Mathf.Max(0, right));
        }

        private void Rotate(string name, Vector3 axis, float degrees)
        {
            if (_bones.TryGetValue(name, out Transform bone))
                bone.localRotation = _rest[name] * Quaternion.AngleAxis(degrees, axis);
        }

        private void OnDisable()
        {
            foreach (var pair in _bones)
                if (pair.Value != null && _rest.TryGetValue(pair.Key, out Quaternion q))
                    pair.Value.localRotation = q;
        }
    }
}
