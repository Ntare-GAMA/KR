using System.IO;
using KR.Core;
using KR.Gameplay.Story;
using NUnit.Framework;
using UnityEngine;

namespace KR.Tests
{
    public class TrafficStopTests
    {
        static TrafficStop Stopped(out RoadRule rule)
        {
            rule = new RoadRule { id = RuleId.RefuseStop, title = "test rule" };
            var stop = new TrafficStop();
            stop.Start(rule, Vector3.zero);
            return stop;
        }

        [Test]
        public void PullingOverAndWaitingClearsTheStop()
        {
            var stop = Stopped(out _);
            Assert.AreEqual(TrafficStop.Outcome.Pending, stop.Update(1f, new Vector3(20, 0, 0), 0f));   // stopped: check begins
            for (int i = 0; i < 9; i++)
                Assert.AreEqual(TrafficStop.Outcome.Pending, stop.Update(1f, new Vector3(20, 0, 0), 0f));
            Assert.AreEqual(TrafficStop.Outcome.Cleared, stop.Update(1.1f, new Vector3(20, 0, 0), 0f));
            Assert.IsFalse(stop.Active);
        }

        [Test]
        public void DrivingAwayIsRefusingToStop()
        {
            var stop = Stopped(out _);
            Assert.AreEqual(TrafficStop.Outcome.Refused, stop.Update(0.1f, new Vector3(0, 0, 121), 40f));
        }

        [Test]
        public void MovingDuringTheCheckRestartsPartOfIt()
        {
            var stop = Stopped(out _);
            stop.Update(0.1f, Vector3.zero, 0f);                       // holding, 10 s check
            stop.Update(9.5f, Vector3.zero, 0f);                       // 0.5 s left
            stop.Update(0.1f, Vector3.zero, 15f);                      // rolled forward: at least 6 s again
            for (int i = 0; i < 5; i++)
                Assert.AreEqual(TrafficStop.Outcome.Pending, stop.Update(1f, Vector3.zero, 0f));
            Assert.AreEqual(TrafficStop.Outcome.Cleared, stop.Update(1.1f, Vector3.zero, 0f));
        }

        [Test]
        public void NotStoppingInTimeWarnsAndKeepsWaiting()
        {
            var stop = Stopped(out _);
            Assert.AreEqual(TrafficStop.Outcome.Pending, stop.Update(16f, new Vector3(50, 0, 0), 30f));
            StringAssert.Contains("did not stop", stop.Message);
        }
    }

    public class AtomicFileTests
    {
        string _dir;

        [SetUp] public void SetUp() => Directory.CreateDirectory(_dir = Path.Combine(Path.GetTempPath(), "kr_atomic_" + System.Guid.NewGuid().ToString("N")));
        [TearDown] public void TearDown() => Directory.Delete(_dir, true);

        [Test]
        public void SecondWriteKeepsTheFirstAsBackup()
        {
            string path = Path.Combine(_dir, "save.json");
            AtomicFile.Write(path, "one");
            AtomicFile.Write(path, "two");
            Assert.AreEqual("two", File.ReadAllText(path));
            Assert.AreEqual("one", File.ReadAllText(path + ".bak"));
            Assert.IsFalse(File.Exists(path + ".tmp"));
        }
    }
}
