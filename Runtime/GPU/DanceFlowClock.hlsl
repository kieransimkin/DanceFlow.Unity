//UNITY_SHADER_NO_UPGRADE
#ifndef DANCEFLOW_CLOCK_HLSL_INCLUDED
#define DANCEFLOW_CLOCK_HLSL_INCLUDED

// DanceFlow shader API 2. Global values come ONLY from DanceFlowShaderBridge.
// Do not also declare these names in Unity shader Properties/UnityPerMaterial buffers.
float _DanceFlow_ShaderApiVersion;
float _DanceFlow_BPM;
float _DanceFlow_BeatPhase;
float _DanceFlow_BarPhase;
float _DanceFlow_TickPhase;
float _DanceFlow_PipPhase;
float _DanceFlow_BeatPulse;
float _DanceFlow_DownbeatPulse;
float _DanceFlow_BeatSin;
float _DanceFlow_BeatCos;
float _DanceFlow_Meter;
float _DanceFlow_IsPlaying;
float _DanceFlow_BeatRemainder256;
float4 _DanceFlow_BeatIndexHiLo;
float4 _DanceFlow_SourceSecondsHiLo;
float4 _DanceFlow_TransportGenerationHiLo;

float DF_PositiveMod(float value, float period)
{
    return value - period * floor(value / period);
}

float DF_ClockValid()
{
    return step(1.5, _DanceFlow_ShaderApiVersion);
}

// Input is an INTEGER number of beats in [1,256]. Avoid rebuilding a huge float
// from hi+low: perform Euclidean modular arithmetic on each integer chunk instead.
float DF_CyclePhase(float periodBeats)
{
    float p = clamp(floor(periodBeats + 0.5), 1.0, 256.0);
    float highRem = DF_PositiveMod(_DanceFlow_BeatIndexHiLo.x, p);
    float lowRem = DF_PositiveMod(_DanceFlow_BeatIndexHiLo.y, p);
    float radixRem = DF_PositiveMod(65536.0, p);
    float wholeRem = DF_PositiveMod(DF_PositiveMod(highRem * radixRem, p) + lowRem, p);
    return DF_PositiveMod((wholeRem + _DanceFlow_BeatPhase) / p, 1.0) * DF_ClockValid();
}

// Musical subdivisions smaller than a beat should operate on the *fractional*
// beat rather than a huge absolute beat number.
float DF_SubdivisionPhase(float divisionsPerBeat)
{
    return frac(_DanceFlow_BeatPhase * max(1.0, divisionsPerBeat)) * DF_ClockValid();
}

float DF_BeatPhaseOffset(float offsetBeats)
{
    return frac(_DanceFlow_BeatPhase + offsetBeats) * DF_ClockValid();
}

float DF_PulseEnvelope(float phase, float exponent)
{
    return pow(saturate(1.0 - phase), max(0.01, exponent));
}

float DF_SwingPhase(float phase, float firstHalfFraction)
{
    float splitPoint = clamp(firstHalfFraction, 0.5, 0.9);
    return phase < splitPoint ? (phase * 0.5 / splitPoint) :
           (0.5 + (phase - splitPoint) * 0.5 / (1.0 - splitPoint));
}

// Functions suffixed _float and _half are immediately usable in Shader Graph's
// File-mode Custom Function node. Supply the name WITHOUT the suffix in its UI.
void DF_GetClock_float(out float BeatPhase, out float BarPhase, out float BPM,
                       out float BeatPulse, out float DownbeatPulse)
{
    BeatPhase = _DanceFlow_BeatPhase * DF_ClockValid();
    BarPhase = _DanceFlow_BarPhase * DF_ClockValid();
    BPM = _DanceFlow_BPM * DF_ClockValid();
    BeatPulse = _DanceFlow_BeatPulse * DF_ClockValid();
    DownbeatPulse = _DanceFlow_DownbeatPulse * DF_ClockValid();
}

void DF_GetClock_half(out half BeatPhase, out half BarPhase, out half BPM,
                      out half BeatPulse, out half DownbeatPulse)
{
    float b, bar, bpm, pulse, down;
    DF_GetClock_float(b, bar, bpm, pulse, down);
    BeatPhase = (half)b;
    BarPhase = (half)bar;
    BPM = (half)bpm;
    BeatPulse = (half)pulse;
    DownbeatPulse = (half)down;
}

void DF_GetCycle_float(float PeriodBeats, out float Phase)
{
    Phase = DF_CyclePhase(PeriodBeats);
}

void DF_GetCycle_half(half PeriodBeats, out half Phase)
{
    Phase = (half)DF_CyclePhase((float)PeriodBeats);
}

void DF_GetPulse_float(float OffsetBeats, float Exponent, out float Pulse)
{
    Pulse = DF_PulseEnvelope(DF_BeatPhaseOffset(OffsetBeats), Exponent) * DF_ClockValid();
}

void DF_GetPulse_half(half OffsetBeats, half Exponent, out half Pulse)
{
    Pulse = (half)(DF_PulseEnvelope(DF_BeatPhaseOffset((float)OffsetBeats), (float)Exponent)
                   * DF_ClockValid());
}

void DF_GetVertexBounce_float(float3 PositionOS, float3 NormalOS, float Amplitude,
                              out float3 DisplacedPositionOS)
{
    DisplacedPositionOS = PositionOS + NormalOS * (Amplitude * _DanceFlow_BeatPulse * DF_ClockValid());
}

void DF_GetVertexBounce_half(half3 PositionOS, half3 NormalOS, half Amplitude,
                             out half3 DisplacedPositionOS)
{
    DisplacedPositionOS = PositionOS + NormalOS * (Amplitude * (half)_DanceFlow_BeatPulse
                                                     * (half)DF_ClockValid());
}

#endif // DANCEFLOW_CLOCK_HLSL_INCLUDED
