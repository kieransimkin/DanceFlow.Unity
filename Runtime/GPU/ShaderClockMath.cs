using System;
using DanceFlow.Unity.Clock;

namespace DanceFlow.Unity.GPU
{
    /// <summary>Source phase repeats with the master source; continuous phase advances through transport loops.</summary>
    public enum ShaderBeatDomain { Source = 0, Continuous = 1 }

    /// <summary>Immutable Unity-independent payload. The integer split preserves musical phases over long runtimes.</summary>
    public readonly struct ShaderClockSample
    {
        public const int ShaderApiVersion = 2;
        public readonly float Bpm;
        public readonly float BeatPhase;
        public readonly float BarPhase;
        public readonly float TickPhase;
        public readonly float PipPhase;
        public readonly float BeatPulse;
        public readonly float DownbeatPulse;
        public readonly float BeatSin;
        public readonly float BeatCos;
        public readonly float BeatIndexHigh;
        public readonly float BeatIndexLow;
        public readonly float BeatRemainder256;
        public readonly float SourceSecondsHigh;
        public readonly float SourceSecondsLow;
        public readonly float TransportGenerationHigh;
        public readonly float TransportGenerationLow;
        public readonly float BeatsPerBar;
        public readonly float IsPlaying;

        internal ShaderClockSample(float bpm, float beatPhase, float barPhase, float tickPhase,
                                   float pipPhase, float beatPulse, float downbeatPulse,
                                   float beatSin, float beatCos, float beatIndexHigh,
                                   float beatIndexLow, float beatRemainder256,
                                   float sourceSecondsHigh, float sourceSecondsLow,
                                   float generationHigh, float generationLow,
                                   float beatsPerBar, float isPlaying)
        {
            Bpm = bpm;
            BeatPhase = beatPhase;
            BarPhase = barPhase;
            TickPhase = tickPhase;
            PipPhase = pipPhase;
            BeatPulse = beatPulse;
            DownbeatPulse = downbeatPulse;
            BeatSin = beatSin;
            BeatCos = beatCos;
            BeatIndexHigh = beatIndexHigh;
            BeatIndexLow = beatIndexLow;
            BeatRemainder256 = beatRemainder256;
            SourceSecondsHigh = sourceSecondsHigh;
            SourceSecondsLow = sourceSecondsLow;
            TransportGenerationHigh = generationHigh;
            TransportGenerationLow = generationLow;
            BeatsPerBar = beatsPerBar;
            IsPlaying = isPlaying;
        }
    }

    public static class ShaderClockMath
    {
        public const double CounterRadix = 65536.0;
        public const double MaximumExactHighCounter = 16777216.0;
        private const double Tau = Math.PI * 2.0;

        public static double PositiveMod(double value, double period)
        {
            MusicalMath.Finite(value, nameof(value));
            MusicalMath.Finite(period, nameof(period));
            if (period <= 0) throw new ArgumentOutOfRangeException(nameof(period));
            return value - period * Math.Floor(value / period);
        }

        /// <summary>Signed Euclidean division. Both pieces are represented exactly as GPU float integers.</summary>
        public static void SplitCounter(long value, out float high, out float low)
        {
            long hi = MusicalMath.FloorDiv(value, (int)CounterRadix);
            long lo = value - hi * (long)CounterRadix;
            if (Math.Abs((double)hi) > MaximumExactHighCounter)
                throw new OverflowException("Counter exceeds the shader's precise two-float integer range.");
            high = (float)hi;
            low = (float)lo;
        }

        public static void SplitSeconds(double seconds, out float high, out float low)
        {
            MusicalMath.Finite(seconds, nameof(seconds));
            high = (float)seconds;
            if (float.IsInfinity(high))
                throw new OverflowException("Seconds exceed shader float representation.");
            low = (float)(seconds - (double)high);
        }

        /// <summary>Derive GPU values from ONE BeatState; never create an independent beat timer.</summary>
        public static ShaderClockSample Pack(BeatState state, ShaderBeatDomain domain = ShaderBeatDomain.Source)
        {
            if (domain != ShaderBeatDomain.Source && domain != ShaderBeatDomain.Continuous)
                throw new ArgumentOutOfRangeException(nameof(domain));
            double beat = domain == ShaderBeatDomain.Continuous ? state.ContinuousBeat : state.AbsoluteBeat;
            double beatPhase = MusicalMath.Fraction(beat);
            double barPhase = MusicalMath.Fraction((beat - state.BarOriginBeat) / state.BeatsPerBar);
            double tickPhase = MusicalMath.Fraction(beat * MusicalMath.DanceMovesTicksPerBeat);
            double pipPhase = MusicalMath.Fraction(beat * MusicalMath.DanceRudimentsPipsPerBeat);
            double beatPulse = Math.Pow(1.0 - beatPhase, 8.0);
            // The bar origin may be fractional, so the downbeat is not necessarily a whole beat.
            double downbeatPulse = Math.Pow(Math.Max(0.0, 1.0 - barPhase * state.BeatsPerBar), 8.0);
            SplitCounter(MusicalMath.FloorToLong(beat), out float indexHigh, out float indexLow);
            SplitCounter(state.TransportGeneration, out float genHigh, out float genLow);
            SplitSeconds(state.SourceSeconds, out float timeHigh, out float timeLow);
            return new ShaderClockSample(
                (float)state.Bpm, (float)beatPhase, (float)barPhase, (float)tickPhase, (float)pipPhase,
                (float)beatPulse, (float)downbeatPulse,
                (float)Math.Sin(Tau * beatPhase), (float)Math.Cos(Tau * beatPhase),
                indexHigh, indexLow, (float)PositiveMod(beat, 256.0),
                timeHigh, timeLow, genHigh, genLow, state.BeatsPerBar, state.IsPlaying ? 1f : 0f);
        }

        public static double CyclePhase(double beat, int periodBeats)
        {
            if (periodBeats < 1 || periodBeats > 256) throw new ArgumentOutOfRangeException(nameof(periodBeats));
            return PositiveMod(beat, periodBeats) / periodBeats;
        }

        /// <summary>Float-only reference for DF_CyclePhase in the HLSL; tolerance at most 5e-5 cycles.</summary>
        public static float SimulateGpuCyclePhase(ShaderClockSample s, int periodBeats)
        {
            if (periodBeats < 1 || periodBeats > 256) throw new ArgumentOutOfRangeException(nameof(periodBeats));
            float p = periodBeats;
            float hi = PosModFloat(s.BeatIndexHigh, p);
            float lo = PosModFloat(s.BeatIndexLow, p);
            float radix = PosModFloat((float)CounterRadix, p);
            float integerRemainder = PosModFloat(PosModFloat(hi * radix, p) + lo, p);
            return PosModFloat((integerRemainder + s.BeatPhase) / p, 1f);
        }

        private static float PosModFloat(float x, float period) => x - period * (float)Math.Floor(x / period);
    }
}
