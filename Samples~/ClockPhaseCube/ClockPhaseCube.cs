using DanceFlow.Unity.Clock;
using UnityEngine;

/// <summary>Import this UPM sample and attach to a Cube in a scene containing a DanceFlow clock.</summary>
public sealed class ClockPhaseCube : MonoBehaviour
{
    [SerializeField] private DanceFlowClockBehaviour masterClock;
    [SerializeField] private float pulseAmplitude = 0.4f;
    [SerializeField, Min(1)] private float pulseSharpness = 5;
    private Vector3 _restScale;

    private void Awake() { _restScale = transform.localScale; }
    private void Update()
    {
        if (masterClock == null) return;
        float phase = (float)masterClock.Current.BeatPhase;
        float envelope = Mathf.Pow(Mathf.Clamp01(1f - phase), pulseSharpness);
        transform.localScale = _restScale * (1f + pulseAmplitude * envelope);
    }

    private void OnDisable() { transform.localScale = _restScale; }
}
