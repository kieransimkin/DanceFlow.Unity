using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;

namespace DanceFlow.Unity.Rigging
{
    /// <summary>
    /// A Playables post-process job. Its pose arrays are populated from the *master clock*
    /// before graph evaluation. No wall-clock timing or recording sampling occurs on worker threads.
    /// </summary>
    public struct RigMotionAnimationJob : IAnimationJob
    {
        public NativeArray<TransformStreamHandle> Handles;
        public NativeArray<Vector3> Positions;
        public NativeArray<Quaternion> Rotations;
        public NativeArray<Vector3> Scales;
        public NativeArray<byte> ChannelFlags;
        public NativeArray<float> Weights;

        public void ProcessRootMotion(AnimationStream stream) { }

        public void ProcessAnimation(AnimationStream stream)
        {
            for (int i=0;i<Handles.Length;i++)
            {
                var handle=Handles[i];
                if (!handle.IsValid(stream)) continue;
                float weight=Mathf.Clamp01(Weights[i]);
                if (weight <= 0) continue;
                byte flags=ChannelFlags[i];
                if ((flags & 1) != 0)
                    handle.SetLocalPosition(stream, Vector3.Lerp(handle.GetLocalPosition(stream), Positions[i], weight));
                if ((flags & 2) != 0)
                    handle.SetLocalRotation(stream, Quaternion.Slerp(handle.GetLocalRotation(stream), Rotations[i], weight));
                if ((flags & 4) != 0)
                    handle.SetLocalScale(stream, Vector3.Lerp(handle.GetLocalScale(stream), Scales[i], weight));
            }
        }
    }
}
