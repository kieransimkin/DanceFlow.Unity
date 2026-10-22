using System;
using DanceFlow.Unity.Performance;
using NUnit.Framework;

namespace DanceFlow.Unity.Tests
{
    public sealed class FrameSeriesStatsTests
    {
        [Test]
        public void ReportsTypeSevenInterpolatedPercentiles()
        {
            var summary = FrameSeriesStats.Calculate(new[] { 30d, 0d, 10d, 20d, 40d }, 5);
            Assert.AreEqual(5, summary.Count);
            Assert.AreEqual(20, summary.Mean, 1e-12);
            Assert.AreEqual(20, summary.P50, 1e-12);
            Assert.AreEqual(38, summary.P95, 1e-12);
            Assert.AreEqual(39.6, summary.P99, 1e-12);
        }

        [Test]
        public void NoMeasurementsAreNotFabricated()
        {
            var summary = FrameSeriesStats.Calculate(Array.Empty<double>(), 0);
            Assert.AreEqual(0, summary.Count);
        }

        [Test]
        public void RejectsMalformedMeasurements()
        {
            Assert.Throws<ArgumentException>(() => FrameSeriesStats.Calculate(new[] { double.NaN }, 1));
            Assert.Throws<ArgumentException>(() => FrameSeriesStats.Calculate(new[] { double.PositiveInfinity }, 1));
            Assert.Throws<ArgumentException>(() => FrameSeriesStats.Calculate(new[] { -1d }, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => FrameSeriesStats.Calculate(new[] { 1d }, 2));
        }
    }
}
