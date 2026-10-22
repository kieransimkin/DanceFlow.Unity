using DanceFlow.Unity.GPU;
using UnityEditor;
using UnityEngine;

namespace DanceFlow.Unity.Editor
{
    public sealed class ShaderClockDiagnosticsWindow : EditorWindow
    {
        private DanceFlowShaderBridge _bridge;
        private DanceFlowShaderMetrics _metrics;
        private double _seekSeconds;
        private DanceFlowShaderMetrics.MetricsReport _lastReport;

        [MenuItem("Window/DanceFlow/Shader Clock Diagnostics")]
        public static void Open() => GetWindow<ShaderClockDiagnosticsWindow>("Shader Clock");

        private void OnGUI()
        {
            _bridge = (DanceFlowShaderBridge)EditorGUILayout.ObjectField("Global bridge", _bridge,
                typeof(DanceFlowShaderBridge), true);
            if (_bridge == null) _bridge = DanceFlowShaderBridge.Active;
            _metrics = (DanceFlowShaderMetrics)EditorGUILayout.ObjectField("Optional metrics", _metrics,
                typeof(DanceFlowShaderMetrics), true);
            if (_bridge == null)
            {
                EditorGUILayout.HelpBox("Add exactly one enabled DanceFlowShaderBridge to your scene.", MessageType.Info);
                return;
            }
            if (_bridge.HasPublishedState)
            {
                var s = _bridge.LastSample;
                EditorGUILayout.LabelField("BPM", s.Bpm.ToString("F4"));
                EditorGUILayout.LabelField("Beat phase", s.BeatPhase.ToString("F6"));
                EditorGUILayout.LabelField("Bar phase", s.BarPhase.ToString("F6"));
                EditorGUILayout.LabelField("Tick / pip phase", s.TickPhase.ToString("F5") + " / " + s.PipPhase.ToString("F5"));
                EditorGUILayout.LabelField("Beat index hi / low", s.BeatIndexHigh + " / " + s.BeatIndexLow);
                EditorGUILayout.LabelField("Song seconds (hi+low)", (s.SourceSecondsHigh + (double)s.SourceSecondsLow).ToString("F6"));
                EditorGUILayout.LabelField("Published snapshots", _bridge.PublishedStates.ToString());
                EditorGUILayout.LabelField("Publisher mean CPU", _bridge.PublisherMeasuredCount > 0 ?
                    _bridge.PublisherAverageMicroseconds.ToString("F3") + " μs" : "not measured");
            }
            _seekSeconds = EditorGUILayout.DoubleField("Preview source seconds", _seekSeconds);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Publish at source seconds (manual override)"))
                {
                    try { _bridge.PublishAtSourceSeconds(_seekSeconds); }
                    catch (System.Exception e) { Debug.LogException(e); }
                }
                if (GUILayout.Button("Release manual override")) _bridge.ReleaseManualOverride();
            }
            if (_metrics != null)
            {
                EditorGUILayout.LabelField("CPU frame", _metrics.HasCpuTiming ?
                    _metrics.MeanCpuFrameMs.ToString("F3") + " ms" : "unavailable");
                EditorGUILayout.LabelField("GPU frame", _metrics.HasGpuTiming ?
                    _metrics.MeanGpuFrameMs.ToString("F3") + " ms" : "unavailable");
                EditorGUILayout.LabelField("Capture state", _metrics.IsCapturing ? "recording" : "stopped");
                EditorGUILayout.LabelField("Frame observations", _metrics.SamplesTaken.ToString());
                EditorGUILayout.LabelField("GC allocated/frame", _metrics.HasGcAllocationCounter ?
                    _metrics.LastGcAllocatedBytes + " bytes" : "unavailable");
                EditorGUILayout.LabelField("Video used (Unity estimate)", _metrics.HasVideoMemoryEstimate ?
                    (_metrics.LastVideoUsedBytes / 1048576.0).ToString("F2") + " MiB" : "unavailable");
                EditorGUILayout.LabelField("Reserved memory", _metrics.HasReservedMemoryCounter ?
                    (_metrics.LastReservedBytes / 1048576.0).ToString("F2") + " MiB" : "unavailable");
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    if (!_metrics.IsCapturing && GUILayout.Button("Start opt-in detailed capture"))
                        _metrics.BeginCapture("manual", "editor-manual", resourceCounters: true);
                    if (_metrics.IsCapturing && GUILayout.Button("Stop capture")) _metrics.StopCapture();
                    if (GUILayout.Button("Calculate percentiles")) _lastReport = _metrics.GetReport();
                }
                if (_lastReport != null)
                {
                    EditorGUILayout.LabelField("CPU p95", _lastReport.cpuFrameMs.count > 0 ?
                        _lastReport.cpuFrameMs.p95.ToString("F3") + " ms" : "unavailable");
                    EditorGUILayout.LabelField("GPU p95", _lastReport.gpuFrameMs.count > 0 ?
                        _lastReport.gpuFrameMs.p95.ToString("F3") + " ms" : "unavailable");
                    EditorGUILayout.LabelField("GC p95", _lastReport.gcAllocatedBytes.count > 0 ?
                        _lastReport.gcAllocatedBytes.p95.ToString("F1") + " bytes" : "unavailable");
                }
                if (GUILayout.Button("Export metrics JSON (explicit)"))
                {
                    var path = EditorUtility.SaveFilePanel("Export DanceFlow shader metrics", "", "shader-metrics.json", "json");
                    if (!string.IsNullOrEmpty(path)) _metrics.ExportToJson(path);
                }
            }
            if (EditorApplication.isPlaying) Repaint();
        }
    }
}
