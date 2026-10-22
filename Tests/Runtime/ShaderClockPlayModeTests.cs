using System.Collections;
using DanceFlow.Unity.Clock;
using DanceFlow.Unity.GPU;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DanceFlow.Unity.Tests
{
    public sealed class ShaderClockPlayModeTests
    {
        [UnityTest]
        public IEnumerator BridgeFollowsExistingTransportAndManualOverrideIsSeekSafe()
        {
            var root = new GameObject("Shader clock playmode test");
            try
            {
                var clock = root.AddComponent<DanceFlowClockBehaviour>();
                var bridge = root.AddComponent<DanceFlowShaderBridge>();
                bridge.SetClock(clock);
                clock.Transport.Play();
                yield return null;
                Assert.IsTrue(bridge.HasPublishedState);
                Assert.AreEqual(ShaderClockSample.ShaderApiVersion,
                    Shader.GetGlobalFloat("_DanceFlow_ShaderApiVersion"), 1e-6);

                bridge.PublishAtSourceSeconds(1.25);
                float atSeek = Shader.GetGlobalFloat("_DanceFlow_BeatPhase");
                Assert.AreEqual(0.5f, atSeek, 1e-5f); // default Phase 1 clock = 120 BPM.
                yield return null;
                Assert.AreEqual(atSeek, Shader.GetGlobalFloat("_DanceFlow_BeatPhase"), 1e-6);
                bridge.ReleaseManualOverride();
                yield return null;
                Assert.Greater(bridge.PublishedStates, 1);
            }
            finally
            {
                UnityEngine.Object.Destroy(root);
            }
        }
    }
}
