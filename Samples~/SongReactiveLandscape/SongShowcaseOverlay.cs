using DanceFlow.Unity.Clock;
using DanceFlow.Unity.Motion;
using UnityEngine;

namespace DanceFlow.Unity.Samples
{
    /// <summary>On-screen provenance and four channel meters; always discloses synthetic preview.</summary>
    public sealed class SongShowcaseOverlay : MonoBehaviour
    {
        [SerializeField] private MotionSignalBus bus;
        [SerializeField] private DanceFlowClockBehaviour clock;
        [SerializeField] private SongShowcaseBootstrap showcase;
        [SerializeField] private AudioSource audio;
        public void Configure(MotionSignalBus signals, DanceFlowClockBehaviour master,
                              SongShowcaseBootstrap preset, AudioSource track)
        {
            bus = signals; clock = master; showcase = preset; audio = track;
        }
        private void OnGUI()
        {
            if (bus == null || clock == null || clock.Transport == null) return;
            var rect = new Rect(12, 12, 368, 162);
            GUI.Box(rect, "DanceFlow · " + (showcase == null ? "Song Showcase" : showcase.Preset.ToString()));
            bool audioLoaded = audio != null && audio.clip != null;
            GUI.Label(new Rect(24, 37, 350, 21),
                bus.IsRecordedSample && audioLoaded ? "REAL RECORDING · source association confirmed" :
                "SYNTHETIC / UNMATCHED PREVIEW — NOT SONG DATA");
            GUI.Label(new Rect(24, 56, 320, 19), "Beat " + clock.Current.AbsoluteBeat.ToString("F2") +
                "  BPM " + clock.Current.Bpm.ToString("F2"));
            for (int i = 0; i < 4; i++)
            {
                string slot = i == 0 ? "Wind" : i == 1 ? "Gust" : i == 2 ? "Palette" : "Shimmer";
                GUI.Label(new Rect(24, 77 + 19 * i, 115, 18), slot);
                GUI.HorizontalSlider(new Rect(136, 80 + 19 * i, 185, 20), bus.Value(i), -1, 1);
            }
        }
    }
}
