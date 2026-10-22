using UnityEngine;

namespace DanceFlow.Unity.Clock
{
    /// <summary>Unity DSP clock. Does not use Time.deltaTime or AudioSource.time as its authority.</summary>
    public sealed class UnityDspClockSource : IClockTimeSource
    {
        public double NowSeconds => AudioSettings.dspTime;
    }
}
