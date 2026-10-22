using DanceFlow.Unity.Motion;
using UnityEngine;

namespace DanceFlow.Unity.Samples
{
    /// <summary>Gentle, explicit scene-owned fog/sky/light modulation; restores settings at unload.</summary>
    [DefaultExecutionOrder(9500)]
    public sealed class SongShowcaseAtmosphere : MonoBehaviour
    {
        [SerializeField] private MotionSignalBus source;
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private Light moon;
        [SerializeField] private Color night = new Color(0.015f, 0.025f, 0.10f);
        [SerializeField] private Color dusk = new Color(0.11f, 0.035f, 0.16f);
        private Color _originalSky, _originalFog;
        private float _originalLight;
        private bool _originalFogEnabled;
        private FogMode _originalFogMode;
        private float _originalFogDensity;
        private bool _saved;
        public void Configure(MotionSignalBus bus, Camera camera, Light light)
        {
            source = bus; sceneCamera = camera; moon = light;
            if (_saved)
            {
                _originalSky = camera != null ? camera.backgroundColor : Color.black;
                if (light != null) _originalLight = light.intensity;
            }
        }
        private void OnEnable()
        {
            if (sceneCamera == null) sceneCamera = Camera.main;
            _originalFogEnabled = RenderSettings.fog;
            _originalFogMode = RenderSettings.fogMode;
            _originalFogDensity = RenderSettings.fogDensity;
            _originalFog = RenderSettings.fogColor;
            _originalSky = sceneCamera != null ? sceneCamera.backgroundColor : Color.black;
            if (moon != null) _originalLight = moon.intensity;
            _saved = true;
        }
        private void LateUpdate()
        {
            if (!Application.isPlaying || source == null || !source.HasSample) return;
            float palette = Mathf.Clamp01(0.5f + 0.5f * source.Value(2));
            Color sky = Color.Lerp(night, dusk, palette);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.004f;
            RenderSettings.fogColor = sky;
            if (sceneCamera != null) sceneCamera.backgroundColor = sky;
            if (moon != null) moon.intensity = 0.45f + 0.38f * Mathf.Abs(source.Value(0));
        }
        private void OnDisable()
        {
            if (!_saved) return;
            RenderSettings.fog = _originalFogEnabled;
            RenderSettings.fogMode = _originalFogMode;
            RenderSettings.fogDensity = _originalFogDensity;
            RenderSettings.fogColor = _originalFog;
            if (sceneCamera != null) sceneCamera.backgroundColor = _originalSky;
            if (moon != null) moon.intensity = _originalLight;
            _saved = false;
        }
    }
}
