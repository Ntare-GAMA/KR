using System.Collections.Generic;
using KR.Gameplay.World;
using NUnit.Framework;
using UnityEngine;

namespace KR.Tests
{
    public class RoadGraphTests
    {
        static RoadNetworkTraffic.RoadPath Road(bool oneway, params Vector3[] pts) =>
            new RoadNetworkTraffic.RoadPath { points = pts, oneway = oneway, halfWidth = 3f, driveCost = 1f };

        // A 100 m square A(0,0) B(100,0) C(100,100) D(0,100), plus a diagonal from C to A (one-way C to A if asked).
        static RoadGraph Square(bool diagonalOneWay) => new RoadGraph(new List<RoadNetworkTraffic.RoadPath>
        {
            Road(false, new Vector3(0, 0, 0), new Vector3(100, 0, 0), new Vector3(100, 0, 100)),
            Road(false, new Vector3(100, 0, 100), new Vector3(0, 0, 100), new Vector3(0, 0, 0)),
            Road(diagonalOneWay, new Vector3(100, 0, 100), new Vector3(50, 0, 50), new Vector3(0, 0, 0)),
        });

        static float Length(List<Vector3> path)
        {
            float l = 0f;
            for (int i = 1; i < path.Count; i++)
                l += Vector3.Distance(path[i - 1], path[i]);
            return l;
        }

        [Test]
        public void WalkingTakesTheShortestWay()
        {
            var g = Square(true);
            int a = g.Nearest(new Vector3(1, 0, 1)), c = g.Nearest(new Vector3(99, 0, 99));
            var path = g.Route(a, c, false);
            Assert.IsNotNull(path);
            Assert.AreEqual(100f * Mathf.Sqrt(2f), Length(path), 0.5f);   // the diagonal, walked against the one-way
        }

        [Test]
        public void DrivingRespectsOneWayStreets()
        {
            var g = Square(true);
            int a = g.Nearest(new Vector3(1, 0, 1)), c = g.Nearest(new Vector3(99, 0, 99));
            var path = g.Route(a, c, true);
            Assert.IsNotNull(path);
            Assert.AreEqual(200f, Length(path), 0.5f);                    // round the edge, not up the one-way
        }

        [Test]
        public void TwoWayShortcutIsDriven()
        {
            var g = Square(false);
            int a = g.Nearest(new Vector3(1, 0, 1)), c = g.Nearest(new Vector3(99, 0, 99));
            Assert.AreEqual(100f * Mathf.Sqrt(2f), Length(g.Route(a, c, true)), 0.5f);
        }

        [Test]
        public void NearestIsMinusOneWhenNothingIsInRange()
        {
            Assert.AreEqual(-1, Square(false).Nearest(new Vector3(5000, 0, 5000), 100f));
        }

        [Test]
        public void RouteToItselfIsOnePoint()
        {
            var g = Square(false);
            int a = g.Nearest(Vector3.zero);
            Assert.AreEqual(1, g.Route(a, a, true).Count);
        }
    }
}
