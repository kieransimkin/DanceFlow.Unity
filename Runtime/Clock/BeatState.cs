using System;

namespace DanceFlow.Unity.Clock
{
    /// <summary>Immutable source-timestamped musical sample. A beat is one quarter note, not a Unity frame.</summary>
    public readonly struct BeatState
    {
        public readonly double SourceSeconds;
        public readonly double AbsoluteBeat;
        public readonly double ContinuousBeat;
        public readonly double Bpm;
        public readonly long BeatIndex;
        public readonly long Tick16;
        public readonly long Pip64;
        public readonly long BarIndex;
        public readonly double BeatPhase;
        public readonly double TickPhase;
        public readonly double PipPhase;
        public readonly double BarPhase;
        public readonly int BeatsPerBar;
        public readonly double BarOriginBeat;
        public readonly long TransportGeneration;
        /// <summary>Epoch for discontinuities that can jump musical coordinates (not pause/resume).</summary>
        public readonly long ContinuityGeneration;
        public readonly long LoopIteration;
        public readonly bool IsPlaying;

        public BeatState(double sourceSeconds, double beat, double continuousBeat, double bpm,
                         int beatsPerBar, double barOriginBeat, long generation,
                         long loopIteration, bool isPlaying, long continuityGeneration = 0)
        {
            if (beatsPerBar < 1 || beatsPerBar > 64)
                throw new ArgumentOutOfRangeException(nameof(beatsPerBar));
            SourceSeconds = MusicalMath.Finite(sourceSeconds, nameof(sourceSeconds));
            AbsoluteBeat = MusicalMath.Finite(beat, nameof(beat));
            ContinuousBeat = MusicalMath.Finite(continuousBeat, nameof(continuousBeat));
            Bpm = MusicalMath.Finite(bpm, nameof(bpm));
            if (bpm <= 0) throw new ArgumentOutOfRangeException(nameof(bpm));
            BeatsPerBar = beatsPerBar;
            BarOriginBeat = MusicalMath.Finite(barOriginBeat, nameof(barOriginBeat));
            TransportGeneration = generation;
            ContinuityGeneration = continuityGeneration;
            LoopIteration = loopIteration;
            IsPlaying = isPlaying;
            BeatIndex = MusicalMath.FloorToLong(beat);
            Tick16 = MusicalMath.FloorToLong(beat * MusicalMath.DanceMovesTicksPerBeat);
            Pip64 = MusicalMath.FloorToLong(beat * MusicalMath.DanceRudimentsPipsPerBeat);
            BeatPhase = MusicalMath.Fraction(beat);
            TickPhase = MusicalMath.Fraction(beat * MusicalMath.DanceMovesTicksPerBeat);
            PipPhase = MusicalMath.Fraction(beat * MusicalMath.DanceRudimentsPipsPerBeat);
            double bars = (beat - barOriginBeat) / beatsPerBar;
            BarIndex = MusicalMath.FloorToLong(bars);
            BarPhase = MusicalMath.Fraction(bars);
        }
    }

    public readonly struct BeatPosition
    {
        public readonly double Beat;
        public readonly double Bpm;
        public BeatPosition(double beat, double bpm)
        {
            Beat = MusicalMath.Finite(beat, nameof(beat));
            Bpm = MusicalMath.Finite(bpm, nameof(bpm));
            if (bpm <= 0) throw new ArgumentOutOfRangeException(nameof(bpm));
        }
    }

    public interface IBeatMap
    {
        BeatPosition Evaluate(double sourceSeconds);
        double SecondsAtBeat(double beat);
    }
}
