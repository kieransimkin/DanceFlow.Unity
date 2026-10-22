using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using DanceFlow.Unity.Motion;
using DanceFlow.Unity.Rigging;

internal static class Program
{
    static int _count;
    static void Check(bool okay, string detail)
    { if (!okay) throw new Exception("Stage 4 rig test failed: " + detail); _count++; }
    static void Near(double actual, double expected, string detail, double tolerance=1e-9)
        => Check(Math.Abs(actual-expected)<=tolerance,detail+": "+actual+" != "+expected);
    static void Fails(Action callback, string detail)
    {
        bool caught=false;
        try { callback(); }
        catch (ArgumentException) {caught=true;}
        catch (FormatException) {caught=true;}
        catch (InvalidOperationException) {caught=true;}
        Check(caught,detail);
    }
    static RigMotionDocument Load(string name)
    {
        var path=Path.Combine(Directory.GetCurrentDirectory(),"Samples~","RiggedCharacterDemos",
            "Data","unity_"+name+"_demo.rig-motion.json");
        return RigMotionJson.ParseUtf8(File.ReadAllBytes(path));
    }
    static void Main()
    {
        foreach (string name in new[] {"humanoid","quadruped"})
        {
            var doc=Load(name);
            int expected=name=="humanoid"?20:15;
            Check(doc.Profile.ChannelCount==expected,"Profile has every numbered channel");
            Check(doc.Profile.ChannelAt(0).Number==1 && doc.Profile.ChannelAt(expected-1).Number==expected,
                "Rig channels are contiguous and one-based");
            Check(doc.Profile.TypeUri.EndsWith("/unity-"+name+"-demo"),"Correct exact semantic profile URI");
            Check(doc.Recording.SampleCount==1024 && doc.Recording.Clock.StartPip==0,
                "Finite native 64-pip capture has 1024 positions");
            var sampler=new RigPoseSampler(doc.Profile,doc.Recording);
            var atZero=new double[expected];
            var atMid=new double[expected];
            var atEnd=new double[expected];
            sampler.SampleExactInto(0,atZero);
            sampler.SampleAtPipInto(64.5,atMid);
            sampler.SampleExactInto(1023,atEnd);
            int rootX=doc.Profile.DimensionNames.ToList().IndexOf("hips_location_x");
            Near(atZero[rootX],0,"Root starts in its declared neutral pose");
            Check(Math.Abs(atMid[rootX])>0.02,"Recorded root motion changes with native pip");
            Fails(()=>sampler.SampleAtPipInto(1024,new double[expected]),"Rig recording has exclusive end");
            Fails(()=>sampler.SampleAtPipInto(-1,new double[expected]),"Rig recording has no preroll row");
            for (int pip=0;pip<1024;pip+=127)
            {
                var pose=new double[expected];
                sampler.SampleExactInto(pip,pose);
                foreach(double v in pose) Check(!double.IsNaN(v) && !double.IsInfinity(v),"Finite decoded physical rig channels");
            }
            var dims=doc.Profile.DimensionNames;
            int w=Array.IndexOf(dims,"spine_quat_w");
            int x=Array.IndexOf(dims,"spine_quat_x");
            int y=Array.IndexOf(dims,"spine_quat_y");
            int z=Array.IndexOf(dims,"spine_quat_z");
            double length=atMid[w]*atMid[w]+atMid[x]*atMid[x]+atMid[y]*atMid[y]+atMid[z]*atMid[z];
            Near(length,1,"Shortest-path interpolated quaternion stays unit",2e-5);
            var blend=new double[expected];
            sampler.BlendInto(atZero,atEnd,0.5,blend);
            length=blend[w]*blend[w]+blend[x]*blend[x]+blend[y]*blend[y]+blend[z]*blend[z];
            Near(length,1,"Physical pose crossfade preserves unit quaternion",2e-5);
            var wrong=new RecordingDescriptor(doc.Recording.Descriptor.Name, dims.Reverse().ToArray(),
                doc.Recording.Clock,doc.Recording.SampleCount,doc.Recording.Descriptor.ProvenanceJson);
            Fails(()=>doc.Profile.RequireMatchingRecording(wrong),"Rig channel reordering must reject");
            if(name=="humanoid")
            {
                int hand=Array.IndexOf(dims,"hand_mode");
                var held=new double[expected];
                sampler.SampleAtPipInto(63.5,held);
                Near(held[hand],0,"Discrete selector must not interpolate");
                sampler.SampleAtPipInto(64,held);
                Near(held[hand],1,"Discrete selector advances at native integer pip");
            }
        }
        var human=Load("humanoid");
        var raw=File.ReadAllText(Path.Combine("Samples~","RiggedCharacterDemos","Data", "unity_humanoid_demo.rig-motion.json"));
        string modified=raw.Replace("\"profile_sha256\":\""+human.Profile.Fingerprint+"\"",
            "\"profile_sha256\":\""+new string('0',64)+"\"");
        Fails(()=>RigMotionJson.Parse(modified),"Embedded profile fingerprint must be exact");
        var objectJson=JsonNode.Parse(raw)!.AsObject();
        var rec=objectJson["recording"]!.AsObject();
        rec["samples"]!.AsArray()[0]!.AsArray()[3]=0.0; // hips wxyz now has zero W and zero XYZ
        var corrupted=RecordingJson.Parse(rec.ToJsonString());
        Fails(()=>new RigPoseSampler(human.Profile,corrupted),"Non-unit captured quaternions reject");
        var profile=objectJson["profile"]!.AsObject();
        var chan=profile["channels"]!.AsArray();
        chan.RemoveAt(4); // remove a quaternion component; numbers no longer contiguous
        Fails(()=>RigProfileJson.Parse(profile.ToJsonString()),"Partial quaternion and channel gaps reject");
        Console.WriteLine("DanceFlow Stage 4 rigorous rig-motion checks passed ("+_count+" assertions).");
    }
}
