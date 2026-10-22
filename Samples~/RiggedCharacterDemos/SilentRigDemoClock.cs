using DanceFlow.Unity.Clock;
using UnityEngine;

namespace DanceFlow.Unity.Samples.Rigging
{
    /// <summary>Synthetic, silent source clock; absolutely no claim of musical beat extraction.</summary>
    [DefaultExecutionOrder(-900)]
    public sealed class SilentRigDemoClock : MonoBehaviour
    {
        [SerializeField] private DanceFlowClockBehaviour clock;
        public void SetClock(DanceFlowClockBehaviour value) => clock=value;
        private void Start()
        {
            if (clock == null || clock.Transport == null) return;
            clock.UseFixedTempo(120);
            clock.Transport.Play();
        }
        private void OnGUI()
        {
            if (clock == null || clock.Transport == null) return;
            GUI.Box(new Rect(12, 12, 520, 78), "DanceFlow Stage 4 — synthetic skinned rig demonstrations");
            GUI.Label(new Rect(22, 38, 480, 22), "Silent 120 BPM authored test; NO captured Kandi Boom or CMU motion supplied");
            GUI.Label(new Rect(22, 59, 480, 22), "Beat: " + clock.Current.AbsoluteBeat.ToString("F2") + "   Pip: " + clock.Current.Pip64);
        }
    }
}
