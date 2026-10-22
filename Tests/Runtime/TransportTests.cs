using DanceFlow.Unity.Clock;
using NUnit.Framework;

namespace DanceFlow.Unity.Tests
{
    public sealed class TransportTests
    {
        [Test]
        public void PauseSeekRateLoopAndMeterStayClockDerived()
        {
            var clock = new ManualClockSource();
            var player = new MusicalTransport(new FixedBeatMap(120), clock);
            player.SetMeter(3);
            player.Play(); clock.Advance(1.25);
            Assert.AreEqual(2.5, player.Evaluate().AbsoluteBeat, 1e-12);
            player.Pause(); clock.Advance(4);
            Assert.AreEqual(2.5, player.Evaluate().AbsoluteBeat, 1e-12);
            player.SeekBeat(3); player.SetPlaybackRate(0.5); player.Play();
            clock.Advance(0.5);
            Assert.AreEqual(3.5, player.Evaluate().AbsoluteBeat, 1e-12);
            player.SetExcerpt(0, 4); player.SetLoop(1, 2);
            player.SeekSourceSeconds(1); clock.Advance(2.5);
            var looped = player.Evaluate();
            Assert.AreEqual(1.25, looped.SourceSeconds, 1e-12);
            Assert.AreEqual(1, looped.LoopIteration);
            Assert.AreEqual(4.5, looped.ContinuousBeat, 1e-12);
        }

        [Test]
        public void OriginalSourceCuesWorkAcrossLoopSeams()
        {
            var clock = new ManualClockSource();
            var transport = new MusicalTransport(new FixedBeatMap(120), clock);
            transport.SetExcerpt(0, 4); transport.SetLoop(1, 2); transport.SeekSourceSeconds(1.5);
            var cues = new SourceCueScheduler(transport,
                new[] {new SourceCue(1.8, "CHORUS"), new SourceCue(1.2, "VERSE")});
            var names = new System.Collections.Generic.List<string>();
            transport.Play(); cues.Advance(transport.Evaluate(), c => names.Add(c.Name));
            clock.Advance(1.1); cues.Advance(transport.Evaluate(), c => names.Add(c.Name));
            CollectionAssert.AreEqual(new[] {"CHORUS", "VERSE"}, names);
        }

        [Test]
        public void SchedulerReplaysEachCrossedBoundaryButNeverSeekBursts()
        {
            var clock = new ManualClockSource();
            var transport = new MusicalTransport(new FixedBeatMap(120), clock);
            var scheduler = new MusicalScheduler();
            int beats = 0, bars = 0;
            System.Action<MusicalBoundaryEvent> receive = e => {
                if (e.Kind == MusicalBoundaryKind.Beat) beats++; else bars++;
            };
            transport.Play(); scheduler.Advance(transport.Evaluate(), receive);
            clock.Advance(2.1); scheduler.Advance(transport.Evaluate(), receive);
            Assert.AreEqual(4, beats); Assert.AreEqual(1, bars);
            transport.SeekBeat(100); scheduler.Advance(transport.Evaluate(), receive);
            Assert.AreEqual(4, beats);
        }
    }
}
