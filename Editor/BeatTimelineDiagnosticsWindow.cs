using DanceFlow.Unity.Clock;
using UnityEditor;
using UnityEngine;

namespace DanceFlow.Unity.Editor
{
    public sealed class BeatTimelineDiagnosticsWindow : EditorWindow
    {
        private DanceFlowClockBehaviour _clock;

        [MenuItem("Window/DanceFlow/Musical Clock Diagnostics")]
        public static void Open() => GetWindow<BeatTimelineDiagnosticsWindow>("DanceFlow Clock");

        private void OnGUI()
        {
            _clock = (DanceFlowClockBehaviour)EditorGUILayout.ObjectField("Clock component", _clock,
                typeof(DanceFlowClockBehaviour), true);
            if (_clock == null || _clock.Transport == null)
            {
                EditorGUILayout.HelpBox("Select an active DanceFlowClockBehaviour in Play mode.", MessageType.Info);
                return;
            }
            BeatState s = _clock.Current;
            EditorGUILayout.LabelField("Master source (s)", s.SourceSeconds.ToString("F6"));
            EditorGUILayout.LabelField("Current BPM", s.Bpm.ToString("F6"));
            EditorGUILayout.LabelField("Absolute beat", s.AbsoluteBeat.ToString("F6"));
            EditorGUILayout.LabelField("16-tick index", s.Tick16.ToString());
            EditorGUILayout.LabelField("64-pip index", s.Pip64.ToString());
            EditorGUILayout.LabelField("Beat phase", s.BeatPhase.ToString("F6"));
            EditorGUILayout.LabelField("Bar phase", s.BarPhase.ToString("F6"));
            EditorGUILayout.LabelField("Loop iteration", s.LoopIteration.ToString());
            EditorGUILayout.LabelField("Transport generation", s.TransportGeneration.ToString());
            if (EditorApplication.isPlaying) Repaint();
        }
    }
}
