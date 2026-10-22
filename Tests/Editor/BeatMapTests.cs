using System;
using System.IO;
using DanceFlow.Unity.Clock;
using NUnit.Framework;
using UnityEngine;

namespace DanceFlow.Unity.Tests
{
    public sealed class BeatMapTests
    {
        [Test]
        public void FixedClockHasSignedFloorAndPrecisePhases()
        {
            var fixedMap = new FixedBeatMap(120);
            var state = new BeatState(-0.125, fixedMap.Evaluate(-0.125).Beat, -0.25, 120, 4, 0, 0, 0, false);
            Assert.AreEqual(-1, state.BeatIndex);
            Assert.AreEqual(-4, state.Tick16);
            Assert.AreEqual(-16, state.Pip64);
            Assert.AreEqual(0.75, state.BeatPhase, 1e-12);
            Assert.AreEqual(-1, state.BarIndex);
        }

        [Test]
        public void StemLabAnchorsAndMissingOrdinalsAreNotQuantised()
        {
            var source = new SourceAudioIdentity(new string('a', 64), 8000, 24000, 1);
            // Fixture is also checked by the standalone .NET harness; avoid loading Editor-specific paths.
            var map = new VariableBeatMap(new[] {
                new BeatAnchor(0, 1000), new BeatAnchor(1, 5000),
                new BeatAnchor(2, 9000), new BeatAnchor(3, 14160), new BeatAnchor(4, 18080)
            }, source.SampleRate, source.FrameCount, 2, source.Sha256, new string('b', 64), "estimated");
            Assert.AreEqual(-2.25, map.Evaluate(0).Beat, 1e-12);
            Assert.AreEqual(0, map.EvaluateFrame(9000).Beat, 1e-12);
            Assert.AreEqual(0.5, map.EvaluateFrame(11580).Beat, 1e-12);
            Assert.AreEqual(60d * 8000 / 5160, map.EvaluateFrame(11580).Bpm, 1e-11);
            Assert.AreEqual(9000 / 8000d, map.SecondsAtBeat(0), 1e-12);
            var gap = new VariableBeatMap(new[] {new BeatAnchor(0, 1000), new BeatAnchor(3, 5000)},
                8000, 24000, 0, source.Sha256, new string('b', 64), "estimated");
            Assert.AreEqual(1.5, gap.EvaluateFrame(3000).Beat, 1e-12);
        }

        [Test]
        public void InvalidOriginAndOverlappingAnchorsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new VariableBeatMap(
                new[] { new BeatAnchor(0, 100), new BeatAnchor(1, 100) },
                8000, 24000, 0, new string('a', 64), new string('b', 64), "estimated"));
        }
    }
}
