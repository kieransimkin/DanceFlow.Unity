using System;
using System.Collections.Generic;
using UnityEngine;

namespace DanceFlow.Unity.Rigging
{
    /// <summary>
    /// Explicit coordinate conversion; never infer source/target skeleton identity from names.
    /// BlenderZUp maps source local axes into Unity Y-up. Custom rigs require reviewed calibration.
    /// </summary>
    public enum RigCoordinateSpace { UnityLocalAlreadyRetargeted = 0, BlenderZUpToUnityYUp = 1 }
    public enum RigRootMotionMode { InPlace = 0, ApplyRecordedTranslation = 1 }
    public enum RigPlaybackBackend { DirectTransforms = 0, AnimationPlayables = 1 }

    [Serializable]
    public sealed class RigBoneBinding
    {
        public string sourceBone;
        public Transform target;
        [Range(0, 1)] public float maskWeight = 1f;
        public float translationScale = 1f;
        public bool isRootMotion;
        [Tooltip("Optional reviewed correction to source local axes. Identity for same-basis rigs.")]
        public Quaternion localBasisCorrection = Quaternion.identity;
    }

    /// <summary>Per-bone pose calculated from fully decoded physical rig controls.</summary>
    public struct RigTargetPose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public byte Flags; // 1 = translation, 2 = rotation, 4 = scale
    }

    internal sealed class RigBindingPlan
    {
        internal readonly RigBoneBinding Binding;
        internal readonly int[] Location = { -1, -1, -1 };
        internal readonly int[] Euler = { -1, -1, -1 };
        internal readonly int[] Scale = { -1, -1, -1 };
        internal readonly int[] Quaternion = { -1, -1, -1, -1 };
        internal string RotationMode;
        internal Vector3 RestPosition;
        internal UnityEngine.Quaternion RestRotation;
        internal Vector3 RestScale;
        internal RigBindingPlan(RigBoneBinding binding)
        {
            Binding = binding;
            RestPosition = binding.target.localPosition;
            RestRotation = binding.target.localRotation;
            RestScale = binding.target.localScale;
        }
        internal void CapturePose(RigProfile profile, double[] physical, RigCoordinateSpace space,
            RigRootMotionMode rootMotion, ref RigTargetPose pose)
        {
            pose.Position = RestPosition; pose.Rotation = RestRotation; pose.Scale = RestScale; pose.Flags = 0;
            if (Location[0] >= 0 && Location[1] >= 0 && Location[2] >= 0 &&
                (!Binding.isRootMotion || rootMotion == RigRootMotionMode.ApplyRecordedTranslation))
            {
                var delta = new Vector3((float)(physical[Location[0]] - profile.ChannelAt(Location[0]).Neutral),
                    (float)(physical[Location[1]] - profile.ChannelAt(Location[1]).Neutral),
                    (float)(physical[Location[2]] - profile.ChannelAt(Location[2]).Neutral));
                pose.Position = RestPosition + ConvertVector(delta, space, Binding.localBasisCorrection) * Binding.translationScale;
                pose.Flags |= 1;
            }
            if (Quaternion[0] >= 0 && Quaternion[1] >= 0 && Quaternion[2] >= 0 && Quaternion[3] >= 0)
            {
                var observed = new UnityEngine.Quaternion((float)physical[Quaternion[1]],
                    (float)physical[Quaternion[2]], (float)physical[Quaternion[3]], (float)physical[Quaternion[0]]);
                var neutral = new UnityEngine.Quaternion((float)profile.ChannelAt(Quaternion[1]).Neutral,
                    (float)profile.ChannelAt(Quaternion[2]).Neutral,
                    (float)profile.ChannelAt(Quaternion[3]).Neutral,
                    (float)profile.ChannelAt(Quaternion[0]).Neutral);
                UnityEngine.Quaternion delta = observed * UnityEngine.Quaternion.Inverse(neutral);
                pose.Rotation = RestRotation * ConvertRotation(delta, space, Binding.localBasisCorrection);
                pose.Flags |= 2;
            }
            else if (Euler[0] >= 0 && Euler[1] >= 0 && Euler[2] >= 0)
            {
                // The source Euler angles are Blender radians. The exact source order is
                // required; this adapter does not silently treat other modes as XYZ.
                if (RotationMode != "XYZ")
                    throw new NotSupportedException("Bake " + RotationMode + " Euler controls to quaternion channels before Unity retargeting.");
                float x = (float)((physical[Euler[0]] - profile.ChannelAt(Euler[0]).Neutral) * Mathf.Rad2Deg);
                float y = (float)((physical[Euler[1]] - profile.ChannelAt(Euler[1]).Neutral) * Mathf.Rad2Deg);
                float z = (float)((physical[Euler[2]] - profile.ChannelAt(Euler[2]).Neutral) * Mathf.Rad2Deg);
                // Blender XYZ intrinsic order composes Rz * Ry * Rx in source basis.
                // Unity Quaternion.Euler uses its own ZXY convention and is NOT equivalent.
                var qx = UnityEngine.Quaternion.AngleAxis(x, Vector3.right);
                var qy = UnityEngine.Quaternion.AngleAxis(y, Vector3.up);
                var qz = UnityEngine.Quaternion.AngleAxis(z, Vector3.forward);
                var delta = qz * qy * qx;
                pose.Rotation = RestRotation * ConvertRotation(delta, space, Binding.localBasisCorrection);
                pose.Flags |= 2;
            }
            if (Scale[0] >= 0 && Scale[1] >= 0 && Scale[2] >= 0)
            {
                // Retarget relative to the source neutral scale, preserving Unity bind scale.
                var factor = new Vector3((float)(physical[Scale[0]] / profile.ChannelAt(Scale[0]).Neutral),
                    (float)(physical[Scale[1]] / profile.ChannelAt(Scale[1]).Neutral),
                    (float)(physical[Scale[2]] / profile.ChannelAt(Scale[2]).Neutral));
                pose.Scale = Vector3.Scale(RestScale, factor);
                pose.Flags |= 4;
            }
        }
        private static Vector3 ConvertVector(Vector3 v, RigCoordinateSpace space, UnityEngine.Quaternion correction)
        {
            var basis = space == RigCoordinateSpace.BlenderZUpToUnityYUp ?
                UnityEngine.Quaternion.Euler(-90, 0, 0) : UnityEngine.Quaternion.identity;
            return correction * (basis * v);
        }
        private static UnityEngine.Quaternion ConvertRotation(UnityEngine.Quaternion q, RigCoordinateSpace space,
            UnityEngine.Quaternion correction)
        {
            var basis = space == RigCoordinateSpace.BlenderZUpToUnityYUp ?
                UnityEngine.Quaternion.Euler(-90, 0, 0) : UnityEngine.Quaternion.identity;
            var total = correction * basis;
            return total * q * UnityEngine.Quaternion.Inverse(total);
        }
    }

    public static class RigRetargetValidator
    {
        /// <summary>
        /// Resolve a profile against *explicit target Transform references*. All mapped source
        /// channels must exist; no silent name matching or inferred bone ancestry.
        /// </summary>
        internal static RigBindingPlan[] Build(RigProfile profile, RigBoneBinding[] bindings,
            Transform hierarchyRoot, SkinnedMeshRenderer skin, bool requireAllMappedBones)
        {
            if (profile == null || bindings == null || hierarchyRoot == null || skin == null)
                throw new ArgumentNullException("Rig profile, explicit mappings, hierarchy root, and SkinnedMeshRenderer are required.");
            var targets = new HashSet<Transform>(skin.bones);
            var usedSource = new HashSet<string>(StringComparer.Ordinal);
            var usedTarget = new HashSet<Transform>();
            var plans = new List<RigBindingPlan>();
            foreach (RigBoneBinding entry in bindings)
            {
                if (entry == null || entry.target == null || string.IsNullOrWhiteSpace(entry.sourceBone))
                    throw new InvalidOperationException("Rig bindings must name a source bone and a Unity Transform.");
                if (!usedSource.Add(entry.sourceBone) || !usedTarget.Add(entry.target))
                    throw new InvalidOperationException("Source bones and target Transform references must be unique.");
                if (!entry.target.IsChildOf(hierarchyRoot) || !targets.Contains(entry.target))
                    throw new InvalidOperationException("Target must belong to the selected skinned mesh and hierarchy.");
                if ((float.IsNaN(entry.translationScale) || float.IsInfinity(entry.translationScale)) || entry.translationScale <= 0 ||
                    (float.IsNaN(entry.maskWeight) || float.IsInfinity(entry.maskWeight)) || entry.maskWeight < 0 || entry.maskWeight > 1)
                    throw new InvalidOperationException("Rig per-bone translation scales and weights must be bounded.");
                var plan = new RigBindingPlan(entry);
                for (int i=0;i<profile.ChannelCount;i++)
                {
                    var channel = profile.ChannelAt(i);
                    if (channel.Bone != entry.sourceBone) continue;
                    int axis=channel.ArrayIndex;
                    switch (channel.DataPath)
                    {
                        case "location": if (axis>=0 && axis<=2) plan.Location[axis]=i; break;
                        case "rotation_euler": if (axis>=0 && axis<=2)
                        {
                            plan.Euler[axis]=i; plan.RotationMode=channel.RotationMode;
                        } break;
                        case "rotation_quaternion": if (axis>=0 && axis<=3) plan.Quaternion[axis]=i; break;
                        case "scale": if (axis>=0 && axis<=2) plan.Scale[axis]=i; break;
                        default: break; // Unsupported Rigify custom properties must be baked upstream.
                    }
                }
                bool location = AllPresent(plan.Location);
                bool quaternion = AllPresent(plan.Quaternion);
                bool euler = AllPresent(plan.Euler);
                bool scale = AllPresent(plan.Scale);
                if (AnyPresent(plan.Location) && !location || AnyPresent(plan.Euler) && !euler ||
                    AnyPresent(plan.Quaternion) && !quaternion || AnyPresent(plan.Scale) && !scale)
                    throw new InvalidOperationException("Partial per-bone vector or quaternion control: " + entry.sourceBone);
                if (quaternion && euler) throw new InvalidOperationException("Mixed Euler/quaternion rotation for source bone.");
                if (euler && plan.RotationMode != "XYZ")
                    throw new NotSupportedException("Non-XYZ Euler rigs must be baked to quaternion controls.");
                if (scale)
                {
                    for (int axis=0; axis<3;axis++)
                        if (profile.ChannelAt(plan.Scale[axis]).Neutral <= 0)
                            throw new InvalidOperationException("Scale neutral must be positive to retarget multiplicatively.");
                }
                if (!location && !quaternion && !euler && !scale)
                    throw new InvalidOperationException("Source bone has no supported transform channels: " + entry.sourceBone);
                plans.Add(plan);
            }
            if (plans.Count == 0 || (requireAllMappedBones && plans.Count != targets.Count))
                throw new InvalidOperationException("Rig mapping is empty or not all skinned bones are mapped.");
            return plans.ToArray();
        }
        private static bool AllPresent(int[] v)
        { foreach (int x in v) if (x < 0) return false; return true; }
        private static bool AnyPresent(int[] v)
        { foreach (int x in v) if (x >= 0) return true; return false; }
    }
}
