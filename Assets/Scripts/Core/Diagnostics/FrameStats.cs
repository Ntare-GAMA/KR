using System;
using System.Collections.Generic;

namespace KR.Core.Diagnostics
{
    // Frame-time statistics with no Unity dependency, so it can be unit-tested. Frame times are in milliseconds.
    // "1% low" is the FPS of the slowest 1% of frames (the 99th-percentile frame time), the usual stutter measure.
    public sealed class FrameStats
    {
        readonly List<float> _ms;
        double _sum;
        float _worst;

        public FrameStats(int capacity = 8192) => _ms = new List<float>(capacity);

        public int Count => _ms.Count;
        public float WorstMs => _worst;
        public float AverageMs => _ms.Count == 0 ? 0f : (float)(_sum / _ms.Count);
        public float AverageFps => _sum <= 0 ? 0f : (float)(_ms.Count * 1000.0 / _sum);

        public void Add(float ms)
        {
            if (ms <= 0f || float.IsNaN(ms) || float.IsInfinity(ms))
                return;
            _ms.Add(ms);
            _sum += ms;
            if (ms > _worst)
                _worst = ms;
        }

        public void Clear()
        {
            _ms.Clear();
            _sum = 0;
            _worst = 0f;
        }

        // Frame time at percentile p (0..100), nearest-rank. Sorts a copy, so call it at the end, not per frame.
        public float PercentileMs(float p)
        {
            if (_ms.Count == 0)
                return 0f;
            var sorted = _ms.ToArray();
            Array.Sort(sorted);
            int rank = (int)Math.Ceiling(Math.Clamp(p, 0f, 100f) / 100.0 * sorted.Length) - 1;
            return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
        }

        public float LowFps(float percent = 1f)
        {
            float ms = PercentileMs(100f - percent);
            return ms <= 0f ? 0f : 1000f / ms;
        }

        // Share of frames (0..1) that took longer than the budget, e.g. 1000/72 ms on a Quest.
        public float ShareOver(float budgetMs)
        {
            if (_ms.Count == 0)
                return 0f;
            int over = 0;
            foreach (var m in _ms)
                if (m > budgetMs)
                    over++;
            return over / (float)_ms.Count;
        }
    }
}
