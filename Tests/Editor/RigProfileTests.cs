using System;
using System.Security.Cryptography;
using System.Text;
using DanceFlow.Unity.Rigging;
using NUnit.Framework;

namespace DanceFlow.Unity.Tests
{
    public sealed class RigProfileTests
    {
        private const string ProfileJson = "{\"channels\":[" +
            "{\"array_index\":0,\"bone\":\"hip\",\"data_path\":\"rotation_quaternion\",\"interpolation\":\"quaternion\",\"maximum\":1.0,\"minimum\":-1.0,\"name\":\"hip_w\",\"neutral\":1.0,\"number\":1,\"rotation_mode\":\"QUATERNION\",\"unit\":\"unitless\"}," +
            "{\"array_index\":1,\"bone\":\"hip\",\"data_path\":\"rotation_quaternion\",\"interpolation\":\"quaternion\",\"maximum\":1.0,\"minimum\":-1.0,\"name\":\"hip_x\",\"neutral\":0.0,\"number\":2,\"rotation_mode\":\"QUATERNION\",\"unit\":\"unitless\"}," +
            "{\"array_index\":2,\"bone\":\"hip\",\"data_path\":\"rotation_quaternion\",\"interpolation\":\"quaternion\",\"maximum\":1.0,\"minimum\":-1.0,\"name\":\"hip_y\",\"neutral\":0.0,\"number\":3,\"rotation_mode\":\"QUATERNION\",\"unit\":\"unitless\"}," +
            "{\"array_index\":3,\"bone\":\"hip\",\"data_path\":\"rotation_quaternion\",\"interpolation\":\"quaternion\",\"maximum\":1.0,\"minimum\":-1.0,\"name\":\"hip_z\",\"neutral\":0.0,\"number\":4,\"rotation_mode\":\"QUATERNION\",\"unit\":\"unitless\"}" +
            "],\"format\":\"dancerudiments.rig-profile\",\"model\":\"unity_test\",\"schema_version\":1,\"source\":{},\"type_uri\":\"syn://type/danceflow/motion/rig/custom/unity-test\",\"version\":1}";

        private static string Hash(string raw)
        {
            using(var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw))).Replace("-", "").ToLowerInvariant();
        }

        private static string Wrapper()
        {
            const string recording = "{\"clock\":{\"beat_zero_seconds\":0.0,\"bpm\":120.0,\"bpm_source\":\"test\",\"bpm_status\":\"confirmed\",\"phase_status\":\"supplied\",\"start_pip\":0}," +
                "\"dimensions\":[\"hip_w\",\"hip_x\",\"hip_y\",\"hip_z\"],\"format\":\"dancerudiments.recording\",\"name\":\"short_test\",\"pips_per_beat\":64,\"provenance\":{}," +
                "\"sample_decimals\":12,\"sample_dtype\":\"float64\",\"samples\":[[1.0,0.0,0.0,0.0],[0.707106781187,0.0,0.0,0.707106781187]],\"schema_version\":1}";
            return "{\"format\":\"dancerudiments.rig-motion\",\"profile\":" + ProfileJson +
                ",\"profile_sha256\":\"" + Hash(ProfileJson) + "\",\"recording\":" + recording +
                ",\"schema_version\":1,\"type_uri\":\"syn://type/danceflow/motion/rig/custom/unity-test\"}";
        }

        [Test] public void ExactProfileAndRecordingIdentity()
        {
            var motion=RigMotionJson.Parse(Wrapper());
            Assert.AreEqual(4,motion.Profile.ChannelCount);
            Assert.AreEqual(Hash(ProfileJson),motion.Profile.Fingerprint);
            motion.Profile.RequireMatchingRecording(motion.Recording.Descriptor);
            Assert.AreEqual(2,motion.Recording.SampleCount);
        }

        [Test] public void QuaternionInterpolationPreservesNormAndNativeExtent()
        {
            var motion=RigMotionJson.Parse(Wrapper());
            var sampler=new RigPoseSampler(motion.Profile,motion.Recording);
            var values=new double[4];
            sampler.SampleAtPipInto(0.5,values);
            double norm=0;foreach(double v in values)norm+=v*v;
            Assert.AreEqual(1,norm,2e-5);
            Assert.Throws<ArgumentOutOfRangeException>(()=>sampler.SampleAtPipInto(2,values));
        }

        [Test] public void FingerprintAndQuaternionCorruptionFailsClosed()
        {
            string wrapper=Wrapper();
            Assert.Throws<FormatException>(()=>RigMotionJson.Parse(wrapper.Replace(Hash(ProfileJson),new string('f',64))));
            var bad=RigMotionJson.Parse(wrapper.Replace("[1.0,0.0,0.0,0.0]", "[0.0,0.0,0.0,0.0]"));
            Assert.Throws<FormatException>(()=>new RigPoseSampler(bad.Profile,bad.Recording));
        }
    }
}
