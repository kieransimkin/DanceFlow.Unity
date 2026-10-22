using System;
using System.Collections.Generic;

namespace DanceFlow.Unity.Motion
{
    /// <summary>Interpolation is a playback choice; integer-pip native samples remain unchanged.</summary>
    public enum RecordingInterpolation { Step = 0, Linear = 1 }

    /// <summary>The independent constant-tempo source clock from a captured DanceRudiments recording.</summary>
    public sealed class RecordingClock
    {
        public double Bpm { get; }
        public string BpmStatus { get; }
        public string BpmSource { get; }
        public double BeatZeroSeconds { get; }
        public string PhaseStatus { get; }
        public int StartPip { get; }

        public RecordingClock(double bpm, string bpmStatus, string bpmSource,
                              double beatZeroSeconds, string phaseStatus, int startPip)
        {
            if (double.IsNaN(bpm) || double.IsInfinity(bpm) || bpm <= 0 ||
                double.IsInfinity(60.0 / bpm) || 60.0 / bpm / 64.0 <= 0)
                throw new ArgumentOutOfRangeException(nameof(bpm), "BPM must be finite, positive and representable.");
            if (bpmStatus != "estimated" && bpmStatus != "confirmed")
                throw new ArgumentException("Expected estimated or confirmed.", nameof(bpmStatus));
            if (string.IsNullOrWhiteSpace(bpmSource))
                throw new ArgumentException("A BPM source is mandatory.", nameof(bpmSource));
            if (phaseStatus != "unverified" && phaseStatus != "estimated" && phaseStatus != "supplied")
                throw new ArgumentException("Unsupported phase status.", nameof(phaseStatus));
            if (double.IsNaN(beatZeroSeconds) || double.IsInfinity(beatZeroSeconds))
                throw new ArgumentOutOfRangeException(nameof(beatZeroSeconds));
            Bpm = bpm;
            BpmStatus = bpmStatus;
            BpmSource = bpmSource;
            BeatZeroSeconds = beatZeroSeconds;
            PhaseStatus = phaseStatus;
            StartPip = startPip;
        }

        public double PipAtSourceSeconds(double sourceSeconds)
        {
            if (double.IsNaN(sourceSeconds) || double.IsInfinity(sourceSeconds))
                throw new ArgumentOutOfRangeException(nameof(sourceSeconds));
            // Divide by the representable native pip interval rather than multiplying
            // BPM by 64 first, which could overflow for otherwise valid source BPMs.
            double pip = (sourceSeconds - BeatZeroSeconds) / (60.0 / Bpm / 64.0);
            if (double.IsNaN(pip) || double.IsInfinity(pip))
                throw new ArgumentOutOfRangeException(nameof(sourceSeconds), "Recording pip overflows the source clock.");
            // Recover actual integer-pip timestamps lost solely to binary64 time arithmetic;
            // do NOT quantise a genuine fractional pip position.
            double nearest = Math.Round(pip);
            if (Math.Abs(pip - nearest) <= 1e-9) pip = nearest;
            return pip;
        }

        public double SourceSecondsAtPip(double absolutePip)
        {
            if (double.IsNaN(absolutePip) || double.IsInfinity(absolutePip))
                throw new ArgumentOutOfRangeException(nameof(absolutePip));
            double time = BeatZeroSeconds + absolutePip * (60.0 / Bpm / 64.0);
            if (double.IsNaN(time) || double.IsInfinity(time))
                throw new ArgumentOutOfRangeException(nameof(absolutePip));
            return time;
        }
    }

    /// <summary>Small immutable descriptor; never includes the heavyweight N-dimensional sample payload.</summary>
    public sealed class RecordingDescriptor
    {
        private readonly string[] _dimensions;
        private readonly Dictionary<string, int> _dimensionLookup;
        public string Name { get; }
        public RecordingClock Clock { get; }
        public int SampleCount { get; }
        public int DimensionCount => _dimensions.Length;
        public long EndPipExclusive => (long)Clock.StartPip + SampleCount;
        public string ProvenanceJson { get; }
        public string[] Dimensions => (string[])_dimensions.Clone();

        public RecordingDescriptor(string name, string[] dimensions, RecordingClock clock,
                                   int sampleCount, string provenanceJson)
        {
            ValidateName(name, nameof(name));
            if (dimensions == null || dimensions.Length == 0 || dimensions.Length > RecordingJson.MaxValues)
                throw new ArgumentException("Nonempty dimensions are required.", nameof(dimensions));
            if (sampleCount < 1 || (long)sampleCount * dimensions.Length > RecordingJson.MaxValues)
                throw new ArgumentOutOfRangeException(nameof(sampleCount));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if ((long)clock.StartPip + sampleCount > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(sampleCount), "Recording exceeds signed int32 native pip range.");
            if (provenanceJson == null || provenanceJson.Length < 2 ||
                provenanceJson[0] != '{' || provenanceJson[provenanceJson.Length - 1] != '}')
                throw new ArgumentException("Serialized provenance must be a JSON object.", nameof(provenanceJson));
            _dimensions = (string[])dimensions.Clone();
            _dimensionLookup = new Dictionary<string, int>(_dimensions.Length, StringComparer.Ordinal);
            for (int i = 0; i < _dimensions.Length; i++)
            {
                ValidateName(_dimensions[i], "dimensions[" + i + "]");
                if (_dimensionLookup.ContainsKey(_dimensions[i]))
                    throw new ArgumentException("Dimension identifiers must be unique.", nameof(dimensions));
                _dimensionLookup.Add(_dimensions[i], i);
            }
            Name = name;
            SampleCount = sampleCount;
            ProvenanceJson = provenanceJson;
        }

        internal static void ValidateName(string text, string field)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 128 || text[0] < 'a' || text[0] > 'z')
                throw new ArgumentException("Expected [a-z][a-z0-9_]{0,127}: " + field, field);
            for (int i = 1; i < text.Length; i++)
            {
                char c = text[i];
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_')
                    throw new ArgumentException("Invalid recording identifier: " + field, field);
            }
        }

        public string DimensionAt(int index) => _dimensions[index];

        public int ChannelIndex(string name)
        {
            if (name == null || !_dimensionLookup.TryGetValue(name, out int index))
                throw new KeyNotFoundException("Unknown recording channel: " + name);
            return index;
        }

        public bool TryChannelIndex(string name, out int index)
        {
            index = -1;
            return name != null && _dimensionLookup.TryGetValue(name, out index);
        }
    }

    /// <summary>
    /// Immutable N-dimensional float64 recording, explicitly bounded, never implicitly looped.
    /// Public sampling methods do not allocate a fresh result array per frame.
    /// </summary>
    public sealed class MotionRecording
    {
        private readonly double[] _interleaved;
        public RecordingDescriptor Descriptor { get; }
        public RecordingClock Clock => Descriptor.Clock;
        public int SampleCount => Descriptor.SampleCount;
        public int DimensionCount => Descriptor.DimensionCount;
        public long EndPipExclusive => Descriptor.EndPipExclusive;

        internal MotionRecording(RecordingDescriptor descriptor, double[] samples)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            if (samples == null || (long)samples.Length != (long)descriptor.SampleCount * descriptor.DimensionCount)
                throw new ArgumentException("Sample array length disagrees with dimensions.", nameof(samples));
            for (int i = 0; i < samples.Length; i++)
                if (double.IsNaN(samples[i]) || double.IsInfinity(samples[i]) || samples[i] < -1 || samples[i] > 1)
                    throw new ArgumentException("Recording sample outside finite [-1,1] bounds.", nameof(samples));
            // The parser/binary decoder has exclusive ownership of this newly allocated array.
            _interleaved = samples;
        }

        internal double[] NativeValues => _interleaved;

        private int RowForExactPip(int absolutePip)
        {
            long row = (long)absolutePip - Clock.StartPip;
            if (row < 0 || row >= SampleCount)
                throw new ArgumentOutOfRangeException(nameof(absolutePip), "Captured recordings never wrap outside their finite extent.");
            return (int)row;
        }

        private void ValidateAxis(int axis)
        {
            if (axis < 0 || axis >= DimensionCount) throw new ArgumentOutOfRangeException(nameof(axis));
        }

        public double SampleExact(int absolutePip, int axis)
        {
            ValidateAxis(axis);
            return _interleaved[RowForExactPip(absolutePip) * DimensionCount + axis];
        }

        public double SampleExact(int absolutePip, string channel) =>
            SampleExact(absolutePip, Descriptor.ChannelIndex(channel));

        public void SampleExactInto(int absolutePip, double[] destination)
        {
            if (destination == null || destination.Length < DimensionCount)
                throw new ArgumentException("Destination must hold every channel.", nameof(destination));
            Array.Copy(_interleaved, RowForExactPip(absolutePip) * DimensionCount,
                       destination, 0, DimensionCount);
        }

        private bool ValidPip(double absolutePip) =>
            !double.IsNaN(absolutePip) && !double.IsInfinity(absolutePip) &&
            absolutePip >= Clock.StartPip && absolutePip < EndPipExclusive;

        /// <summary>
        /// Step = native pip window. Linear = next native sample when available;
        /// final pip window holds the final value, but reading past it is an error.
        /// </summary>
        public double SampleAtPip(double absolutePip, int axis, RecordingInterpolation interpolation = RecordingInterpolation.Linear)
        {
            ValidateAxis(axis);
            if (interpolation != RecordingInterpolation.Step && interpolation != RecordingInterpolation.Linear)
                throw new ArgumentOutOfRangeException(nameof(interpolation));
            if (!ValidPip(absolutePip))
                throw new ArgumentOutOfRangeException(nameof(absolutePip), "Pip outside non-looping capture.");
            double relative = absolutePip - Clock.StartPip;
            int row = (int)Math.Floor(relative);
            double current = _interleaved[row * DimensionCount + axis];
            if (interpolation == RecordingInterpolation.Step || row + 1 == SampleCount) return current;
            double alpha = relative - row;
            double next = _interleaved[(row + 1) * DimensionCount + axis];
            return current + (next - current) * alpha;
        }

        public double SampleAtPip(double absolutePip, string channel,
                                  RecordingInterpolation interpolation = RecordingInterpolation.Linear) =>
            SampleAtPip(absolutePip, Descriptor.ChannelIndex(channel), interpolation);

        public double SampleAtSourceSeconds(double sourceSeconds, int axis,
                                            RecordingInterpolation interpolation = RecordingInterpolation.Linear) =>
            SampleAtPip(Clock.PipAtSourceSeconds(sourceSeconds), axis, interpolation);

        public bool TrySampleAtPip(double absolutePip, int axis, RecordingInterpolation interpolation, out double value)
        {
            ValidateAxis(axis);
            if (!ValidPip(absolutePip)) { value = 0; return false; }
            value = SampleAtPip(absolutePip, axis, interpolation);
            return true;
        }

        public void SampleAtPipInto(double absolutePip, double[] destination,
                                    RecordingInterpolation interpolation = RecordingInterpolation.Linear)
        {
            if (destination == null || destination.Length < DimensionCount)
                throw new ArgumentException("Destination must hold every channel.", nameof(destination));
            if (!ValidPip(absolutePip)) throw new ArgumentOutOfRangeException(nameof(absolutePip));
            if (interpolation != RecordingInterpolation.Step && interpolation != RecordingInterpolation.Linear)
                throw new ArgumentOutOfRangeException(nameof(interpolation));
            double relative = absolutePip - Clock.StartPip;
            int row = (int)Math.Floor(relative);
            double alpha = interpolation == RecordingInterpolation.Step || row + 1 == SampleCount ? 0 : relative - row;
            int offset = row * DimensionCount;
            for (int axis = 0; axis < DimensionCount; axis++)
            {
                double current = _interleaved[offset + axis];
                destination[axis] = alpha == 0 ? current : current +
                    (_interleaved[offset + DimensionCount + axis] - current) * alpha;
            }
        }
    }
}
