Shader "DanceFlow/URP/RecordedMotionWarp"
{
    Properties
    {
        _BaseColor("Base colour", Color) = (0.6, 0.2, 0.9, 1)
        _Mode("Warp mode", Float) = 4
        _WindGain("Wind", Range(0, 3)) = 0.1
        _WarpGain("Warp amplitude", Range(0, 3)) = 1.4
        _PaletteGain("Palette shift", Range(0, 3)) = 0.8
        _GlowGain("Glow", Range(0, 4)) = 0.8
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "MotionWarp"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            HLSLPROGRAM
            // StructuredBuffer requires a modern device. Generic landscape fallback is target 3.0.
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.kieransimkin.danceflow.unity/Runtime/GPU/DanceFlowClock.hlsl"
            #include "Packages/com.kieransimkin.danceflow.unity/Runtime/GPU/DanceFlowMotionSignals.hlsl"
            #include "Packages/com.kieransimkin.danceflow.unity/Runtime/GPU/DanceFlowMotionBuffer.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Mode;
                float _WindGain;
                float _WarpGain;
                float _PaletteGain;
                float _GlowGain;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionHCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            float2 ResolvedMotion()
            {
                if (_DanceFlow_RecordingBufferValid > 0.5)
                    return float2(DF_MotionBufferSample(0), DF_MotionBufferSample(1));
                return _DanceFlow_MotionA.xy * _DanceFlow_MotionAvailable;
            }
            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(input.positionOS);
                float2 m = ResolvedMotion();
                float beat = _DanceFlow_BeatPhase * 6.28318530718;
                float angle = world.y * 0.8 + beat + m.x * 2.4;
                float warp = _WarpGain * (0.35 + 0.55 * abs(m.x) + 0.3 * abs(m.y));
                world.xz += float2(cos(angle), sin(angle)) * warp;
                world.x += sin(world.y * 1.2 + m.y * 3.4) * warp * 0.35;
                o.positionWS = world;
                o.positionHCS = TransformWorldToHClip(world);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 m = ResolvedMotion();
                float shift = (_DanceFlow_BarPhase * 0.5 + m.y * 0.35) * _PaletteGain;
                float3 c = _BaseColor.rgb;
                float3 rotated = c * (0.65 + 0.35 * cos(shift * 6.28318530718)) +
                                 c.gbr * (0.18 + 0.18 * sin(shift * 6.28318530718));
                float pulse = _DanceFlow_BeatPulse * DF_ClockValid();
                float lighting = 0.4 + 0.6 * abs(normalize(input.normalWS).y);
                return half4(saturate(rotated) * (lighting + _GlowGain * (0.4 + pulse + abs(m.x) * 0.6)), _BaseColor.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
