using DanceFlow.Unity.Clock;
using UnityEngine;

/// <summary>Runs an explicitly silent constant tempo clock for the bundled visual demo.</summary>
public sealed class SilentDemoClock : MonoBehaviour
{
    private DanceFlowClockBehaviour _clock;
    private void Awake() => _clock = GetComponent<DanceFlowClockBehaviour>();
    private void Start()
    {
        if (_clock != null && _clock.Transport != null) _clock.Transport.Play();
    }
}
