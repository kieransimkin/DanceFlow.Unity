Shader "DanceFlow/URP/BeatPulseVertex"
{
    Properties
    {
        _BaseColor("Base Colour", Color) = (0.05, 0.45, 0.9, 1)
        _Displacement("Vertex Bounce Metres", Range(0, 0.6)) = 0.15
        _Glow("Beat Glow Gain", Range(0, 4)) = 1.6
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.kieransimkin.danceflow.unity/Runtime/GPU/DanceFlowClock.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Displacement;
                float _Glow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 moved = input.positionOS.xyz;
                DF_GetVertexBounce_float(input.positionOS.xyz, input.normalOS, _Displacement, moved);
                output.positionHCS = TransformObjectToHClip(moved);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float pulse = _DanceFlow_BeatPulse * DF_ClockValid();
                float bar = _DanceFlow_DownbeatPulse * DF_ClockValid();
                half lighting = (half)(0.28 + 0.72 * saturate(input.normalWS.y * 0.6 + 0.4));
                half3 rgb = _BaseColor.rgb * (lighting + (half)(_Glow * pulse + 0.6 * bar));
                return half4(rgb, _BaseColor.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
