using System;
using Unity.Profiling;

namespace KR.Core.Diagnostics
{
    // The Profiler's own counters, read in any build (draw calls and the like need a development build or the
    // Editor; in a release build those read 0 and Available says so). Dispose when done.
    public sealed class RenderCounters : IDisposable
    {
        ProfilerRecorder _mainThread, _drawCalls, _batches, _setPass, _triangles, _gcAlloc, _memory;

        public RenderCounters()
        {
            _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _memory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
        }

        public bool Available => _drawCalls.Valid && _drawCalls.LastValue > 0;
        public long DrawCalls => _drawCalls.Valid ? _drawCalls.LastValue : 0;
        public long Batches => _batches.Valid ? _batches.LastValue : 0;
        public long SetPassCalls => _setPass.Valid ? _setPass.LastValue : 0;
        public long Triangles => _triangles.Valid ? _triangles.LastValue : 0;
        public long GcBytesThisFrame => _gcAlloc.Valid ? _gcAlloc.LastValue : 0;
        public long UsedMemoryBytes => _memory.Valid ? _memory.LastValue : 0;

        // Average main-thread time over the last 15 frames, in ms (0 when the recorder is not available).
        public double MainThreadMs
        {
            get
            {
                if (!_mainThread.Valid || _mainThread.Count == 0)
                    return 0;
                long sum = 0;
                int n = _mainThread.Count;
                for (int i = 0; i < n; i++)
                    sum += _mainThread.GetSample(i).Value;
                return sum / (double)n * 1e-6;
            }
        }

        public void Dispose()
        {
            _mainThread.Dispose();
            _drawCalls.Dispose();
            _batches.Dispose();
            _setPass.Dispose();
            _triangles.Dispose();
            _gcAlloc.Dispose();
            _memory.Dispose();
        }
    }
}
