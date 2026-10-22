using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.GPU;

internal static class Program
{
    private static int _passed;
    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _passed++;
    }
    private static void Close(double found, double wanted, string message, double tolerance = 1e-10)
    {
        Expect(Math.Abs(found - wanted) <= tolerance, message + $": {found} != {wanted}");
    }
    private static void Fails(Action action, string message)
    {
        bool threw = false;
        try { action(); }
        catch (ArgumentException) { threw = true; }
        catch (FormatException) { threw = true; }
        catch (OverflowException) { threw = true; }
        Expect(threw, message);
    }
    private static string Change(string source, Action<JsonObject> alter)
    {
        JsonObject doc = JsonNode.Parse(source)!.AsObject();
        alter(doc);
        return doc.ToJsonString();
    }

    private static void Main()
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(), "Tests", "Fixtures", "stemlab-frames-v1.json");
        string json = File.ReadAllText(path);
        var identity = new SourceAudioIdentity(new string('a', 64), 8000, 24000, 1);
        var map = StemLabBeatTimeline.Parse(json, identity);
        byte[] bytes = { 1, 2, 3, 4, 5 };
        using (var master = new MemoryStream(bytes))
        {
            master.Position = 2;
            var derived = SourceAudioIdentity.FromAudioBytes(master, 8000, 24000, 1);
            string expectedSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            Expect(derived.Sha256 == expectedSha, "Real source bytes are hashed");
            Expect(master.Position == 2, "Stream position is restored after hashing");
        }

        Close(map.EvaluateFrame(0).Beat, -2.25, "Pre-roll and beat-zero correction");
        Close(map.Evaluate(0).Beat * 64, -144, "Native signed phase pips");
        Close(map.EvaluateFrame(1000).Beat, -2, "First source anchor");
        Close(map.EvaluateFrame(5000).Beat, -1, "Second source anchor");
        Close(map.EvaluateFrame(9000).Beat, 0, "Corrected beat 1");
        Close(map.EvaluateFrame(11580).Beat, 0.5, "Interpolated source frame");
        Close(map.EvaluateFrame(11580).Bpm, 60.0 * 8000 / 5160, "Measured local BPM");
        Close(map.EvaluateFrame(25000).Bpm, 60.0 * 8000 / 3920, "Last interval extrapolation");
        Close(map.SecondsAtBeat(0.5), 11580.0 / 8000, "Beat/source inverse mapping");
        Close(map.SecondsAtBeat(-2.25), 0, "Inverse pre-roll");

        var gaps = new VariableBeatMap(new[] { new BeatAnchor(0, 1000), new BeatAnchor(3, 5000) },
            8000, 24000, 0, identity.Sha256, new string('b', 64), "estimated");
        Close(gaps.EvaluateFrame(3000).Beat, 1.5, "Skipped ordinal contributes to phase without inventing onset");
        Close(gaps.EvaluateFrame(3000).Bpm, 360, "Skipped ordinal uses actual beat-step tempo");
        var signed = new BeatState(0, -0.25, -0.25, 120, 3, 0, 2, 0, false);
        Expect(signed.BeatIndex == -1 && signed.Tick16 == -4 && signed.Pip64 == -16, "Signed floor indexes");
        Close(signed.BarPhase, 11.0 / 12, "Negative bar phase");

        var clock = new ManualClockSource();
        var transport = new MusicalTransport(new FixedBeatMap(120), clock);
        var scheduler = new MusicalScheduler();
        int beats = 0, bars = 0;
        Action<MusicalBoundaryEvent> cb = e => { if (e.Kind == MusicalBoundaryKind.Beat) beats++; else bars++; };
        transport.Play(); scheduler.Advance(transport.Evaluate(), cb);
        clock.Advance(2.1); scheduler.Advance(transport.Evaluate(), cb);
        Expect(beats == 4 && bars == 1, "Frame skip delivers four beats and one bar");
        transport.SeekBeat(100); scheduler.Advance(transport.Evaluate(), cb);
        Expect(beats == 4 && bars == 1, "Seeking does not replay skipped events");
        transport.Pause(); clock.Advance(5); Close(transport.Evaluate().AbsoluteBeat, 100, "Pause holds source time");
        transport.SeekBeat(3); transport.SetPlaybackRate(0.5); transport.Play();
        clock.Advance(0.5); Close(transport.Evaluate().AbsoluteBeat, 3.5, "Half-rate advance");
        transport.SetExcerpt(0, 4); transport.SetLoop(1, 2); transport.SeekSourceSeconds(1);
        clock.Advance(2.5);
        var looped = transport.Evaluate();
        Close(looped.SourceSeconds, 1.25, "Source loop maps through original source time");
        Close(looped.ContinuousBeat, 4.5, "Loop-derived monotonic beat for scheduling");
        Expect(looped.LoopIteration == 1, "Loop cycle count");

        // Bar-origin and optional phrase boundaries use one continuous beat position.
        var phraseClock = new ManualClockSource();
        var phraseTransport = new MusicalTransport(new FixedBeatMap(120), phraseClock);
        var phraseScheduler = new MusicalScheduler { PhraseBars = 2 };
        int phraseCount = 0;
        phraseTransport.Play();
        phraseScheduler.Advance(phraseTransport.Evaluate(), e => {
            if (e.Kind == MusicalBoundaryKind.Phrase) phraseCount++;
        });
        phraseClock.Advance(4.1);
        phraseScheduler.Advance(phraseTransport.Evaluate(), e => {
            if (e.Kind == MusicalBoundaryKind.Phrase) phraseCount++;
        });
        Expect(phraseCount == 1, "Phrase boundary at two four-beat bars");

        // Source-second cues are independent of local BPM and correctly cross loop seams.
        var cueClock = new ManualClockSource();
        var cueTransport = new MusicalTransport(new FixedBeatMap(120), cueClock);
        cueTransport.SetExcerpt(0, 4);
        cueTransport.SetLoop(1, 2);
        cueTransport.SeekSourceSeconds(1.5);
        var cueScheduler = new SourceCueScheduler(cueTransport,
            new[] { new SourceCue(1.8, "DROP"), new SourceCue(1.2, "VERSE") });
        var cueNames = new System.Collections.Generic.List<string>();
        cueTransport.Play();
        cueScheduler.Advance(cueTransport.Evaluate(), c => cueNames.Add(c.Name));
        cueClock.Advance(1.1);
        cueScheduler.Advance(cueTransport.Evaluate(), c => cueNames.Add(c.Name));
        Expect(cueNames.SequenceEqual(new[] { "DROP", "VERSE" }), "Cue order across original source loop seam");
        cueTransport.SeekSourceSeconds(3.5);
        cueScheduler.Advance(cueTransport.Evaluate(), c => cueNames.Add(c.Name));
        Expect(cueNames.Count == 2, "Cue seek suppression");

        Fails(() => StemLabBeatTimeline.Parse(json, new SourceAudioIdentity(new string('c', 64), 8000, 24000, 1)), "Wrong master rejects");
        Fails(() => StemLabBeatTimeline.Parse(json, new SourceAudioIdentity(identity.Sha256, 16000, 24000, 1)), "Wrong sample rate rejects");
        Fails(() => StemLabBeatTimeline.Parse(Change(json, d => d["sync"]!["exact_phase_offset_pips"] = "-143"), identity), "Tampered exact offset rejects");
        Fails(() => StemLabBeatTimeline.Parse(Change(json, d => d["tempo_segments"]![0]!["bpm"] = 300), identity), "Tampered derived tempo rejects");
        Fails(() => StemLabBeatTimeline.Parse(Change(json, d => d["beat_timeline"]![1]!["pip"] = 999), identity), "Tampered derived pip rejects");
        Fails(() => StemLabBeatTimeline.Parse(Change(json, d => d["beats"]![1]!["frame"] = 1000), identity), "Duplicate frames reject");
        Fails(() => StemLabBeatTimeline.Parse(Change(json, d => d["review_status"] = "confirmed"), identity), "False confirmed status rejects");
        Fails(() => StemLabBeatTimeline.Parse(Change(json, d => d["beats"]![1]!["source"] = "manual"), identity), "Manual source with detector reference rejects");
        Fails(() => StemLabBeatTimeline.Parse("{\"schema\":\"x\",\"schema\":\"x\"}", identity), "Duplicate JSON keys reject");
        Fails(() => StemLabBeatTimeline.Parse(json.Replace("24000", "NaN"), identity), "NaN JSON rejects");
        // Phase 2: GPU transport data must agree with the same double-precision map.
        var gpuAtHalf = ShaderClockMath.Pack(new BeatState(11580.0/8000, 0.5, 0.5,
            map.EvaluateFrame(11580).Bpm, 4, 0, 10, 0, true));
        Close(gpuAtHalf.BeatPhase, 0.5, "GPU half-beat phase");
        Close(gpuAtHalf.BarPhase, 0.125, "GPU quarter-note bar phase");
        Close(gpuAtHalf.Bpm, 60.0 * 8000 / 5160, "GPU local BPM", 1e-4);
        Close(gpuAtHalf.PipPhase, 0, "GPU native pip phase");
        var signedGpu = ShaderClockMath.Pack(signed);
        Expect(signedGpu.BeatIndexHigh == -1 && signedGpu.BeatIndexLow == 65535,
            "Shader BeatIndexHiLo encodes negative beat indices");
        Close(signedGpu.BeatPhase, 0.75, "GPU signed beat phase", 1e-7);
        var sourceLoopGpu = ShaderClockMath.Pack(looped, ShaderBeatDomain.Source);
        var continuousLoopGpu = ShaderClockMath.Pack(looped, ShaderBeatDomain.Continuous);
        Expect(Math.Abs(sourceLoopGpu.BeatRemainder256 - continuousLoopGpu.BeatRemainder256) > 0.5,
            "GPU source and loop-continuous domains are independent");
        foreach (double beat in new[] {-150005.125, -1.25, 0.125, 1123403.75, 1000000021.125})
        {
            var state = new BeatState(beat/2, beat, beat, 120, 3, -0.25, 17, 0, true);
            var packed = ShaderClockMath.Pack(state);
            foreach (int period in new[] {1, 2, 3, 4, 5, 7, 16, 32, 64, 255, 256})
            {
                double reference = ShaderClockMath.CyclePhase(beat, period);
                double gpu = ShaderClockMath.SimulateGpuCyclePhase(packed, period);
                double distance = Math.Abs(reference - gpu);
                Expect(Math.Min(distance, 1.0 - distance) <= 5e-5,
                    $"GPU float cycle parity at {beat} / {period}");
            }
        }
        Fails(() => ShaderClockMath.CyclePhase(42, 257), "GPU cycle period bound");
        Fails(() => ShaderClockMath.SplitCounter(1L << 45, out _, out _), "GPU high integer overflow guard");
        Console.WriteLine($"DanceFlow Phases 1–2 clock/shader checks passed ({_passed} assertions).");
    }
}
