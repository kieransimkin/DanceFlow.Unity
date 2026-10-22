using DanceFlow.Unity.Clock;
using DanceFlow.Unity.Motion;
using UnityEngine;

namespace DanceFlow.Unity.Samples
{
    public enum SongShowcasePreset
    {
        SyntheticPreview = 0,
        Arcadians = 1,
        TheTruthIsAVirus = 2,
        StarLightsGlowMysteryLiquidKick = 3,
        StarLightsGlowAnthem = 4
    }

    /// <summary>
    /// Scene-only audio/clock glue. This is not a detector: never invents tempo, source
    /// identity or matching motion. Missing audio uses a clearly labelled synthetic demo.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class SongShowcaseBootstrap : MonoBehaviour
    {
        [SerializeField] private SongShowcasePreset preset;
        [SerializeField, Tooltip("Optional verified local audio/beat-map/recording association; no music is distributed with this sample.")]
        private SongShowcaseProfile songProfile;
        [SerializeField] private DanceFlowClockBehaviour songClock;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private MotionSignalBus motion;
        [SerializeField, Tooltip("Required when supplying Star Lights Glow audio: measured/confirmed BPM for this exact mix.")]
        private float reviewedSongBpm;
        [SerializeField] private bool allowUnconfirmedTruthIsAVirusTempo;
        [SerializeField] private float syntheticPreviewBpm = 120;

        public SongShowcasePreset Preset => songProfile != null ? songProfile.Song : preset;
        public SongShowcaseProfile Profile => songProfile;
        public void ConfigureProfile(SongShowcaseProfile profile)
        {
            songProfile = profile;
            if (profile != null) preset = profile.Song;
        }
        public void Configure(SongShowcasePreset choice, DanceFlowClockBehaviour clock, AudioSource songAudio, MotionSignalBus signalBus)
        {
            preset = choice;
            songClock = clock;
            audioSource = songAudio;
            motion = signalBus;
        }

        private float PresetBpm()
        {
            if (reviewedSongBpm > 0) return reviewedSongBpm;
            if (preset == SongShowcasePreset.Arcadians) return 145;
            // StemLab reports 109.0013 BPM as a fitted estimate, NOT artist confirmation.
            if (preset == SongShowcasePreset.TheTruthIsAVirus && allowUnconfirmedTruthIsAVirusTempo) return 109.0013f;
            return 0;
        }
        private void Start()
        {
            if (songClock == null) { Debug.LogError("Assign a DanceFlowClockBehaviour.", this); return; }
            if (motion != null) motion.Configure(songClock);
            if (songProfile != null)
            {
                try
                {
                    songProfile.ApplyTo(songClock, motion, audioSource);
                    songClock.PlayScheduled();
                    Debug.Log("DanceFlow source profile active: " + songProfile.DisplayTitle +
                        " (verified master hash " + songProfile.OriginalMasterSha256 + ", " +
                        (songProfile.Recording == null ? "no recording — synthetic visual preview" :
                         "recording association explicitly approved") + ").", this);
                }
                catch (System.Exception ex) when (ex is System.ArgumentException ||
                          ex is System.InvalidOperationException || ex is System.FormatException)
                {
                    Debug.LogError("DanceFlow source profile refused: " + ex.Message, this);
                }
                return;
            }
            bool audioReady = audioSource != null && audioSource.clip != null;
            float bpm = PresetBpm();
            if (audioReady && bpm <= 0)
            {
                Debug.LogError("No verified BPM supplied for the selected audio. Set Reviewed Song Bpm on the bootstrap before playing; synthetic BPM will not be silently applied to this song.", this);
                return;
            }
            if (audioReady)
            {
                songClock.UseFixedTempo(bpm);
                songClock.PlayScheduled();
                Debug.Log("DanceFlow showcase playing " + preset + " with explicit BPM " + bpm + ". Import a matching recording and confirm source association to drive true motion.", this);
            }
            else
            {
                // Preview is never presented as analysed song motion or beat matched audio.
                songClock.UseFixedTempo(Mathf.Max(1, syntheticPreviewBpm));
                songClock.Transport.Play();
                Debug.LogWarning("DanceFlow showcase is in SYNTHETIC PREVIEW — no audio/recording match is claimed for " + preset + ". Assign exact song audio and an imported DanceRudiments recording for authentic timing.", this);
            }
        }
    }
}
