using System;
using System.Collections.Generic;
using DanceFlow.Unity.Motion;

namespace DanceFlow.Unity.Rigging
{
    /// <summary>Double-precision quaternion (wxyz), independent of Unity and of graphics APIs.</summary>
    public readonly struct RigQuaternion
    {
        public readonly double W, X, Y, Z;
        public RigQuaternion(double w, double x, double y, double z) { W = w; X = x; Y = y; Z = z; }
        public double SquaredLength => W * W + X * X + Y * Y + Z * Z;
        public RigQuaternion Normalized()
        {
            double norm = Math.Sqrt(SquaredLength);
            if (norm < 1e-12 || double.IsNaN(norm) || double.IsInfinity(norm))
                throw new FormatException("Rig quaternion must have a finite nonzero norm.");
            return new RigQuaternion(W / norm, X / norm, Y / norm, Z / norm);
        }
        public static RigQuaternion Slerp(RigQuaternion a, RigQuaternion b, double t)
        {
            if (t <= 0) return a;
            if (t >= 1) return b;
            a = a.Normalized(); b = b.Normalized();
            double dot = a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            if (dot < 0) { dot = -dot; b = new RigQuaternion(-b.W, -b.X, -b.Y, -b.Z); }
            dot = Math.Min(1, Math.Max(-1, dot));
            if (dot > 0.9995)
                return new RigQuaternion(a.W + t * (b.W - a.W), a.X + t * (b.X - a.X),
                    a.Y + t * (b.Y - a.Y), a.Z + t * (b.Z - a.Z)).Normalized();
            double theta = Math.Acos(dot);
            double denominator = Math.Sin(theta);
            double f = Math.Sin((1 - t) * theta) / denominator;
            double g = Math.Sin(t * theta) / denominator;
            return new RigQuaternion(a.W * f + b.W * g, a.X * f + b.X * g,
                a.Y * f + b.Y * g, a.Z * f + b.Z * g);
        }
    }

    internal readonly struct QuaternionColumns
    {
        public readonly int W, X, Y, Z;
        public QuaternionColumns(int w, int x, int y, int z) { W=w; X=x; Y=y; Z=z; }
        public RigQuaternion Read(double[] row) => new RigQuaternion(row[W], row[X], row[Y], row[Z]);
        public void Write(RigQuaternion q, double[] row)
        { row[W]=q.W; row[X]=q.X; row[Y]=q.Y; row[Z]=q.Z; }
    }

    /// <summary>
    /// Source recording values remain normalized [-1,1]. Decode to *physical* rig channels,
    /// preserve exact native integer pips, hold discrete selectors, and slerp quaternion groups.
    /// This does not infer skeleton identity or create a second musical clock.
    /// </summary>
    public sealed class RigPoseSampler
    {
        private readonly MotionRecording _recording;
        private readonly RigProfile _profile;
        private readonly QuaternionColumns[] _quaternionGroups;
        private readonly double[] _first;
        private readonly double[] _second;
        public RigProfile Profile => _profile;
        public RecordingClock Clock => _recording.Clock;
        public int StartPip => Clock.StartPip;
        public long EndPipExclusive => _recording.EndPipExclusive;
        public int ChannelCount => _profile.ChannelCount;

        public RigPoseSampler(RigProfile profile, MotionRecording recording)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _recording = recording ?? throw new ArgumentNullException(nameof(recording));
            profile.RequireMatchingRecording(recording.Descriptor);
            var groups = new Dictionary<string, int[]>(StringComparer.Ordinal);
            for (int i = 0; i < profile.ChannelCount; i++)
            {
                var c = profile.ChannelAt(i);
                if (c.Interpolation != RigInterpolation.Quaternion) continue;
                if (!groups.TryGetValue(c.Bone, out int[] group))
                    groups.Add(c.Bone, group = new[] { -1, -1, -1, -1 });
                group[c.ArrayIndex] = i;
            }
            var tuples = new List<QuaternionColumns>();
            foreach (var pair in groups)
            {
                var g = pair.Value;
                if (g[0] < 0 || g[1] < 0 || g[2] < 0 || g[3] < 0)
                    throw new FormatException("Incomplete rig quaternion: " + pair.Key);
                tuples.Add(new QuaternionColumns(g[0], g[1], g[2], g[3]));
            }
            _quaternionGroups = tuples.ToArray();
            _first = new double[profile.ChannelCount];
            _second = new double[profile.ChannelCount];
            // Scan every integer pip *once* so invalid discrete states and malformed rotations
            // are rejected before a rig can animate or allocate Unity Playables handles.
            var check = new double[profile.ChannelCount];
            for (int row = 0; row < recording.SampleCount; row++)
            {
                recording.SampleExactInto(checked(recording.Clock.StartPip + row), check);
                DecodeInPlace(check);
            }
        }

        private void DecodeInPlace(double[] values)
        {
            for (int i=0;i<values.Length;i++) values[i]=_profile.ChannelAt(i).Decode(values[i]);
            foreach (var g in _quaternionGroups)
            {
                var q = g.Read(values);
                if (Math.Abs(q.SquaredLength - 1) > 2e-5)
                    throw new FormatException("Rig quaternion is not unit length.");
            }
        }

        public void SampleExactInto(int absolutePip, double[] destination)
        {
            ValidateBuffer(destination);
            _recording.SampleExactInto(absolutePip, destination);
            DecodeInPlace(destination);
        }

        public void SampleAtPipInto(double absolutePip, double[] destination)
        {
            ValidateBuffer(destination);
            if (double.IsNaN(absolutePip) || double.IsInfinity(absolutePip) ||
                absolutePip < StartPip || absolutePip >= EndPipExclusive)
                throw new ArgumentOutOfRangeException(nameof(absolutePip), "Rig recordings do not loop or hold beyond their finite extent.");
            int pip = checked((int)Math.Floor(absolutePip));
            double fraction = absolutePip - pip;
            _recording.SampleExactInto(pip, _first);
            DecodeInPlace(_first);
            if (fraction <= 0 || (long)pip + 1 >= EndPipExclusive)
            {
                Array.Copy(_first, destination, ChannelCount);
                return;
            }
            _recording.SampleExactInto(checked(pip + 1), _second);
            DecodeInPlace(_second);
            for (int i=0;i<ChannelCount;i++)
                destination[i] = _profile.ChannelAt(i).Interpolation == RigInterpolation.Linear
                    ? _first[i] + (_second[i]-_first[i])*fraction : _first[i];
            foreach (var g in _quaternionGroups)
                g.Write(RigQuaternion.Slerp(g.Read(_first), g.Read(_second), fraction), destination);
        }

        private void ValidateBuffer(double[] destination)
        {
            if (destination == null || destination.Length < ChannelCount)
                throw new ArgumentException("Destination must hold every rig channel.", nameof(destination));
        }

        /// <summary>Physically correct pose crossfade. Masks/weights belong to the retarget adapter.</summary>
        public void BlendInto(double[] a, double[] b, double weight, double[] destination)
        {
            ValidateBuffer(a); ValidateBuffer(b); ValidateBuffer(destination);
            if (double.IsNaN(weight) || weight < 0 || weight > 1)
                throw new ArgumentOutOfRangeException(nameof(weight));
            for (int i=0;i<ChannelCount;i++)
                destination[i] = _profile.ChannelAt(i).Interpolation == RigInterpolation.Linear
                    ? a[i] + (b[i]-a[i])*weight : weight < 0.5 ? a[i] : b[i];
            foreach (var g in _quaternionGroups)
                g.Write(RigQuaternion.Slerp(g.Read(a), g.Read(b), weight), destination);
        }
    }
}
