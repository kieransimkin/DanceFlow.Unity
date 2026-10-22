using System;
using System.Collections.Generic;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.Motion;
using NUnit.Framework;

namespace DanceFlow.Unity.Tests
{
    public sealed class RecordingImportTests
    {
        private const string Json = @"{
            ""format"":""dancerudiments.recording"", ""schema_version"":1,
            ""pips_per_beat"":64, ""sample_dtype"":""float64"", ""sample_decimals"":12,
            ""name"":""two_axes"", ""dimensions"":[""bass"",""drums""],
            ""clock"":{""bpm"":145,""bpm_source"":""test"",""bpm_status"":""estimated"",
                       ""beat_zero_seconds"":0.25,""phase_status"":""unverified"",""start_pip"":-2},
            ""samples"":[[0.2,1],[-1,0],[0.12345678901234,-0.3]],
            ""provenance"":{""producer"":""synthetic""}
        }";

        [Test]
        public void ExactPipsKeepAllDimensionsAndNeverWrap()
        {
            var r = RecordingJson.Parse(Json);
            Assert.AreEqual(2, r.DimensionCount);
            Assert.AreEqual(-2, r.Clock.StartPip);
            Assert.AreEqual(1, r.EndPipExclusive);
            Assert.AreEqual(1, r.SampleExact(-2, "drums"), 1e-12);
            Assert.AreEqual(-1, r.SampleExact(-1, "bass"), 1e-12);
            Assert.AreEqual(0.123456789012, r.SampleExact(0, "bass"), 1e-12);
            Assert.Throws<ArgumentOutOfRangeException>(() => r.SampleExact(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => r.SampleExact(-3, 0));
        }

        [Test]
        public void FractionalPipAndSourceClockAreDeterministic()
        {
            var r = RecordingJson.Parse(Json);
            Assert.AreEqual(-0.4, r.SampleAtPip(-1.5, "bass"), 1e-12);
            Assert.AreEqual(0.2, r.SampleAtPip(-1.5, "bass", RecordingInterpolation.Step), 1e-12);
            for (int pip = -2; pip <= 0; pip++)
                Assert.AreEqual(r.SampleExact(pip, 0),
                    r.SampleAtSourceSeconds(r.Clock.SourceSecondsAtPip(pip), 0), 1e-12);
        }

        [Test]
        public void BinaryCacheChecksBothBytesAndDescriptor()
        {
            var r = RecordingJson.Parse(Json);
            var bytes = RecordingBinaryCodec.Encode(r);
            var restored = RecordingBinaryCodec.Decode(bytes, r.Descriptor,
                RecordingBinaryCodec.Sha256(bytes));
            Assert.AreEqual(r.SampleExact(0, 0), restored.SampleExact(0, 0));
            var wrong = new RecordingDescriptor("incorrect", r.Descriptor.Dimensions, r.Clock,
                r.SampleCount, r.Descriptor.ProvenanceJson);
            Assert.Throws<FormatException>(() => RecordingBinaryCodec.Decode(bytes, wrong,
                RecordingBinaryCodec.Sha256(bytes)));
            bytes[bytes.Length - 1] ^= 1;
            Assert.Throws<FormatException>(() => RecordingBinaryCodec.Decode(bytes, r.Descriptor,
                RecordingBinaryCodec.Sha256(RecordingBinaryCodec.Encode(r))));
        }

        [Test]
        public void UnverifiedCaptureMustExplicitlyApproveMusicalRetiming()
        {
            var r = RecordingJson.Parse(Json);
            var state = new BeatState(0, 2, 2, 120, 4, 0, 0, 0, false);
            Assert.Throws<InvalidOperationException>(() =>
                RecordingTimeMapping.ExplicitBeatRetiming(2, -2).RecordingPip(state, r.Clock));
            Assert.AreEqual(-2, RecordingTimeMapping.ExplicitBeatRetiming(2, -2, 64, true)
                .RecordingPip(state, r.Clock), 1e-12);
        }

        [Test]
        public void RejectDuplicateKeysAndMalformedNativeSchema()
        {
            Assert.Throws<FormatException>(() => RecordingJson.Parse(Json.Replace(
                "\"name\":\"two_axes\"", "\"name\":\"two_axes\",\"name\":\"duplicate\"")));
            Assert.Throws<FormatException>(() => RecordingJson.Parse(Json.Replace(
                "\"sample_decimals\":12", "\"sample_decimals\":10")));
            Assert.Throws<FormatException>(() => RecordingJson.Parse(Json.Replace(
                "\"dimensions\":[\"bass\",\"drums\"]", "\"dimensions\":[\"bass\",\"bass\"]")));
        }
    }
}
