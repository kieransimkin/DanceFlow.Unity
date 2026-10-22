using System;
using System.Collections.Generic;

namespace DanceFlow.Unity.Clock
{
    public enum MusicalBoundaryKind { Beat, Bar, Phrase }
    public enum CatchUpPolicy { AllBounded, LatestOnly, AggregateMissed }
    public enum SeekEventPolicy { Suppress, ReplayForward }

    public readonly struct MusicalBoundaryEvent
    {
        public readonly MusicalBoundaryKind Kind;
        public readonly long Index;
        public readonly double ContinuousBeat;
        public readonly long Skipped;
        public readonly long TransportGeneration;

        public MusicalBoundaryEvent(MusicalBoundaryKind kind, long index, double beat, long skipped, long generation)
        {
            Kind = kind; Index = index; ContinuousBeat = beat;
            Skipped = skipped; TransportGeneration = generation;
        }
    }

    /// <summary>Boundary-crossing scheduler; never infers a beat solely from phase wrapping.</summary>
    public sealed class MusicalScheduler
    {
        private const int MaxPerStep = 512;
        private bool _hasLast;
        private BeatState _last;
        public CatchUpPolicy CatchUp { get; set; } = CatchUpPolicy.AllBounded;
        public SeekEventPolicy Seeks { get; set; } = SeekEventPolicy.Suppress;
        /// <summary>Phrase interval in bars; zero disables optional phrase events.</summary>
        public int PhraseBars { get; set; } = 0;

        public void Reset() { _hasLast = false; }

        public void Advance(BeatState state, Action<MusicalBoundaryEvent> emit)
        {
            if (emit == null) throw new ArgumentNullException(nameof(emit));
            if (PhraseBars < 0 || PhraseBars > 1024) throw new ArgumentOutOfRangeException(nameof(PhraseBars));
            if (!_hasLast) { _last = state; _hasLast = true; return; }
            BeatState previous = _last;
            _last = state;
            if (!state.IsPlaying || state.ContinuousBeat <= previous.ContinuousBeat) return;
            if (state.BeatsPerBar != previous.BeatsPerBar || state.BarOriginBeat != previous.BarOriginBeat) return;
            if (state.ContinuityGeneration != previous.ContinuityGeneration && Seeks == SeekEventPolicy.Suppress) return;

            long beatFirst = MusicalMath.FloorToLong(previous.ContinuousBeat) + 1;
            long beatLast = MusicalMath.FloorToLong(state.ContinuousBeat);
            long barFirst = MusicalMath.FloorToLong((previous.ContinuousBeat - state.BarOriginBeat) / state.BeatsPerBar) + 1;
            long barLast = MusicalMath.FloorToLong((state.ContinuousBeat - state.BarOriginBeat) / state.BeatsPerBar);
            long phraseFirst = 0, phraseLast = -1;
            if (PhraseBars > 0)
            {
                double phraseLength = PhraseBars * (double)state.BeatsPerBar;
                phraseFirst = MusicalMath.FloorToLong((previous.ContinuousBeat - state.BarOriginBeat) / phraseLength) + 1;
                phraseLast = MusicalMath.FloorToLong((state.ContinuousBeat - state.BarOriginBeat) / phraseLength);
            }
            long phraseCount = Math.Max(0L, phraseLast - phraseFirst + 1);
            long beatCount = Math.Max(0L, beatLast - beatFirst + 1);
            long barCount = Math.Max(0L, barLast - barFirst + 1);
            if (beatCount == 0 && barCount == 0 && phraseCount == 0) return;

            if (CatchUp != CatchUpPolicy.AllBounded || beatCount > MaxPerStep || barCount > MaxPerStep ||
                beatCount + barCount + phraseCount > MaxPerStep || phraseCount > MaxPerStep)
            {
                var reduced = new List<MusicalBoundaryEvent>(3);
                if (beatCount > 0)
                    reduced.Add(new MusicalBoundaryEvent(MusicalBoundaryKind.Beat, beatLast, beatLast,
                        CatchUp == CatchUpPolicy.LatestOnly ? 0 : beatCount - 1, state.TransportGeneration));
                if (barCount > 0)
                    reduced.Add(new MusicalBoundaryEvent(MusicalBoundaryKind.Bar, barLast,
                        state.BarOriginBeat + barLast * (double)state.BeatsPerBar,
                        CatchUp == CatchUpPolicy.LatestOnly ? 0 : barCount - 1, state.TransportGeneration));
                if (phraseCount > 0)
                    reduced.Add(new MusicalBoundaryEvent(MusicalBoundaryKind.Phrase, phraseLast,
                        state.BarOriginBeat + phraseLast * (double)PhraseBars * state.BeatsPerBar,
                        CatchUp == CatchUpPolicy.LatestOnly ? 0 : phraseCount - 1, state.TransportGeneration));
                reduced.Sort((a, b) =>
                {
                    int cmp = a.ContinuousBeat.CompareTo(b.ContinuousBeat);
                    return cmp != 0 ? cmp : a.Kind.CompareTo(b.Kind);
                });
                foreach (var boundary in reduced) emit(boundary);
                return;
            }

            var events = new List<MusicalBoundaryEvent>((int)(beatCount + barCount + phraseCount));
            for (long beat = beatFirst; beat <= beatLast; beat++)
                events.Add(new MusicalBoundaryEvent(MusicalBoundaryKind.Beat, beat, beat, 0, state.TransportGeneration));
            for (long bar = barFirst; bar <= barLast; bar++)
                events.Add(new MusicalBoundaryEvent(MusicalBoundaryKind.Bar, bar,
                    state.BarOriginBeat + bar * (double)state.BeatsPerBar, 0, state.TransportGeneration));
            for (long phrase = phraseFirst; phrase <= phraseLast; phrase++)
                events.Add(new MusicalBoundaryEvent(MusicalBoundaryKind.Phrase, phrase,
                    state.BarOriginBeat + phrase * (double)PhraseBars * state.BeatsPerBar, 0, state.TransportGeneration));
            events.Sort((a, b) =>
            {
                int cmp = a.ContinuousBeat.CompareTo(b.ContinuousBeat);
                return cmp != 0 ? cmp : a.Kind.CompareTo(b.Kind);
            });
            foreach (var boundary in events) emit(boundary);
        }
    }
}
