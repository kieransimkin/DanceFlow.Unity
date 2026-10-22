using System;
using DanceFlow.Unity.Clock;

namespace DanceFlow.Unity.Motion
{
    /// <summary>Explicitly separates native source seconds from deliberately retimed musical beats.</summary>
    public enum RecordingTimeDomain { OriginalSourceSeconds = 0, ExplicitMusicalRetiming = 1 }

    /// <summary>
    /// Immutable mapping from a Phase 1 BeatState into a recording's independent pip clock.
    /// A recording's BPM and origin never silently become the master song's BPM.
    /// </summary>
    public readonly struct RecordingTimeMapping
    {
        public readonly RecordingTimeDomain Domain;
        public readonly double MasterAnchorBeat;
        public readonly double RecordingAnchorPip;
        public readonly double RecordingPipsPerMasterBeat;
        public readonly bool AcknowledgeUnverifiedPhase;

        private RecordingTimeMapping(RecordingTimeDomain domain, double masterAnchorBeat,
                                     double recordingAnchorPip, double pipsPerBeat, bool acknowledge)
        {
            Domain = domain;
            MasterAnchorBeat = masterAnchorBeat;
            RecordingAnchorPip = recordingAnchorPip;
            RecordingPipsPerMasterBeat = pipsPerBeat;
            AcknowledgeUnverifiedPhase = acknowledge;
        }

        /// <summary>
        /// Use when the owner has intentionally associated this recording's source timeline
        /// with BeatState.SourceSeconds. Canonical JSON does not itself prove audio identity.
        /// </summary>
        public static RecordingTimeMapping OriginalSourceSeconds() =>
            new RecordingTimeMapping(RecordingTimeDomain.OriginalSourceSeconds, 0, 0, 0, false);

        /// <summary>
        /// Declare an explicit target song beat -> absolute recording pip correspondence.
        /// Defaults to one quarter-note beat = 64 recorded pips; no implicit source BPM substitution.
        /// </summary>
        public static RecordingTimeMapping ExplicitBeatRetiming(double masterAnchorBeat,
            double recordingAnchorPip, double recordingPipsPerMasterBeat = 64,
            bool acknowledgeUnverifiedPhase = false)
        {
            if (!Finite(masterAnchorBeat) || !Finite(recordingAnchorPip) ||
                !Finite(recordingPipsPerMasterBeat) || recordingPipsPerMasterBeat <= 0)
                throw new ArgumentOutOfRangeException(nameof(recordingPipsPerMasterBeat));
            return new RecordingTimeMapping(RecordingTimeDomain.ExplicitMusicalRetiming,
                masterAnchorBeat, recordingAnchorPip, recordingPipsPerMasterBeat, acknowledgeUnverifiedPhase);
        }

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        public double RecordingPip(BeatState state, RecordingClock recordingClock)
        {
            if (recordingClock == null) throw new ArgumentNullException(nameof(recordingClock));
            if (Domain == RecordingTimeDomain.OriginalSourceSeconds)
                return recordingClock.PipAtSourceSeconds(state.SourceSeconds);
            if (Domain != RecordingTimeDomain.ExplicitMusicalRetiming)
                throw new InvalidOperationException("Unsupported recording time domain.");
            if (recordingClock.PhaseStatus == "unverified" && !AcknowledgeUnverifiedPhase)
                throw new InvalidOperationException("Unverified capture phase requires explicit approval to retime.");
            double pip = RecordingAnchorPip +
                (state.AbsoluteBeat - MasterAnchorBeat) * RecordingPipsPerMasterBeat;
            if (!Finite(pip)) throw new OverflowException("Retimed pip position is not finite.");
            return pip;
        }
    }
}
