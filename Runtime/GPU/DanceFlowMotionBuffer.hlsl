//UNITY_SHADER_NO_UPGRADE
#ifndef DANCEFLOW_MOTION_BUFFER_INCLUDED
#define DANCEFLOW_MOTION_BUFFER_INCLUDED
// Optional GPU recording buffer. Requires shader target 4.5 and capable hardware.
// The CPU determines the source-to-recording pip coordinate and publishes a bounded
// row/fraction cursor; the shader never guesses BPM or wraps finite captures.
StructuredBuffer<float> _DanceFlow_RecordingSamples;
float _DanceFlow_RecordingBufferValid;
// x: row relative to the current bounded overlapping GPU window; y: [0,1)
// z: dimension count; w: row count
float4 _DanceFlow_RecordingCursor;

float DF_MotionBufferSample(uint channel)
{
    if (_DanceFlow_RecordingBufferValid < 0.5) return 0;
    uint dims = (uint)_DanceFlow_RecordingCursor.z;
    uint rows = (uint)_DanceFlow_RecordingCursor.w;
    uint row = (uint)_DanceFlow_RecordingCursor.x;
    if (dims == 0 || rows == 0 || row >= rows || channel >= dims) return 0;
    float current = _DanceFlow_RecordingSamples[row * dims + channel];
    if (row + 1 >= rows) return current;
    float next = _DanceFlow_RecordingSamples[(row + 1) * dims + channel];
    return lerp(current, next, saturate(_DanceFlow_RecordingCursor.y));
}
#endif
