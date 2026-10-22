using UnityEngine;

namespace DanceFlow.Unity.Motion
{
    public enum MotionPropertyTarget { LightIntensity, ParticleEmissionRate, RendererFloat, RendererColour }

    /// <summary>
    /// Typed motion-to-property binding: no reflection, code execution or dynamic channel names.
    /// RendererPropertyBlock is updated without cloning materials. One binding owns its target
    /// property; it restores captured target values on disable.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(9400)]
    public sealed class MotionPropertyDriver : MonoBehaviour
    {
        [SerializeField] private MotionSignalBus source;
        [SerializeField, Range(0, 3)] private int slot;
        [SerializeField] private MotionPropertyTarget target;
        [SerializeField] private Light targetLight;
        [SerializeField] private ParticleSystem targetParticles;
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private string shaderProperty = "_GlowGain";
        [SerializeField] private float minValue = 0.2f;
        [SerializeField] private float maxValue = 2.0f;
        [SerializeField, Min(0)] private float gain = 1f;
        [SerializeField] private Color lowColour = Color.blue;
        [SerializeField] private Color highColour = Color.magenta;
        private float _oldLight;
        private ParticleSystem.MinMaxCurve _oldRate;
        private MaterialPropertyBlock _block;
        private MaterialPropertyBlock _initialBlock;
        private int _propertyId;

        private void OnEnable()
        {
            if (source == null) source = MotionSignalBus.Active;
            if (targetLight != null) _oldLight = targetLight.intensity;
            if (targetParticles != null)
            {
                var emission = targetParticles.emission;
                _oldRate = emission.rateOverTime;
            }
            if (targetRenderer != null)
            {
                _block = new MaterialPropertyBlock();
                _initialBlock = new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(_initialBlock);
                _propertyId = Shader.PropertyToID(shaderProperty);
            }
        }
        private void LateUpdate()
        {
            if (source == null || !source.HasSample) return;
            float t = Mathf.Clamp01(0.5f + 0.5f * source.Value(slot) * gain);
            float value = Mathf.Lerp(minValue, maxValue, t);
            switch (target)
            {
                case MotionPropertyTarget.LightIntensity:
                    if (targetLight != null) targetLight.intensity = value;
                    break;
                case MotionPropertyTarget.ParticleEmissionRate:
                    if (targetParticles != null)
                    {
                        var emission = targetParticles.emission;
                        emission.rateOverTime = value;
                    }
                    break;
                case MotionPropertyTarget.RendererFloat:
                case MotionPropertyTarget.RendererColour:
                    if (targetRenderer != null && _block != null)
                    {
                        targetRenderer.GetPropertyBlock(_block); // retain other component's properties
                        if (target == MotionPropertyTarget.RendererFloat) _block.SetFloat(_propertyId, value);
                        else _block.SetColor(_propertyId, Color.Lerp(lowColour, highColour, t));
                        targetRenderer.SetPropertyBlock(_block);
                    }
                    break;
            }
        }
        private void OnDisable()
        {
            if (targetLight != null) targetLight.intensity = _oldLight;
            if (targetParticles != null)
            {
                var emission = targetParticles.emission;
                emission.rateOverTime = _oldRate;
            }
            if (targetRenderer != null && _initialBlock != null)
                targetRenderer.SetPropertyBlock(_initialBlock);
        }
    }
}
