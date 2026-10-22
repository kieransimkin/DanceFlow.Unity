using System;

namespace DanceFlow.Unity.Performance
{
    /// <summary>Pure managed summary of valid timing samples. Unavailable observations are excluded.</summary>
    public readonly struct FrameSeriesStats
    {
        public readonly int Count;
        public readonly double Mean;
        public readonly double Minimum;
        public readonly double Maximum;
        public readonly double P50;
        public readonly double P95;
        public readonly double P99;

        private FrameSeriesStats(int count, double mean, double minimum, double maximum,
                                 double p50, double p95, double p99)
        {
            Count = count; Mean = mean; Minimum = minimum; Maximum = maximum;
            P50 = p50; P95 = p95; P99 = p99;
        }

        public static FrameSeriesStats Calculate(double[] samples, int count)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (count < 0 || count > samples.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0) return default;
            var values = new double[count];
            double total = 0;
            for (int i = 0; i < count; i++)
            {
                if (double.IsNaN(samples[i]) || double.IsInfinity(samples[i]) || samples[i] < 0)
                    throw new ArgumentException("Statistics require finite nonnegative observations.", nameof(samples));
                values[i] = samples[i];
                total += samples[i];
            }
            Array.Sort(values);
            return new FrameSeriesStats(count, total / count, values[0], values[count - 1],
                Percentile(values, 0.5), Percentile(values, 0.95), Percentile(values, 0.99));
        }

        // Type 7 linear interpolation, consistent with the standalone Python benchmark analyser.
        private static double Percentile(double[] sorted, double quantile)
        {
            double pos = quantile * (sorted.Length - 1);
            int left = (int)Math.Floor(pos);
            int right = Math.Min(left + 1, sorted.Length - 1);
            return sorted[left] + (sorted[right] - sorted[left]) * (pos - left);
        }
    }
}
