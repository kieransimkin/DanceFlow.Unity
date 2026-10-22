using System;

namespace DanceFlow.Unity.Clock
{
    /// <summary>Monotonic seconds in an external time domain (e.g. Unity's audio DSP time).</summary>
    public interface IClockTimeSource
    {
        double NowSeconds { get; }
    }

    /// <summary>Deterministic clock for tests, editor scrubbing and offline rendering.</summary>
    public sealed class ManualClockSource : IClockTimeSource
    {
        private double _now;
        public double NowSeconds => _now;
        public ManualClockSource(double initialSeconds = 0) { Set(initialSeconds); }
        public void Set(double seconds) { _now = MusicalMath.Finite(seconds, nameof(seconds)); }
        public void Advance(double seconds)
        {
            MusicalMath.Finite(seconds, nameof(seconds));
            if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            Set(_now + seconds);
        }
    }
}
