using System;

namespace DanceFlow.Unity.Clock
{
    /// <summary>Deterministic musical coordinates. All floor operations are signed, never C# truncation.</summary>
    public static class MusicalMath
    {
        public const int DanceMovesTicksPerBeat = 16;
        public const int DanceRudimentsPipsPerBeat = 64;

        public static double Finite(double value, string argument)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(argument, "Must be finite.");
            return value;
        }

        public static long FloorToLong(double value)
        {
            Finite(value, nameof(value));
            if (value < long.MinValue || value >= 9223372036854775808.0)
                throw new OverflowException("Musical index exceeds Int64 range.");
            return checked((long)Math.Floor(value));
        }

        public static double Fraction(double value)
        {
            Finite(value, nameof(value));
            return value - Math.Floor(value);
        }

        public static long FloorDiv(long value, int divisor)
        {
            if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
            long quotient = value / divisor;
            return value % divisor < 0 ? quotient - 1 : quotient;
        }
    }
}
