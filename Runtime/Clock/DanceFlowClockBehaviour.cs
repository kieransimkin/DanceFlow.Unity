using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace DanceFlow.Unity.Clock
{
    /// <summary>
    /// A single-scene DSP-synchronised clock component. Independent scripts can consume Current.
    /// Stereo/compressed AudioClips may not retain exact source-file sample-frame correspondence;
    /// verified master identity is supplied separately for variable-tempo import.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DanceFlowClockBehaviour : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;
        [SerializeField, Min(1)] private float nominalBpm = 120;
        [SerializeField] private double beatZeroSourceSeconds;
        [SerializeField] private bool playOnStart;
        [SerializeField] private bool showDiagnostics;

        public MusicalTransport Transport { get; private set; }
        public BeatState Current { get; private set; }
        public event Action<MusicalBoundaryEvent> BoundaryCrossed;
        public event Action<SourceCue> CueCrossed;

        private readonly MusicalScheduler _scheduler = new MusicalScheduler();
        private SourceCueScheduler _cueScheduler;

        private void Awake()
        {
            Transport = new MusicalTransport(new FixedBeatMap(nominalBpm, beatZeroSourceSeconds),
                                             new UnityDspClockSource());
            Current = Transport.Evaluate();
            _scheduler.Advance(Current, _ => { }); // Establish the stopped transport baseline.
            if (audioSource != null) audioSource.playOnAwake = false;
        }

        private void OnEnable() { AudioSettings.OnAudioConfigurationChanged += AudioChanged; }
        private void OnDisable() { AudioSettings.OnAudioConfigurationChanged -= AudioChanged; }

        private void Start()
        {
            if (playOnStart && audioSource != null) PlayScheduled();
        }

        private void Update()
        {
            if (Transport == null) return;
            Current = Transport.Evaluate();
            _scheduler.Advance(Current, boundary => BoundaryCrossed?.Invoke(boundary));
            _cueScheduler?.Advance(Current, cue => CueCrossed?.Invoke(cue));
        }

        /// <summary>Configure explicit source-time cue data; names are inert strings, never commands.</summary>
        public void SetCues(IReadOnlyList<SourceCue> cues)
        {
            _cueScheduler = new SourceCueScheduler(Transport, cues);
            _cueScheduler.Advance(Transport.Evaluate(), _ => { });
        }

        /// <summary>Enable optional phrase boundaries at multiples of the configured meter.</summary>
        public void SetPhraseBars(int bars)
        {
            if (bars < 0 || bars > 1024) throw new ArgumentOutOfRangeException(nameof(bars));
            _scheduler.PhraseBars = bars;
            _scheduler.Reset();
        }

        /// <summary>Import using the SHA-256 of the actual selected source file, not AudioClip.name.</summary>
        public void UseStemLabTimeline(string json, Stream exactMasterBytes, int masterSampleRate,
                                       long masterFrames, int masterChannels)
        {
            var identity = SourceAudioIdentity.FromAudioBytes(exactMasterBytes, masterSampleRate,
                                                               masterFrames, masterChannels);
            // Parse before replacement so a rejected map does not alter the live clock.
            var map = StemLabBeatTimeline.Parse(json, identity);
            UseVerifiedBeatMap(map);
        }

        /// <summary>
        /// Adopt a map that was already strictly parsed against an independently verified
        /// original audio source identity. Avoid decoding untrusted map data during playback.
        /// </summary>
        public void UseVerifiedBeatMap(VariableBeatMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (Transport == null) throw new InvalidOperationException("Clock is not initialized.");
            Transport.ReplaceMap(map);
            _scheduler.Reset();
            _cueScheduler?.Reset();
            Current = Transport.Evaluate();
        }

        public void UseFixedTempo(double bpm, double beatZeroSeconds = 0)
        {
            Transport.ReplaceMap(new FixedBeatMap(bpm, beatZeroSeconds));
            _scheduler.Reset();
        }

        /// <summary>Schedules audio and clock against the same DSP timestamp.</summary>
        public void PlayScheduled(double leadSeconds = 0.2)
        {
            if (audioSource == null || audioSource.clip == null)
                throw new InvalidOperationException("A source with an AudioClip is required.");
            if (leadSeconds < 0.1 || leadSeconds > 10)
                throw new ArgumentOutOfRangeException(nameof(leadSeconds));
            if (Transport.IsRunning) throw new InvalidOperationException("Audio transport is already running.");
            double start = AudioSettings.dspTime + leadSeconds;
            audioSource.loop = false;
            audioSource.PlayScheduled(start);
            Transport.PlayScheduled(start);
        }

        public void Pause()
        {
            if (audioSource != null) audioSource.Pause();
            Transport.Pause();
        }

        public void Resume()
        {
            if (audioSource != null) audioSource.UnPause();
            Transport.Play();
        }

        public void Stop()
        {
            if (audioSource != null) audioSource.Stop();
            Transport.Stop();
            _scheduler.Reset();
            _cueScheduler?.Reset();
        }

        /// <summary>AudioSource.time may seek approximately for compressed/streamed clips.</summary>
        public void SeekSourceSeconds(double seconds)
        {
            // Validate range first; should not change the audio source on an invalid seek.
            if (seconds < Transport.ExcerptStart || seconds > Transport.ExcerptEnd)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (audioSource != null && audioSource.clip != null)
            {
                if (seconds < 0 || seconds > audioSource.clip.length)
                    throw new ArgumentOutOfRangeException(nameof(seconds), "Seek must fit the loaded clip.");
                audioSource.time = (float)seconds;
            }
            Transport.SeekSourceSeconds(seconds);
            _scheduler.Reset();
            _cueScheduler?.Reset();
        }

        private void AudioChanged(bool deviceChanged)
        {
            // DSP continuity cannot be assumed after an output device/format reset.
            if (Transport == null) return;
            Transport.Pause();
            _scheduler.Reset();
            _cueScheduler?.Reset();
            Debug.LogWarning("DanceFlow audio configuration changed; transport paused. Reschedule audio explicitly.", this);
        }

        private void OnGUI()
        {
            if (!showDiagnostics || Transport == null) return;
            var s = Current;
            GUI.Label(new Rect(16, 16, 650, 48),
                $"DanceFlow | source {s.SourceSeconds:F4}s | BPM {s.Bpm:F3} | beat {s.AbsoluteBeat:F4} | tick {s.Tick16} | pip {s.Pip64} | bar {s.BarIndex}");
        }
    }
}
