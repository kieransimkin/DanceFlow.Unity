using System;
using System.Collections.Generic;

namespace DanceFlow.Unity.Clock
{
    /// <summary>Fixed quarter-note BPM and source timestamp that maps to beat zero.</summary>
    public sealed class FixedBeatMap : IBeatMap
    {
        public double Bpm { get; }
        public double BeatZeroSeconds { get; }
        public FixedBeatMap(double bpm, double beatZeroSeconds = 0)
        {
            Bpm = MusicalMath.Finite(bpm, nameof(bpm));
            BeatZeroSeconds = MusicalMath.Finite(beatZeroSeconds, nameof(beatZeroSeconds));
            if (bpm <= 0) throw new ArgumentOutOfRangeException(nameof(bpm));
        }
        public BeatPosition Evaluate(double sourceSeconds) =>
            new BeatPosition((MusicalMath.Finite(sourceSeconds, nameof(sourceSeconds)) - BeatZeroSeconds) * Bpm / 60.0, Bpm);
        public double SecondsAtBeat(double beat) => BeatZeroSeconds + MusicalMath.Finite(beat, nameof(beat)) * 60.0 / Bpm;
    }

    public readonly struct BeatAnchor
    {
        public readonly int Beat;
        public readonly long SourceFrame;
        public BeatAnchor(int beat, long sourceFrame) { Beat = beat; SourceFrame = sourceFrame; }
    }

    /// <summary>
    /// StemLab's source-frame anchor interpolation, including missing beat ordinals and
    /// nearest-interval extrapolation. Source time is NEVER quantised to a new beat.
    /// </summary>
    public sealed class VariableBeatMap : IBeatMap
    {
        private readonly BeatAnchor[] _anchors;
        public int SampleRate { get; }
        public long SourceFrames { get; }
        public int OriginBeat { get; }
        public string SourceSha256 { get; }
        public string ConsensusSha256 { get; }
        public string ReviewStatus { get; }
        public int AnchorCount => _anchors.Length;

        public VariableBeatMap(IReadOnlyList<BeatAnchor> anchors, int sampleRate, long sourceFrames,
                               int originBeat, string sourceSha256, string consensusSha256,
                               string reviewStatus)
        {
            if (anchors == null || anchors.Count < 2 || anchors.Count > 25000)
                throw new ArgumentException("Two to 25,000 anchors are required.", nameof(anchors));
            if (sampleRate < 1 || sampleRate > 768000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (sourceFrames < 2 || sourceFrames > 10000000000000L)
                throw new ArgumentOutOfRangeException(nameof(sourceFrames));
            if (!StemLabBeatTimeline.IsSha256(sourceSha256) || !StemLabBeatTimeline.IsSha256(consensusSha256))
                throw new ArgumentException("Source and consensus SHA-256 values must be lowercase hex.");
            if (reviewStatus != "estimated" && reviewStatus != "artist_confirmed")
                throw new ArgumentException("Unsupported beat-map review status.", nameof(reviewStatus));
            SampleRate = sampleRate;
            SourceFrames = sourceFrames;
            OriginBeat = originBeat;
            SourceSha256 = sourceSha256;
            ConsensusSha256 = consensusSha256;
            ReviewStatus = reviewStatus;
            _anchors = new BeatAnchor[anchors.Count];
            bool foundOrigin = false;
            for (int i = 0; i < anchors.Count; i++)
            {
                var a = anchors[i];
                if (a.Beat < 0 || a.SourceFrame < 0 || a.SourceFrame >= sourceFrames)
                    throw new ArgumentException("Anchor outside source bounds.", nameof(anchors));
                if (i > 0 && (a.SourceFrame <= anchors[i - 1].SourceFrame || a.Beat <= anchors[i - 1].Beat))
                    throw new ArgumentException("Beat and source frame must increase strictly.", nameof(anchors));
                if (a.Beat == originBeat) foundOrigin = true;
                _anchors[i] = a;
            }
            if (!foundOrigin) throw new ArgumentException("Beat 1 origin must identify an actual anchor.");
        }

        public BeatAnchor AnchorAt(int index) => _anchors[index];

        private int SegmentForFrame(double frame)
        {
            int lo = 0, hi = _anchors.Length;
            // bisect_right: exact interior boundary belongs to the following segment.
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (_anchors[mid].SourceFrame <= frame) lo = mid + 1;
                else hi = mid;
            }
            return Math.Max(0, Math.Min(_anchors.Length - 2, lo - 1));
        }

        private int SegmentForBeat(double beat)
        {
            int lo = 0, hi = _anchors.Length;
            double absolute = beat + OriginBeat;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (_anchors[mid].Beat <= absolute) lo = mid + 1;
                else hi = mid;
            }
            return Math.Max(0, Math.Min(_anchors.Length - 2, lo - 1));
        }

        public BeatPosition Evaluate(double sourceSeconds)
        {
            double frame = MusicalMath.Finite(sourceSeconds, nameof(sourceSeconds)) * SampleRate;
            MusicalMath.Finite(frame, nameof(sourceSeconds));
            // Match FrameRudiments: recover integer native sample frames lost only to
            // floating representation, but preserve genuine sub-frame positions.
            double nearest = Math.Floor(frame + 0.5);
            if (Math.Abs(frame - nearest) <= 1e-6) frame = nearest;
            return EvaluateFrame(frame);
        }

        public BeatPosition EvaluateFrame(double sourceFrame)
        {
            MusicalMath.Finite(sourceFrame, nameof(sourceFrame));
            int i = SegmentForFrame(sourceFrame);
            var a = _anchors[i]; var b = _anchors[i + 1];
            double deltaFrames = (double)b.SourceFrame - a.SourceFrame;
            double deltaBeats = (double)b.Beat - a.Beat;
            double beat = (a.Beat - (double)OriginBeat) +
                          ((sourceFrame - a.SourceFrame) / deltaFrames) * deltaBeats;
            return new BeatPosition(beat, (60.0 * SampleRate / deltaFrames) * deltaBeats);
        }

        public double SecondsAtBeat(double beat)
        {
            MusicalMath.Finite(beat, nameof(beat));
            int i = SegmentForBeat(beat);
            var a = _anchors[i]; var b = _anchors[i + 1];
            double absolute = beat + OriginBeat;
            return (a.SourceFrame + (absolute - a.Beat) *
                    ((double)b.SourceFrame - a.SourceFrame) / (b.Beat - (double)a.Beat)) / SampleRate;
        }
    }
}
