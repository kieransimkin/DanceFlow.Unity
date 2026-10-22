using System;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.GPU;
using NUnit.Framework;
using UnityEngine;

namespace DanceFlow.Unity.Tests
{
    public sealed class ShaderClockTests
    {
        [Test]
        public void SignedPhaseAndMeterArePackedWithoutChangingOriginalClock()
        {
            var s = new BeatState(-0.125, -0.25, -0.25, 120, 3, 0, 17, 0, false);
            var gpu = ShaderClockMath.Pack(s);
            Assert.AreEqual(0.75, gpu.BeatPhase, 1e-7);
            Assert.AreEqual(11.0 / 12, gpu.BarPhase, 1e-6);
            Assert.AreEqual(0, gpu.TickPhase, 1e-6);
            Assert.AreEqual(-1, gpu.BeatIndexHigh);
            Assert.AreEqual(65535, gpu.BeatIndexLow);
            Assert.AreEqual(0, gpu.IsPlaying);
            Assert.AreEqual(-0.125, gpu.SourceSecondsHigh + (double)gpu.SourceSecondsLow, 1e-6);
        }

        [Test]
        public void IrregularStemLabTempoBecomesShaderBpmAndPhase()
        {
            var map = new VariableBeatMap(new[] {
                new BeatAnchor(0, 1000), new BeatAnchor(1, 5000),
                new BeatAnchor(2, 9000), new BeatAnchor(3, 14160)
            }, 8000, 24000, 2, new string('a', 64), new string('b', 64), "estimated");
            var p = map.EvaluateFrame(11580);
            var s = new BeatState(11580.0 / 8000, p.Beat, p.Beat, p.Bpm, 4, 0, 0, 0, true);
            var gpu = ShaderClockMath.Pack(s);
            Assert.AreEqual(0.5, gpu.BeatPhase, 1e-7);
            Assert.AreEqual(0.125, gpu.BarPhase, 1e-7);
            Assert.AreEqual(60.0 * 8000 / 5160, gpu.Bpm, 1e-4);
            Assert.AreEqual(1, gpu.IsPlaying);
        }

        [Test]
        public void VeryLongRuntimeCyclesPreserveIntegralThreeAndSevenBeatPhases()
        {
            foreach (double beat in new[] {-1200345.125, -1.75, 0.125, 7000021.75, 1000000017.125})
            {
                var state = new BeatState(beat / 2, beat, beat, 120, 4, 0, 17, 0, true);
                var sample = ShaderClockMath.Pack(state);
                foreach (int period in new[] {1, 2, 3, 4, 5, 7, 16, 64, 255, 256})
                {
                    double cpu = ShaderClockMath.CyclePhase(beat, period);
                    double gpu = ShaderClockMath.SimulateGpuCyclePhase(sample, period);
                    double error = Math.Min(Math.Abs(cpu - gpu), 1.0 - Math.Abs(cpu - gpu));
                    Assert.Less(error, 5e-5, $"beat={beat}, period={period}");
                }
            }
        }

        [Test]
        public void ContinuousDomainUsesLoopContinuityAndSeekCanPublishExplicitly()
        {
            var state = new BeatState(1, 2.25, 10.25, 120, 4, 0, 5, 4, true);
            Assert.AreEqual(2.25f, ShaderClockMath.Pack(state, ShaderBeatDomain.Source).BeatRemainder256, 1e-6);
            Assert.AreEqual(10.25f, ShaderClockMath.Pack(state, ShaderBeatDomain.Continuous).BeatRemainder256, 1e-6);
            var go = new GameObject("DanceFlow bridge test");
            try
            {
                var bridge = go.AddComponent<DanceFlowShaderBridge>();
                bridge.Publish(state);
                Assert.AreEqual(2.25, Shader.GetGlobalVector("_DanceFlow_BeatIndexHiLo").y +
                    Shader.GetGlobalFloat("_DanceFlow_BeatPhase"), 1e-6);
                Assert.AreEqual(ShaderClockSample.ShaderApiVersion,
                    Shader.GetGlobalFloat("_DanceFlow_ShaderApiVersion"));
                Assert.AreEqual(ShaderClockMath.Pack(state).BeatPulse,
                    Shader.GetGlobalFloat("_DanceFlow_BeatPulse"), 1e-6);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                Assert.AreEqual(0, Shader.GetGlobalFloat("_DanceFlow_ShaderApiVersion"));
            }
        }
    }
}
