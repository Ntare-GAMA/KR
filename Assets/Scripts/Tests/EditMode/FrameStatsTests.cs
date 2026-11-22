using KR.Core.Diagnostics;
using NUnit.Framework;

namespace KR.Tests
{
    public class FrameStatsTests
    {
        [Test]
        public void AverageFpsIsFramesOverTotalTime()
        {
            var s = new FrameStats();
            for (int i = 0; i < 72; i++)
                s.Add(1000f / 72f);
            Assert.AreEqual(72f, s.AverageFps, 0.01f);
            Assert.AreEqual(1000f / 72f, s.AverageMs, 0.001f);
        }

        [Test]
        public void OnePercentLowIsTheSlowestFrames()
        {
            var s = new FrameStats();
            for (int i = 0; i < 99; i++)
                s.Add(10f);         // 100 FPS
            s.Add(50f);             // one 20 FPS hitch
            Assert.AreEqual(50f, s.PercentileMs(99.5f), 0.001f);
            Assert.AreEqual(100f, s.LowFps(1.5f), 0.01f);
            Assert.AreEqual(50f, s.WorstMs, 0.001f);
        }

        [Test]
        public void ShareOverBudgetCountsSlowFrames()
        {
            var s = new FrameStats();
            s.Add(10f);
            s.Add(10f);
            s.Add(20f);
            s.Add(30f);
            Assert.AreEqual(0.5f, s.ShareOver(13.9f), 0.001f);
        }

        [Test]
        public void InvalidFrameTimesAreIgnored()
        {
            var s = new FrameStats();
            s.Add(0f);
            s.Add(-3f);
            s.Add(float.NaN);
            s.Add(float.PositiveInfinity);
            Assert.AreEqual(0, s.Count);
            Assert.AreEqual(0f, s.AverageFps);
            Assert.AreEqual(0f, s.LowFps());
        }

        [Test]
        public void ClearStartsAgain()
        {
            var s = new FrameStats();
            s.Add(16f);
            s.Clear();
            Assert.AreEqual(0, s.Count);
            Assert.AreEqual(0f, s.WorstMs);
        }
    }
}
