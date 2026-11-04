using DanceFlow.Unity.Clock;
using UnityEngine;

namespace DanceFlow.Unity.WebGLDemo
{
    /// <summary>Browser-gesture driven song playback and explicit per-property motion routing.</summary>
    public sealed class KandiBoomControls : MonoBehaviour
    {
        [SerializeField] private DanceFlowClockBehaviour clock;
        [SerializeField] private KandiBoomSongPlayer songs;
        [SerializeField] private KandiBoomMotionMatrix matrix;
        private Vector2 _scroll;
        // Legacy SYNTHETIC BEAT PREVIEW is intentionally disabled in the real-audio gallery.
        public void Configure(DanceFlowClockBehaviour authoritativeClock, KandiBoomSongPlayer songPlayer,
                              KandiBoomMotionMatrix motion)
        { clock = authoritativeClock; songs = songPlayer; matrix = motion; }

        private void OnGUI()
        {
            if (songs == null || clock == null || matrix == null) return;
            const float x = 16, y = 12, w = 480;
            GUI.Box(new Rect(x, y, w, 268), "KANDI BOOM  ·  DanceFlow.Unity   ·   SONG PLAYER");
            GUI.Label(new Rect(x+12, y+27, 445, 22), "Real artist audio · source-verified StemLab examples");
            GUI.Label(new Rect(x+12, y+52, 444, 22), songs.Status);
            GUI.Label(new Rect(x+12, y+78, 444, 22), songs.Song == null ? "Loading..." :
                songs.Song.title + " — " + songs.Song.artist);
            if (GUI.Button(new Rect(x+12, y+105, 32, 29), "<") && songs.SongCount > 0)
                songs.Select((songs.SelectedIndex + songs.SongCount - 1) % songs.SongCount);
            if (GUI.Button(new Rect(x+51, y+105, 78, 29), songs.IsPlaying ? "Pause" : "Play"))
                songs.PlayPause(); // the actual user gesture is required by browser audio policies
            if (GUI.Button(new Rect(x+136, y+105, 32, 29), ">") && songs.SongCount > 0)
                songs.Select((songs.SelectedIndex + 1) % songs.SongCount);
            GUI.Label(new Rect(x+183, y+109, 280, 23), songs.Ready ?
                songs.Position.ToString("F1") + " / " + songs.Duration.ToString("F1") + " seconds" : "Load the song before playing");
            if (songs.Ready)
            {
                float next = GUI.HorizontalSlider(new Rect(x+12, y+147, 442, 20),
                                                   songs.Position, 0, Mathf.Max(1, songs.Duration));
                if (Mathf.Abs(next - songs.Position) > 0.9f) songs.Seek(next);
            }
            GUI.Label(new Rect(x+12, y+171, 440, 23), songs.Song == null ? "Tempo: —" :
                songs.Song.bpm <= 0 ? "BPM unverified · motion-only preview 120 BPM" :
                "Tempo " + clock.Current.Bpm.ToString("F2") + " BPM · " + songs.Song.tempo_status);
            GUI.Label(new Rect(x+12, y+193, 427, 23), songs.Song == null ? "" :
                "Phase: " + songs.Song.phase_status + " · pip " + clock.Current.Pip64);
            GUI.Label(new Rect(x+12, y+217, 100, 22), "Volume");
            songs.Volume = GUI.HorizontalSlider(new Rect(x+83, y+224, 185, 19), songs.Volume, 0, 1);
            GUI.Label(new Rect(x+280, y+216, 180, 38), "Drag to orbit · wheel zoom");
            GUI.Label(new Rect(x+12, y+245, 458, 20), "Star Lights Glow (Anthem) awaits its own source master.");

            float top = y + 278;
            GUI.Box(new Rect(x, top, 655, 355), "MOTION MATRIX · each property has independent AUDIO + REAL DANCERUDIMENT pattern");
            GUI.Label(new Rect(x+12, top+25, 630, 24),
                "Native compiled patterns are beat-retimed CANDIDATES, not confirmed song matches.");
            const float rowHeight = 31f;
            Rect window = new Rect(x+12, top+54, 635, 285);
            _scroll = GUI.BeginScrollView(window, _scroll, new Rect(0, 0, 605, 31 + rowHeight*matrix.BindingCount));
            GUI.Label(new Rect(4, 0, 102, 21), "PROPERTY");
            GUI.Label(new Rect(118, 0, 105, 21), "AUDIO BAND");
            GUI.Label(new Rect(245, 0, 245, 21), "DANCERUDIMENT");
            GUI.Label(new Rect(506, 0, 100, 21), "GAIN");
            for (int i=0; i<matrix.BindingCount; ++i)
            {
                float yy = 27 + i * rowHeight;
                GUI.Label(new Rect(3, yy+3, 113, 23), matrix.Name(i));
                if (GUI.Button(new Rect(118, yy, 120, 24), matrix.BandName(i)+"  >")) matrix.CycleBand(i);
                if (GUI.Button(new Rect(245, yy, 202, 24), matrix.PatternName(i)+"  >")) matrix.CyclePattern(i);
                if (GUI.Button(new Rect(451, yy, 39, 24), matrix.AxisName(i))) matrix.CycleAxis(i);
                if (GUI.Button(new Rect(497, yy, 24, 24), "-")) matrix.ChangeGain(i, -0.1f);
                GUI.Label(new Rect(526, yy+2, 46, 22), matrix.Gain(i).ToString("F1"));
                if (GUI.Button(new Rect(573, yy, 24, 24), "+")) matrix.ChangeGain(i, 0.1f);
            }
            GUI.EndScrollView();
        }
    }
}
