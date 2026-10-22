using DanceFlow.Unity.Clock;
using UnityEngine;

namespace DanceFlow.Unity.WebGLDemo
{
    /// <summary>Basic real WebGL controls; original FBX, synthetic pip-aligned animation.</summary>
    public sealed class KandiBoomControls : MonoBehaviour
    {
        [SerializeField] private DanceFlowClockBehaviour clock;
        [SerializeField] private float bpm = 120f;
        private bool _running;

        public void Configure(DanceFlowClockBehaviour authoritativeClock, float previewBpm)
        {
            clock = authoritativeClock;
            bpm = previewBpm;
        }

        private void Start()
        {
            if (clock == null || clock.Transport == null) return;
            clock.UseFixedTempo(bpm);
            clock.Transport.Play();
            _running = true;
        }

        private void OnGUI()
        {
            float x = 18, y = 18;
            GUI.Box(new Rect(x, y, 430, 184), "Kandi Boom · Bass Ben & Friends — DanceFlow.Unity");
            GUI.Label(new Rect(x + 14, y + 27, 404, 25), "Original audited FBX / 24-bone skinned character");
            GUI.Label(new Rect(x + 14, y + 50, 405, 24), "Movement: SYNTHETIC BEAT PREVIEW, not CMU salsa/ballet");
            if (clock != null && clock.Transport != null)
            {
                var s = clock.Current;
                GUI.Label(new Rect(x + 14, y + 73, 400, 22),
                    "Beat " + s.AbsoluteBeat.ToString("F2") + "   Phase " + s.BeatPhase.ToString("F2") +
                    "   64-pip " + s.Pip64);
            }
            if (GUI.Button(new Rect(x + 14, y + 101, 116, 31), _running ? "Pause" : "Play"))
            {
                if (clock != null && clock.Transport != null)
                {
                    if (_running) clock.Transport.Pause(); else clock.Transport.Play();
                    _running = !_running;
                }
            }
            if (GUI.Button(new Rect(x + 140, y + 101, 54, 31), "-")) SetBpm(bpm - 5);
            GUI.Label(new Rect(x + 202, y + 107, 118, 22), bpm.ToString("F0") + " BPM");
            if (GUI.Button(new Rect(x + 326, y + 101, 54, 31), "+")) SetBpm(bpm + 5);
            GUI.Label(new Rect(x + 14, y + 145, 404, 24),
                "No song/audio is bundled. Orbit with mouse drag; scroll to zoom.");
        }

        private void SetBpm(float value)
        {
            bpm = Mathf.Clamp(value, 40, 240);
            if (clock != null && clock.Transport != null) clock.UseFixedTempo(bpm);
        }
    }
}
