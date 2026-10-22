Shader "DanceFlow/URP/MotionLandscape"
{
    Properties
    {
        _BaseColor("Base tint", Color) = (0.3, 0.6, 0.6, 1)
        _Mode("0 ground, 1 grass, 2 trunk, 3 canopy, 4 warp", Float) = 0
        _WindGain("Wind gain", Range(0, 3)) = 0.8
        _WarpGain("Geometry warping", Range(0, 3)) = 0.6
        _PaletteGain("Hue modulation", Range(0, 2)) = 0.4
        _GlowGain("Bioluminescent glow", Range(0, 4)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "MotionLandscape"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.kieransimkin.danceflow.unity/Runtime/GPU/DanceFlowClock.hlsl"
            #include "Packages/com.kieransimkin.danceflow.unity/Runtime/GPU/DanceFlowMotionSignals.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Mode;
                float _WindGain;
                float _WarpGain;
                float _PaletteGain;
                float _GlowGain;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 bend : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float4 color : COLOR;
                float3 world : TEXCOORD1;
            };

            float3 HueRotate(float3 colour, float angle)
            {
                float3 axis = float3(0.577350269, 0.577350269, 0.577350269);
                float cs = cos(angle), sn = sin(angle);
                float3 result = colour * cs + cross(axis, colour) * sn +
                                axis * dot(axis, colour) * (1 - cs);
                return saturate(result);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS);
                float valid = _DanceFlow_MotionAvailable * DF_ClockValid();
                float4 motion = _DanceFlow_MotionA * valid;
                float beatPulse = _DanceFlow_BeatPulse * DF_ClockValid();
                float phase = _DanceFlow_BeatPhase * 6.28318530718;
                float gust = 0.35 + 0.4 * abs(motion.y) + 0.25 * abs(motion.x);
                float windField = sin(world.x * 0.18 + world.z * 0.13 + phase + input.bend.y * 4.3);
                float sway = input.bend.x * _WindGain * gust * windField;

                if (_Mode > 0.5 && _Mode < 3.5)
                {
                    world.x += sway + input.bend.x * motion.x * 0.22 * _WindGain;
                    world.z += input.bend.x * (0.6 * sway + motion.y * 0.12 * _WindGain);
                }
                // Radial terrain ripples (strength tied to measured channels when supplied).
                if (_Mode < 0.5)
                {
                    float r = length(world.xz);
                    world.y += _WarpGain * (0.08 + abs(motion.x) * 0.17) *
                               sin(r * 0.56 - phase + _DanceFlow_BarPhase * 2.2);
                }
                // Music-warped abstract tower. Distinct radial and torsional modes.
                if (_Mode > 3.5 && _Mode < 4.5)
                {
                    float theta = world.y * 0.72 + phase + motion.z * 2.5;
                    float radius = _WarpGain * (0.16 + beatPulse * 0.7 + abs(motion.y) * 0.35);
                    world.xz += float2(cos(theta), sin(theta)) * radius;
                    world.y += _WarpGain * sin(theta * 2.2) * 0.2;
                }
                if (_Mode > 2.5 && _Mode < 3.5)
                {
                    world.xyz += float3(sway * 0.35, beatPulse * 0.08, sway * 0.1);
                }
                output.world = world;
                output.positionHCS = TransformWorldToHClip(world);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float4 m = _DanceFlow_MotionA * _DanceFlow_MotionAvailable;
                float hue = (_DanceFlow_BarPhase * 0.4 + m.z * 0.34 + m.w * 0.12) * _PaletteGain;
                float3 rgb = HueRotate(_BaseColor.rgb * input.color.rgb, hue * 6.28318530718);
                float normalLight = saturate(0.46 + 0.50 * abs(normalize(input.normalWS).y));
                float glow = (_DanceFlow_BeatPulse * 0.45 + abs(m.w) * 0.22) * _GlowGain;
                if (_Mode > 3.5 && _Mode < 4.5) glow += 0.75 + abs(m.x) * 0.4;
                if (_Mode > 4.5) glow += 0.7 + _GlowGain * (0.5 + abs(m.w));
                if (_Mode > 2.5 && _Mode < 3.5) glow += 0.18;
                return half4(rgb * (normalLight + glow), _BaseColor.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
