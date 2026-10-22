using System;

namespace DanceFlow.Unity.Clock
{
    /// <summary>
    /// Single authoritative transport; all positions are derived from absolute clock timestamps.
    /// SourceSeconds are from the original audio master; render frame times are never accumulated.
    /// Not thread-safe; modify/evaluate from the Unity main thread or protect externally.
    /// </summary>
    public sealed class MusicalTransport
    {
        private IBeatMap _beatMap;
        private readonly IClockTimeSource _clock;
        private double _anchorClock;
        private double _anchorSource;
        private double _rate = 1;
        private bool _running;
        private double _excerptStart;
        private double _excerptEnd = double.PositiveInfinity;
        private double _loopStart;
        private double _loopEnd;
        private bool _loop;
        private int _meter = 4;
        private double _barOriginBeat;
        private long _generation;
        private long _continuityGeneration;

        public long Generation => _generation;
        public long ContinuityGeneration => _continuityGeneration;
        public int BeatsPerBar => _meter;
        public double PlaybackRate => _rate;
        public bool HasLoop => _loop;
        public double LoopStart => _loopStart;
        public double LoopEnd => _loopEnd;
        public bool IsRunning => _running;
        public double ExcerptStart => _excerptStart;
        public double ExcerptEnd => _excerptEnd;
        public IBeatMap BeatMap => _beatMap;

        public MusicalTransport(IBeatMap beatMap, IClockTimeSource timeSource)
        {
            _beatMap = beatMap ?? throw new ArgumentNullException(nameof(beatMap));
            _clock = timeSource ?? throw new ArgumentNullException(nameof(timeSource));
            _anchorClock = ReadClock();
        }

        private double ReadClock() => MusicalMath.Finite(_clock.NowSeconds, "NowSeconds");
        private double RawPosition(double now) =>
            _running ? _anchorSource + Math.Max(0.0, now - _anchorClock) * _rate : _anchorSource;

        private double ClampExcerpt(double value) => Math.Min(_excerptEnd, Math.Max(_excerptStart, value));

        private void Reanchor(bool breaksContinuity = false)
        {
            double now = ReadClock();
            bool pendingStart = _running && now < _anchorClock;
            var current = Evaluate();
            _anchorSource = current.SourceSeconds;
            _anchorClock = pendingStart ? _anchorClock : now;
            // A physical jump or new rate is a scheduler discontinuity.
            _generation = checked(_generation + 1);
            if (breaksContinuity) _continuityGeneration = checked(_continuityGeneration + 1);
        }

        public void Play()
        {
            if (_running) return;
            _anchorClock = ReadClock(); _running = true;
            _generation = checked(_generation + 1);
        }

        /// <summary>Clock domain must match the supplied IClockTimeSource, e.g. AudioSettings.dspTime.</summary>
        public void PlayScheduled(double clockStartSeconds)
        {
            MusicalMath.Finite(clockStartSeconds, nameof(clockStartSeconds));
            if (clockStartSeconds < ReadClock()) throw new ArgumentOutOfRangeException(nameof(clockStartSeconds));
            if (_running) throw new InvalidOperationException("Pause or stop before scheduling a new start.");
            _anchorClock = clockStartSeconds; _running = true;
            _generation = checked(_generation + 1);
        }

        public void Pause()
        {
            if (!_running) return;
            Reanchor();
            _running = false;
        }

        public void Stop()
        {
            _running = false;
            _anchorSource = _excerptStart;
            _anchorClock = ReadClock();
            _generation = checked(_generation + 1);
            _continuityGeneration = checked(_continuityGeneration + 1);
        }

        public void SeekSourceSeconds(double sourceSeconds)
        {
            MusicalMath.Finite(sourceSeconds, nameof(sourceSeconds));
            if (sourceSeconds < _excerptStart || sourceSeconds > _excerptEnd)
                throw new ArgumentOutOfRangeException(nameof(sourceSeconds), "Seek must be inside source excerpt.");
            _anchorSource = sourceSeconds;
            _anchorClock = ReadClock();
            _generation = checked(_generation + 1);
            _continuityGeneration = checked(_continuityGeneration + 1);
        }

        public void SeekBeat(double beat) => SeekSourceSeconds(_beatMap.SecondsAtBeat(beat));

        public void SetPlaybackRate(double rate)
        {
            MusicalMath.Finite(rate, nameof(rate));
            if (rate <= 0 || rate > 16) throw new ArgumentOutOfRangeException(nameof(rate));
            if (_rate == rate) return;
            Reanchor(); _rate = rate;
        }

        public void SetMeter(int beatsPerBar, double barOriginBeat = 0)
        {
            if (beatsPerBar < 1 || beatsPerBar > 64) throw new ArgumentOutOfRangeException(nameof(beatsPerBar));
            MusicalMath.Finite(barOriginBeat, nameof(barOriginBeat));
            if (_meter == beatsPerBar && _barOriginBeat == barOriginBeat) return;
            _meter = beatsPerBar; _barOriginBeat = barOriginBeat;
            _generation = checked(_generation + 1);
            _continuityGeneration = checked(_continuityGeneration + 1);
        }

        public void ReplaceMap(IBeatMap beatMap)
        {
            if (beatMap == null) throw new ArgumentNullException(nameof(beatMap));
            Reanchor(true); _beatMap = beatMap;
        }

        public void SetExcerpt(double sourceStart, double sourceEnd)
        {
            MusicalMath.Finite(sourceStart, nameof(sourceStart));
            MusicalMath.Finite(sourceEnd, nameof(sourceEnd));
            if (sourceStart < 0 || sourceEnd <= sourceStart)
                throw new ArgumentException("Source excerpt must have positive duration.");
            Reanchor(true);
            _excerptStart = sourceStart; _excerptEnd = sourceEnd;
            _anchorSource = ClampExcerpt(_anchorSource);
            _loop = false;
        }

        public double ExcerptSecondsToSource(double excerptSeconds)
        {
            MusicalMath.Finite(excerptSeconds, nameof(excerptSeconds));
            double result = _excerptStart + excerptSeconds;
            if (excerptSeconds < 0 || result > _excerptEnd)
                throw new ArgumentOutOfRangeException(nameof(excerptSeconds));
            return result;
        }

        public void SetLoop(double sourceStart, double sourceEnd)
        {
            MusicalMath.Finite(sourceStart, nameof(sourceStart));
            MusicalMath.Finite(sourceEnd, nameof(sourceEnd));
            if (sourceStart < _excerptStart || sourceEnd > _excerptEnd || sourceEnd <= sourceStart)
                throw new ArgumentException("Loop must lie within the source excerpt.");
            Reanchor(true);
            _loopStart = sourceStart; _loopEnd = sourceEnd; _loop = true;
        }

        public void ClearLoop()
        {
            if (!_loop) return;
            Reanchor(true); _loop = false;
        }

        public BeatState Evaluate()
        {
            double now = ReadClock();
            double raw = RawPosition(now);
            double source = ClampExcerpt(raw);
            long iteration = 0;
            if (_loop && raw >= _loopEnd)
            {
                double count = Math.Floor((raw - _loopStart) / (_loopEnd - _loopStart));
                iteration = MusicalMath.FloorToLong(count);
                source = _loopStart + (raw - _loopStart) - count * (_loopEnd - _loopStart);
                // Protect against floating arithmetic falling outside the loop after a long session.
                source = Math.Max(_loopStart, Math.Min(_loopEnd, source));
            }
            bool started = now >= _anchorClock;
            bool playing = _running && started && (_loop || raw < _excerptEnd);
            var mapValue = _beatMap.Evaluate(source);
            double continuous = mapValue.Beat;
            if (_loop && iteration > 0)
                continuous += iteration * (_beatMap.Evaluate(_loopEnd).Beat - _beatMap.Evaluate(_loopStart).Beat);
            return new BeatState(source, mapValue.Beat, continuous, mapValue.Bpm, _meter,
                                 _barOriginBeat, _generation, iteration, playing, _continuityGeneration);
        }

        /// <summary>Independent deterministic sampling; does not change playback or scheduler state.</summary>
        public BeatState EvaluateSourceSeconds(double sourceSeconds)
        {
            var result = _beatMap.Evaluate(sourceSeconds);
            return new BeatState(sourceSeconds, result.Beat, result.Beat, result.Bpm,
                                 _meter, _barOriginBeat, _generation, 0, false, _continuityGeneration);
        }
    }
}
