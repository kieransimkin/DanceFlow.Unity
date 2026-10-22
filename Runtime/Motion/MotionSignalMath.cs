using System;

namespace DanceFlow.Unity.Motion
{
    /// <summary>
    /// Pure, deterministic preview and modulation curves. These are NOT matching results,
    /// nor are they a reimplementation of the canonical DanceRudiments catalogue.
    /// The recording player remains the authority for real captured motion.
    /// </summary>
    public static class MotionSignalMath
    {
        public static double ClampSigned(double value) => Math.Max(-1.0, Math.Min(1.0, value));
        public static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));

        public static void SyntheticPreview(double absoluteBeat, double[] destination)
        {
            if (destination == null || destination.Length < 4) throw new ArgumentException("Four output slots required.", nameof(destination));
            if (double.IsNaN(absoluteBeat) || double.IsInfinity(absoluteBeat))
                throw new ArgumentOutOfRangeException(nameof(absoluteBeat));
            double phase = absoluteBeat - Math.Floor(absoluteBeat);
            double pulse = Math.Pow(1 - phase, 6);
            // Deliberately synthetic, repeatable demonstration waveforms.
            destination[0] = ClampSigned(1.8 * pulse - 0.5);                      // bass / trunk bending
            destination[1] = ClampSigned(Math.Sin(absoluteBeat * Math.PI * 1.5)); // drums / gusts
            destination[2] = ClampSigned(Math.Sin(absoluteBeat * Math.PI * 0.25)); // melody / palette
            destination[3] = ClampSigned(Math.Sin(absoluteBeat * Math.PI * 0.125 + 1)); // sparkle
        }

        public static double Remap(double signedMotion, double low, double high, double gain = 1)
        {
            if (double.IsNaN(signedMotion) || double.IsInfinity(signedMotion) ||
                double.IsNaN(low) || double.IsInfinity(low) ||
                double.IsNaN(high) || double.IsInfinity(high) ||
                double.IsNaN(gain) || double.IsInfinity(gain))
                throw new ArgumentOutOfRangeException(nameof(signedMotion));
            return low + (high - low) * Clamp01(0.5 + 0.5 * signedMotion * gain);
        }
    }
}
