using System;

namespace DanceFlow.Unity.Motion
{
    /// <summary>One bounded GPU upload window, with a 1-sample overlap for linear interpolation.</summary>
    public readonly struct RecordingGpuWindow
    {
        public readonly int StartRow;
        public readonly int RowCount;
        public readonly int CurrentRow;
        public readonly float Fraction;
        public int RelativeRow => CurrentRow - StartRow;
        public RecordingGpuWindow(int startRow, int rows, int currentRow, float fraction)
        {
            StartRow = startRow; RowCount = rows; CurrentRow = currentRow; Fraction = fraction;
        }
    }

    /// <summary>GPU upload layout is independent of Unity and deterministic under seeking.</summary>
    public static class RecordingGpuWindows
    {
        public static RecordingGpuWindow AtPip(int rows, int channels, int startPip,
                                               double absolutePip, int scalarBudget)
        {
            if (rows < 1 || channels < 1 || scalarBudget < channels ||
                (double.IsNaN(absolutePip) || double.IsInfinity(absolutePip)))
                throw new ArgumentOutOfRangeException(nameof(scalarBudget));
            double absoluteRow = absolutePip - startPip;
            if (absoluteRow < 0 || absoluteRow >= rows)
                throw new ArgumentOutOfRangeException(nameof(absolutePip), "Native captured recordings never wrap.");
            int current = checked((int)Math.Floor(absoluteRow));
            int capacity = Math.Min(rows, scalarBudget / channels);
            if (capacity < 2 && rows > 1)
                throw new ArgumentOutOfRangeException(nameof(scalarBudget), "GPU budget cannot hold adjacent samples.");
            // Adjacent windows share one row, retaining exact interpolation at seams.
            int stride = Math.Max(1, capacity - 1);
            int start = (current / stride) * stride;
            int count = Math.Min(capacity, rows - start);
            return new RecordingGpuWindow(start, count, current, (float)(absoluteRow - current));
        }
    }
}
