//UNITY_SHADER_NO_UPGRADE
#ifndef DANCEFLOW_MOTION_SIGNALS_INCLUDED
#define DANCEFLOW_MOTION_SIGNALS_INCLUDED
// Four signed, [-1,1] scene slots: wind, gust, palette and shimmer.
// They map to configurable native source dimension names (default x/y/z/x).
// _DanceFlow_MotionIsRecording=0 explicitly means synthetic preview, not a real match.
float4 _DanceFlow_MotionA;
float4 _DanceFlow_MotionB;
float _DanceFlow_MotionAvailable;
float _DanceFlow_MotionIsRecording;

void DF_GetMotionSignals_float(out float4 Signals, out float4 Magnitudes, out float IsRecorded)
{
    Signals = _DanceFlow_MotionA * _DanceFlow_MotionAvailable;
    Magnitudes = _DanceFlow_MotionB * _DanceFlow_MotionAvailable;
    IsRecorded = _DanceFlow_MotionIsRecording;
}
#endif
