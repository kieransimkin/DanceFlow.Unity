using System;
using DanceFlow.Unity.Motion;
using NUnit.Framework;

namespace DanceFlow.Unity.Tests
{
    public sealed class MotionShowcaseTests
    {
        [Test]
        public void SyntheticPreviewIsDeterministicAndBounded()
        {
            var a = new double[4];
            var b = new double[4];
            MotionSignalMath.SyntheticPreview(-0.25, a);
            MotionSignalMath.SyntheticPreview(-0.25, b);
            CollectionAssert.AreEqual(a, b);
            for (int i = 0; i < 4; i++) Assert.That(Math.Abs(a[i]), Is.LessThanOrEqualTo(1));
            MotionSignalMath.SyntheticPreview(128, a);
            for (int i = 0; i < 4; i++) Assert.That(Math.Abs(a[i]), Is.LessThanOrEqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => MotionSignalMath.SyntheticPreview(double.NaN, b));
            Assert.Throws<ArgumentException>(() => MotionSignalMath.SyntheticPreview(0, new double[3]));
        }

        [Test]
        public void RemapPreservesEndPointsAndGainClamps()
        {
            Assert.AreEqual(0, MotionSignalMath.Remap(-1, 0, 10));
            Assert.AreEqual(5, MotionSignalMath.Remap(0, 0, 10));
            Assert.AreEqual(10, MotionSignalMath.Remap(1, 0, 10));
            Assert.AreEqual(10, MotionSignalMath.Remap(1, 0, 10, 30));
        }

        [Test]
        public void GpuSampleBudgetIsBounded()
        {
            Assert.AreEqual(1024 * 1024, DanceFlow.Unity.GPU.MotionRecordingGpuBridge.MaximumGpuScalars);
        }
    }
}
