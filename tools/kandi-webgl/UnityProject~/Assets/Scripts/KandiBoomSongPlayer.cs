using System;
using System.Collections;
using DanceFlow.Unity.Clock;
using UnityEngine;
using UnityEngine.Networking;

namespace DanceFlow.Unity.WebGLDemo
{
    [Serializable]
    public sealed class KandiWebGLSongCatalogue
    {
        public string schema;
        public KandiWebGLSong[] songs;
        public string[] unavailable_songs;
    }

    [Serializable]
    public sealed class KandiWebGLSong
    {
        public string id;
        public string title;
        public string artist;
        public string audio_file;
        public string signals_file;
        public string audio_sha256;
        public float bpm;
        public float preview_bpm;
        public string canonical_master_sha256;
        public string tempo_status;
        public string phase_status;
        public float duration_seconds;
        public int source_sample_rate;
        public int source_frames;
        public float[] beat_seconds;
        public int origin_beat;
        public string beat_map_sha256;
    }

    [Serializable]
    public sealed class KandiWebGLAudioAnalysis
    {
        public string schema;
        public string source_sha256;
        public float frame_seconds;
        public int frame_count;
        public float[] sub;
        public float[] bass;
        public float[] low_mid;
        public float[] mid;
        public float[] high;
        public float[] onset;
        public float[] width;
        public float[] energy;

        public float[] Channel(int index) => index switch
        {
            0 => sub, 1 => bass, 2 => low_mid, 3 => mid, 4 => high,
            5 => onset, 6 => width, 7 => energy, _ => null
        };
        public bool ValidFor(KandiWebGLSong song)
        {
            if (song == null || schema != "danceflow.unity.webgl-audio-analysis.v1" ||
                source_sha256 != song.audio_sha256 || frame_seconds <= 0 || frame_count < 2)
                return false;
            for (int i = 0; i < 8; ++i)
            {
                float[] values = Channel(i);
                if (values == null || values.Length != frame_count) return false;
                foreach (float value in values)
                    if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1)
                        return false;
            }
            return true;
        }
    }

    [Serializable]
    public sealed class KandiWebGLPattern
    {
        public string name;
        public int period_pips;
        public int primary_axis;
        public string source_sha256;
        public float[] x;
        public float[] y;
        public float[] z;
        public float[] Axis(int axis) => axis switch { 0 => x, 1 => y, 2 => z, _ => null };
        public bool Valid => period_pips >= 64 && period_pips <= 4096 && primary_axis >= 0 && primary_axis < 3 &&
            x != null && y != null && z != null &&
            x.Length == period_pips && y.Length == period_pips && z.Length == period_pips;
    }

    [Serializable]
    public sealed class KandiWebGLPatternLibrary
    {
        public string schema;
        public KandiWebGLPattern[] patterns;
    }

    /// <summary>
    /// Plays the original SHA-locked MP3 files with Unity AudioSource and one DSP-owned
    /// DanceFlow clock. Audio-derived envelopes are precomputed in CI because WebGL has
    /// no scriptable PCM/FFT callback. Native catalogue patterns are independent and
    /// NEVER described as verified musical matches.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-800)]
    public sealed class KandiBoomSongPlayer : MonoBehaviour
    {
        public static readonly string[] BandNames = { "Sub", "Bass", "Low-mid", "Mid", "High", "Onset", "Width", "Energy" };
        [SerializeField] private DanceFlowClockBehaviour clock;
        [SerializeField] private AudioSource player;
        private KandiWebGLSongCatalogue _catalogue;
        private KandiWebGLAudioAnalysis _analysis;
        private KandiWebGLPatternLibrary _patterns;
        private KandiWebGLSong _song;
        private Coroutine _loading;
        private bool _paused;
        private bool _hasStarted;
        private string _status = "Loading pinned song catalogue...";
        private int _selection = -1;

        public string Status => _status;
        public KandiWebGLSong Song => _song;
        public int SongCount => _catalogue?.songs?.Length ?? 0;
        public int SelectedIndex => _selection;
        public int PatternCount => _patterns?.patterns?.Length ?? 0;
        public bool HasStarted => _hasStarted;
        public bool Ready => _song != null && _analysis != null && player != null && player.clip != null;
        public bool IsPlaying => Ready && clock != null && clock.Transport != null && clock.Transport.IsRunning;
        public float Duration => Ready ? player.clip.length : 0;
        public float Position => Ready && clock.Transport != null ? (float)clock.Current.SourceSeconds : 0;
        public float Volume
        {
            get => player != null ? player.volume : 0;
            set { if (player != null) player.volume = Mathf.Clamp01(value); }
        }

        public void Configure(DanceFlowClockBehaviour authoritativeClock, AudioSource audio)
        {
            clock = authoritativeClock;
            player = audio;
            if (player != null)
            {
                player.playOnAwake = false;
                player.loop = false;
                player.spatialBlend = 0; // Full-volume music, not attenuated by the camera.
            }
        }

        private void Start()
        {
            if (clock == null || player == null)
            {
                _status = "Missing shared clock or AudioSource";
                Debug.LogError(_status, this);
                return;
            }
            clock.BindAudioSource(player);
            StartCoroutine(LoadCatalogue());
        }

        private string SongUrl(string filename) =>
            Application.streamingAssetsPath.TrimEnd('/') + "/Songs/" + filename;

        private IEnumerator LoadCatalogue()
        {
            using (var request = UnityWebRequest.Get(SongUrl("rudiments.json")))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    _status = "Cannot load official rudiment patterns: " + request.error;
                    yield break;
                }
                try
                {
                    _patterns = JsonUtility.FromJson<KandiWebGLPatternLibrary>(request.downloadHandler.text);
                    if (_patterns == null || _patterns.schema != "danceflow.unity.webgl-rudiments.v1" ||
                        _patterns.patterns == null || _patterns.patterns.Length < 4)
                        throw new FormatException("Invalid compiled DanceRudiments sample pack");
                    foreach (var p in _patterns.patterns)
                        if (p == null || !p.Valid) throw new FormatException("Invalid rudiment samples");
                }
                catch (Exception e) { _status = "Rudiment catalogue rejected: " + e.Message; _patterns = null; }
                if (_patterns == null) yield break;
            }
            using (var request = UnityWebRequest.Get(SongUrl("catalogue.json")))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    _status = "Cannot load song catalogue: " + request.error;
                    yield break;
                }
                try
                {
                    _catalogue = JsonUtility.FromJson<KandiWebGLSongCatalogue>(request.downloadHandler.text);
                    if (_catalogue == null || _catalogue.schema != "danceflow.unity.webgl-song-gallery.v1" ||
                        _catalogue.songs == null || _catalogue.songs.Length == 0)
                        throw new FormatException("Invalid song catalogue");
                }
                catch (Exception e) { _status = "Song catalogue rejected: " + e.Message; _catalogue = null; }
                if (_catalogue == null) yield break;
            }
            Select(0);
        }

        public string SongTitle(int index) => _catalogue != null && index >= 0 && index < SongCount
            ? _catalogue.songs[index].title : "Unavailable";

        public string PatternName(int index) => index >= 0 && index < PatternCount
            ? _patterns.patterns[index].name : "(unavailable)";

        public int PrimaryAxis(int index) => index >= 0 && index < PatternCount
            ? _patterns.patterns[index].primary_axis : 0;

        public void Select(int index)
        {
            if (_catalogue == null || index < 0 || index >= SongCount) return;
            if (_loading != null) StopCoroutine(_loading);
            if (clock.Transport != null) clock.Stop();
            if (player.clip != null)
            {
                var old = player.clip;
                player.clip = null;
                Destroy(old);
            }
            _analysis = null;
            _song = null;
            _paused = false;
            _hasStarted = false;
            _selection = index;
            _status = "Loading " + SongTitle(index) + "...";
            _loading = StartCoroutine(LoadSelected(_catalogue.songs[index]));
        }

        private IEnumerator LoadSelected(KandiWebGLSong item)
        {
            if (string.IsNullOrEmpty(item.audio_file) || string.IsNullOrEmpty(item.signals_file) ||
                item.audio_file.IndexOf('/') >= 0 || item.signals_file.IndexOf('/') >= 0)
            {
                _status = "Invalid source-bound song paths";
                yield break;
            }
            KandiWebGLAudioAnalysis features;
            using (var request = UnityWebRequest.Get(SongUrl(item.signals_file)))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    _status = "Failed to load measured motion envelopes: " + request.error;
                    yield break;
                }
                try { features = JsonUtility.FromJson<KandiWebGLAudioAnalysis>(request.downloadHandler.text); }
                catch (Exception e) { _status = "Invalid analysis JSON: " + e.Message; features = null; }
                if (features == null || !features.ValidFor(item))
                {
                    _status = "Analysis is not bound to the selected master SHA-256";
                    yield break;
                }
            }
            AudioClip clip;
            using (var request = UnityWebRequestMultimedia.GetAudioClip(SongUrl(item.audio_file), AudioType.MPEG))
            {
                // WebGL audio is browser-decoded asynchronously. streamAudio is not
                // supported there: wait for the full download/decode before playback.
                var handler = (DownloadHandlerAudioClip)request.downloadHandler;
                handler.streamAudio = false;
                handler.compressed = false;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    _status = "Cannot decode original song: " + request.error;
                    yield break;
                }
                clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null)
                {
                    _status = "Audio download returned no clip";
                    yield break;
                }
                float deadline = Time.realtimeSinceStartup + 30;
                while (clip.loadState != AudioDataLoadState.Loaded &&
                       clip.loadState != AudioDataLoadState.Failed &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    Destroy(clip);
                    _status = "Browser could not decode the audio format";
                    yield break;
                }
            }
            if (clip == null || clip.length < 1)
            {
                _status = "Browser returned an empty audio clip";
                yield break;
            }
            try
            {
                // Apply only verified lock metadata. Truth's beat anchors are ESTIMATED
                // BeatThis/BeatNet consensus, never an artist-confirmed tempo or phase.
                if (item.beat_seconds != null && item.beat_seconds.Length > 1)
                {
                    var anchors = new BeatAnchor[item.beat_seconds.Length];
                    for (int i = 0; i < anchors.Length; i++)
                        anchors[i] = new BeatAnchor(i, (long)Math.Round(item.beat_seconds[i] * item.source_sample_rate));
                    clock.UseVerifiedBeatMap(new VariableBeatMap(anchors, item.source_sample_rate,
                        item.source_frames, item.origin_beat, item.audio_sha256,
                        item.beat_map_sha256, "estimated"));
                }
                else if (item.bpm > 0)
                    clock.UseFixedTempo(item.bpm); // Arcadians confirmed; phase not confirmed.
                else if (item.preview_bpm == 120 && item.tempo_status == "not_verified_preview_120")
                    clock.UseFixedTempo(item.preview_bpm); // explicitly SYNTHETIC motion tempo, not detected BPM.
                else
                    throw new FormatException("No reviewed BPM or clearly-labelled preview pulse for this song");
                clock.Transport.SetExcerpt(0, clip.length);
                clock.SeekSourceSeconds(0);
                player.clip = clip;
                _analysis = features;
                _song = item;
                _status = item.bpm == 0 ?
                    "Ready — real audio; 120 BPM motion PREVIEW only" :
                    "Ready — click Play to enable browser audio";
            }
            catch (Exception e)
            {
                Destroy(clip);
                _status = "Song clock rejected: " + e.Message;
            }
            _loading = null;
        }

        public void PlayPause()
        {
            if (!Ready) return;
            if (clock.Transport.IsRunning)
            {
                clock.Pause(); _paused = true;
                _status = "Paused";
            }
            else if (_paused)
            {
                clock.Resume(); _paused = false;
                _status = "Playing";
            }
            else
            {
                if (Position > Duration - 0.5f) clock.SeekSourceSeconds(0);
                // This must be called from an actual user click: browsers block autoplay.
                clock.PlayScheduled();
                _hasStarted = true;
                _status = "Playing";
            }
        }

        public void Seek(float seconds)
        {
            if (!Ready) return;
            clock.SeekSourceSeconds(Mathf.Clamp(seconds, 0, Math.Max(0, Duration - 0.1f)));
        }

        private void Update()
        {
            if (IsPlaying && clock.Current.SourceSeconds >= Duration - 0.08f)
            {
                clock.Stop();
                _paused = false;
                _hasStarted = false;
                _status = "Track ended — click Play to replay";
            }
        }

        public float Signal(int channel, double sourceSeconds)
        {
            if (!Ready || sourceSeconds < 0 || sourceSeconds >= _song.duration_seconds) return 0;
            var samples = _analysis.Channel(channel);
            if (samples == null || samples.Length < 2) return 0;
            double at = sourceSeconds / _analysis.frame_seconds;
            int low = Math.Max(0, Math.Min(samples.Length - 2, (int)Math.Floor(at)));
            float phase = Mathf.Clamp01((float)(at - low));
            return Mathf.Lerp(samples[low], samples[low + 1], phase);
        }

        public float PatternValue(int patternIndex, int axis, double absoluteBeat)
        {
            if (patternIndex < 0 || patternIndex >= PatternCount) return 0;
            KandiWebGLPattern pattern = _patterns.patterns[patternIndex];
            float[] samples = pattern.Axis(axis);
            if (samples == null || samples.Length != pattern.period_pips ||
                double.IsNaN(absoluteBeat) || double.IsInfinity(absoluteBeat)) return 0;
            double pip = absoluteBeat * 64.0;
            double wrapped = ((pip % pattern.period_pips) + pattern.period_pips) % pattern.period_pips;
            int a = (int)Math.Floor(wrapped);
            int b = (a + 1) % pattern.period_pips;
            return Mathf.Lerp(samples[a], samples[b], (float)(wrapped - a));
        }

        private void OnDestroy()
        {
            if (player != null && player.clip != null)
            {
                var clip = player.clip;
                player.clip = null;
                Destroy(clip);
            }
        }
    }
}
