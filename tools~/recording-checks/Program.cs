using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.Motion;

internal static class Program
{
    private static int checks;
    private static void Require(bool success, string message)
    {
        if (!success) throw new Exception("Phase 3 recording parity failed: " + message);
        checks++;
    }
    private static void Near(double result, double wanted, string label, double tolerance = 1e-11) =>
        Require(Math.Abs(result - wanted) <= tolerance, label + " (" + result + " vs " + wanted + ")");

    private static void Reject(Action action, string label)
    {
        bool threw = false;
        try { action(); }
        catch (ArgumentException) { threw = true; }
        catch (FormatException) { threw = true; }
        catch (InvalidOperationException) { threw = true; }
        Require(threw, label);
    }

    private static string Mutate(string raw, Action<JsonObject> edit)
    {
        var source = JsonNode.Parse(raw)!.AsObject();
        edit(source);
        return source.ToJsonString();
    }

    private static void Main()
    {
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "Tests", "Fixtures");
        string path = Path.Combine(basePath, "dancerudiments-four-axes-v1.json");
        byte[] rawBytes = File.ReadAllBytes(path);
        string raw = Encoding.UTF8.GetString(rawBytes);
        var recording = RecordingJson.ParseUtf8(rawBytes);
        var d = recording.Descriptor;
        var receipt = JsonNode.Parse(File.ReadAllText(Path.Combine(basePath,
            "dancerudiments-four-axes-v1.receipt.json")))!.AsObject();
        Require(RecordingBinaryCodec.Sha256(rawBytes) == (string)receipt["source_sha256"]!,
            "Fixture source bytes match pinned SHA-256");
        Require(d.Name == "four_axes" && d.DimensionCount == 4 && d.SampleCount == 6,
            "All four named dimensions and original row count retained");
        Require(d.DimensionAt(2) == "drums" && d.ChannelIndex("vocals") == 3,
            "Named-channel lookup preserves native order");
        Require(d.Clock.StartPip == -32 && d.EndPipExclusive == -26,
            "Native signed start pip and exclusive finite extent");
        Near(recording.SampleExact(-32, 0), 0.2, "Native first bass sample");
        Near(recording.SampleExact(-30, 2), 0.123456789012, "Float64 twelve-decimal source sample");
        Near(recording.SampleAtPip(-31.5, 0), 0.25, "Linear fractional pip interpolation");
        Near(recording.SampleAtPip(-31.5, 0, RecordingInterpolation.Step), 0.3,
            "Exact pip-window (step) sampling");
        Near(recording.SampleAtPip(-26.5, 0), -0.1, "Final pip window holds final sample");
        Reject(() => recording.SampleExact(-33, 0), "Native sample before capture rejects");
        Reject(() => recording.SampleExact(-26, 0), "Native sample at exclusive end rejects");
        Reject(() => recording.SampleAtPip(-26.0, 0), "Interpolation never wraps at capture end");
        var row = new double[4];
        recording.SampleExactInto(-30, row);
        Require(row.SequenceEqual(new[] { -1.0, 0.0, 0.123456789012, 1.0 }),
            "Allocation-free N-dimensional exact row parity");
        for (int pip = -32; pip < -26; pip++)
        {
            double t = recording.Clock.SourceSecondsAtPip(pip);
            Near(recording.SampleAtSourceSeconds(t, 0), recording.SampleExact(pip, 0),
                "Source clock exact-pip time(" + pip + ") parity");
        }
        double missing;
        Require(!recording.TrySampleAtPip(-40, 0, RecordingInterpolation.Linear, out missing),
            "Bounds-aware optional lookup returns false without fake looping");

        byte[] cached = RecordingBinaryCodec.Encode(recording);
        Require(RecordingBinaryCodec.Sha256(cached) == (string)receipt["cache_sha256"]!,
            "Exact little-endian DFR1 bytes agree with independent Python receipt");
        Require(cached.Length == (int)receipt["cache_bytes"]!,
            "Recorded binary format has the specified header and payload lengths");
        var opened = RecordingBinaryCodec.Decode(cached, d, RecordingBinaryCodec.Sha256(cached));
        for (int pip = -32; pip < -26; pip++)
            for (int axis = 0; axis < 4; axis++)
                Require(opened.SampleExact(pip, axis) == recording.SampleExact(pip, axis),
                    "Binary cache float64 roundtrip pip " + pip + ", channel " + axis);
        byte[] damaged = (byte[])cached.Clone(); damaged[damaged.Length - 1] ^= 1;
        Reject(() => RecordingBinaryCodec.Decode(damaged, d, RecordingBinaryCodec.Sha256(cached)),
            "Cache tampering cannot pass original SHA-256");
        var wrongDims = new RecordingDescriptor(d.Name, new[] { "vocals", "treble", "drums", "bass" },
            d.Clock, d.SampleCount, d.ProvenanceJson);
        Reject(() => RecordingBinaryCodec.Decode(cached, wrongDims, RecordingBinaryCodec.Sha256(cached)),
            "Cache metadata hash catches axis reordering");

        // Master-beat remapping is explicit, and requires approval when phase is unverified.
        var state = new BeatState(0.25, 4, 4, 130, 4, 0, 3, 0, true);
        var retimed = RecordingTimeMapping.ExplicitBeatRetiming(4, -32);
        Reject(() => retimed.RecordingPip(state, d.Clock),
            "Unverified phase cannot be retimed without approval");
        Near(RecordingTimeMapping.ExplicitBeatRetiming(4, -32, 64, true)
            .RecordingPip(state, d.Clock), -32, "Explicit master beat anchor maps to recording pip");
        Near(RecordingTimeMapping.OriginalSourceSeconds().RecordingPip(state, d.Clock), 0,
            "Native recording source time does not inherit master BPM");

        Reject(() => RecordingJson.Parse(Mutate(raw, x => x["extra"] = 1)),
            "Unknown top-level schema field rejected");
        Reject(() => RecordingJson.Parse(Mutate(raw, x => x["clock"]!.AsObject()["extra"] = 1)),
            "Unknown clock field rejected");
        Reject(() => RecordingJson.Parse(raw.Replace("\"schema_version\": 1,", "\"schema_version\": 1.0,")),
            "Schema version float type rejected");
        Reject(() => RecordingJson.Parse(Mutate(raw, x => x["dimensions"] =
            new JsonArray(JsonValue.Create("x"), JsonValue.Create("x")))),
            "Duplicate dimensions rejected");
        Reject(() => RecordingJson.Parse(Mutate(raw, x => x["samples"]!.AsArray()[0] =
            new JsonArray(JsonValue.Create(0.1)))),
            "Ragged rows rejected");
        Reject(() => RecordingJson.Parse(Mutate(raw, x => x["samples"]!.AsArray()[0]!.AsArray()[0] = 2)),
            "Out-of-range values rejected");
        Reject(() => RecordingJson.Parse(Mutate(raw, x => x["clock"]!.AsObject()["bpm"] = 0)),
            "Zero BPM rejected");
        Reject(() => RecordingJson.Parse(Mutate(raw, x => x["clock"]!.AsObject()["phase_status"] = "confirmed")),
            "Invented phase confirmation rejected");
        Reject(() => RecordingJson.Parse(Mutate(raw, x => x["clock"]!.AsObject()["start_pip"] = int.MaxValue)),
            "Native signed int pip extent overflow rejected");
        Reject(() => RecordingJson.Parse(raw.Insert(raw.IndexOf("\"format\""), "\"format\":\"duplicate\",")),
            "Duplicate JSON object keys rejected by shared Phase 1 strict parser");
        Reject(() => RecordingJson.Parse(raw.Replace("0.2", "1e999")),
            "Non-finite JSON number rejected");
        Reject(() => RecordingJson.Parse(raw.Replace("\"provenance\": {", "\"provenance\": [")),
            "Non-object inert provenance rejected");
        Reject(() => RecordingJson.Parse("{}"), "Malformed partial header rejected");

        // The native format supports recordings longer than 65,535 pips without a legacy truncation.
        var large = new double[65537];
        large[65536] = 0.75;
        var longDescriptor = new RecordingDescriptor("long_capture", new[] { "x" },
            new RecordingClock(120, "estimated", "fixture", 0, "unverified", -8),
            large.Length, "{}");
        var longMotion = new MotionRecording(longDescriptor, large);
        Near(longMotion.SampleExact(65528, 0), 0.75, "Native long recording beyond 65535 pips");
        Require(RecordingBinaryCodec.Decode(RecordingBinaryCodec.Encode(longMotion), longDescriptor,
            RecordingBinaryCodec.Sha256(RecordingBinaryCodec.Encode(longMotion))).SampleCount == 65537,
            "Long binary cache is not truncated to legacy ushort samples");

        // Optional recording provenance may claim a master SHA. Absence is NOT proof.
        Require(!RecordingSourceIdentity.HasMatchingProvenanceHash(d, new string('a', 64)),
            "Unclaimed provenance is not falsely represented as an audio hash match");
        var asserted = new RecordingDescriptor(d.Name, d.Dimensions, d.Clock, d.SampleCount,
            "{\"source_sha256\":\"" + new string('a', 64) + "\"}");
        Require(RecordingSourceIdentity.HasMatchingProvenanceHash(asserted, new string('a', 64)),
            "Actual recording provenance hash can identify exact source master");
        Reject(() => RecordingSourceIdentity.HasMatchingProvenanceHash(asserted, new string('b', 64)),
            "Contradictory source evidence always fails even when user approved retiming");

        var nestedSha = new RecordingDescriptor(d.Name, d.Dimensions, d.Clock, d.SampleCount,
            "{\"source_audio\":{\"sha256\":\"" + new string('a', 64) + "\"}}");
        Require(RecordingSourceIdentity.HasMatchingProvenanceHash(nestedSha, new string('a', 64)),
            "Nested StemLab source hash is recognised");
        Reject(() => RecordingSourceIdentity.HasMatchingProvenanceHash(nestedSha, new string('b', 64)),
            "Nested contradictory source hash cannot be approved as same-source timing");

        // GPU windows preserve CPU's exact pip position at boundaries and after arbitrary seeks.
        var w0 = RecordingGpuWindows.AtPip(1000, 4, -32, -32.25 + 0.25, 256);
        Require(w0.StartRow == 0 && w0.CurrentRow == 0 && w0.RelativeRow == 0,
            "First GPU window includes the native signed start pip");
        var w1 = RecordingGpuWindows.AtPip(1000, 4, -32, 31.5, 256);
        Require(w1.StartRow == 63 && w1.CurrentRow == 63 && w1.RelativeRow == 0,
            "GPU chunk advances with a one-row overlap at a sample boundary");
        Near(w1.Fraction, 0.5, "GPU fraction remains tied to authoritative CPU pip");
        var wEnd = RecordingGpuWindows.AtPip(1000, 4, -32, 967.5, 256);
        Require(wEnd.CurrentRow == 999 && wEnd.RelativeRow < wEnd.RowCount,
            "GPU tail window represents final finite pip without wrapping");
        Reject(() => RecordingGpuWindows.AtPip(1000, 4, -32, 968, 256),
            "GPU refuses exact native exclusive capture end");
        Reject(() => RecordingGpuWindows.AtPip(1000, 4, -32, -33, 256),
            "GPU refuses samples before signed source recording");
        Reject(() => RecordingGpuWindows.AtPip(1000, 4, -32, 0, 4),
            "GPU buffer budget must hold adjacent rows");

        Console.WriteLine("DanceFlow Phase 3 recording checks: " + checks + " assertions passed.");
    }
}
