using System;
using System.Collections.Generic;

namespace DanceFlow.Unity.Motion
{
    /// <summary>
    /// Explicit reference-counted managed sampler cache; no scene-wide preload.
    /// A recording is released when its last active consumer calls Release.
    /// Not thread-safe; use on Unity's main thread.
    /// </summary>
    public static class MotionRecordingCache
    {
        private sealed class Entry
        {
            public MotionRecording Value;
            public int Owners;
        }
        private static readonly Dictionary<MotionRecordingAsset, Entry> Open =
            new Dictionary<MotionRecordingAsset, Entry>();

        public static MotionRecording Acquire(MotionRecordingAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (Open.TryGetValue(asset, out Entry found))
            {
                found.Owners = checked(found.Owners + 1);
                return found.Value;
            }
            var value = asset.OpenVerified();
            Open.Add(asset, new Entry { Value = value, Owners = 1 });
            return value;
        }

        public static void Release(MotionRecordingAsset asset)
        {
            if (ReferenceEquals(asset, null)) return;
            if (!Open.TryGetValue(asset, out Entry found))
                throw new InvalidOperationException("Recording cache reference count underflow.");
            found.Owners--;
            if (found.Owners == 0) Open.Remove(asset);
        }

        public static int ActiveRecordingCount => Open.Count;
    }
}
