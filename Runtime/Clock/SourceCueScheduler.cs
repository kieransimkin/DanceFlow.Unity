using System;
using System.Collections.Generic;

namespace DanceFlow.Unity.Clock
{
    /// <summary>Data-only cue at an exact original-master timestamp; cue text is never executed.</summary>
    public readonly struct SourceCue
    {
        public readonly double SourceSeconds;
        public readonly string Name;
        public SourceCue(double sourceSeconds, string name)
        {
            SourceSeconds = MusicalMath.Finite(sourceSeconds, nameof(sourceSeconds));
            if (sourceSeconds < 0) throw new ArgumentOutOfRangeException(nameof(sourceSeconds));
            if (string.IsNullOrEmpty(name) || name.Length > 256) throw new ArgumentException("Cue name is required (maximum 256 characters).", nameof(name));
            Name = name;
        }
    }

    /// <summary>
    /// Source-second cue scheduler. Supports excerpt/loop transitions without deriving cue time
    /// from variable BPM. Never executes a user-supplied cue as code or a Unity method name.
    /// </summary>
    public sealed class SourceCueScheduler
    {
        private const int MaxDeliveredPerAdvance = 256;
        private readonly SourceCue[] _cues;
        private readonly MusicalTransport _transport;
        private bool _hasLast;
        private BeatState _last;

        public SeekEventPolicy Seeks { get; set; } = SeekEventPolicy.Suppress;
        public long DroppedCues { get; private set; }

        public SourceCueScheduler(MusicalTransport transport, IReadOnlyList<SourceCue> cues)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            if (cues == null || cues.Count > 100000) throw new ArgumentException("At most 100,000 cues allowed.", nameof(cues));
            var indexed = new List<KeyValuePair<int, SourceCue>>(cues.Count);
            for (int i = 0; i < cues.Count; i++)
            {
                if (string.IsNullOrEmpty(cues[i].Name)) throw new ArgumentException("Invalid cue.", nameof(cues));
                indexed.Add(new KeyValuePair<int, SourceCue>(i, cues[i]));
            }
            indexed.Sort((a, b) =>
            {
                int c = a.Value.SourceSeconds.CompareTo(b.Value.SourceSeconds);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            _cues = new SourceCue[indexed.Count];
            for (int i = 0; i < indexed.Count; i++) _cues[i] = indexed[i].Value;
        }

        public void Reset() { _hasLast = false; }

        private int LowerBound(double position, bool includeEqual)
        {
            int lo = 0, hi = _cues.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (_cues[mid].SourceSeconds < position ||
                    (!includeEqual && _cues[mid].SourceSeconds == position)) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private int CountInRange(double from, double to, bool includeFrom)
        {
            if (to < from) return 0;
            return LowerBound(to, false) - LowerBound(from, includeFrom);
        }

        private int Deliver(double from, double to, bool includeFrom, int remaining, Action<SourceCue> emit)
        {
            if (to < from) return remaining;
            int begin = LowerBound(from, includeFrom);
            int end = LowerBound(to, false);
            for (int i = begin; i < end; i++)
            {
                if (remaining > 0) { emit(_cues[i]); remaining--; }
                else DroppedCues++;
            }
            return remaining;
        }

        public void Advance(BeatState state, Action<SourceCue> emit)
        {
            if (emit == null) throw new ArgumentNullException(nameof(emit));
            if (!_hasLast) { _last = state; _hasLast = true; return; }
            var prev = _last;
            _last = state;
            if (!state.IsPlaying ||
                (prev.ContinuityGeneration != state.ContinuityGeneration && Seeks == SeekEventPolicy.Suppress)) return;
            int remaining = MaxDeliveredPerAdvance;
            if (_transport.HasLoop && state.LoopIteration > prev.LoopIteration)
            {
                remaining = Deliver(prev.SourceSeconds, _transport.LoopEnd, false, remaining, emit);
                long full = state.LoopIteration - prev.LoopIteration - 1;
                // Drop arbitrarily old cycles instead of emitting a huge backlog on a stalled frame.
                long replay = Math.Min(full, 8);
                for (int i = 0; i < replay; i++)
                    remaining = Deliver(_transport.LoopStart, _transport.LoopEnd, true, remaining, emit);
                if (full > replay)
                {
                    long perLoop = CountInRange(_transport.LoopStart, _transport.LoopEnd, true);
                    DroppedCues = checked(DroppedCues + Math.Min(long.MaxValue - DroppedCues, perLoop * Math.Min(full - replay, 1000000)));
                }
                Deliver(_transport.LoopStart, state.SourceSeconds, true, remaining, emit);
            }
            else if (state.LoopIteration == prev.LoopIteration && state.SourceSeconds >= prev.SourceSeconds)
            {
                // A cue on an excerpt's first instant is a real cue, not an earlier skip.
                // Do not repeat it after an ordinary mid-song pause/resume or a seek.
                bool initialStart = !prev.IsPlaying && state.IsPlaying &&
                    prev.SourceSeconds == _transport.ExcerptStart &&
                    prev.ContinuityGeneration == state.ContinuityGeneration;
                Deliver(prev.SourceSeconds, state.SourceSeconds, initialStart, remaining, emit);
            }
        }
    }
}
