using System.Collections.Generic;
using UnityEngine;

namespace KR.Gameplay.World
{
    // Every street in the play area with its real OSM name (KG 11 Ave, KN 5 Rd...), on the road surface. The satnav
    // routes over it (walking can use any street; driving keeps to one-way rules) and names the street you are on.
    [CreateAssetMenu(menuName = "Kigali Rush/World/Street Network")]
    public sealed class StreetNetwork : ScriptableObject
    {
        [System.Serializable]
        public sealed class Street
        {
            public string name;
            public string highway;
            public bool oneway;
            public float halfWidth;
            public Vector3[] points;
        }

        public Street[] streets = new Street[0];

        public RoadNetworkTraffic.RoadPath[] Paths()
        {
            var list = new List<RoadNetworkTraffic.RoadPath>(streets.Length);
            foreach (var s in streets)
                if (s.points != null && s.points.Length >= 2)
                    list.Add(new RoadNetworkTraffic.RoadPath { points = s.points, oneway = s.oneway, halfWidth = s.halfWidth, driveCost = DriveCost(s.highway) });
            return list.ToArray();
        }

        // How drivers usually go: along the trunk and primary roads and the main avenues, onto side streets only near
        // the start and the end. (Road class from OSM is the stand-in for local habit; adjust here if a route looks wrong.)
        public static float DriveCost(string highway)
        {
            if (string.IsNullOrEmpty(highway)) return 1.4f;
            if (highway.StartsWith("trunk") || highway.StartsWith("primary")) return 0.8f;
            if (highway.StartsWith("secondary")) return 0.85f;
            if (highway.StartsWith("tertiary")) return 0.95f;
            if (highway == "residential" || highway == "unclassified") return 1.4f;
            return 2f;   // service roads, living streets, tracks
        }

        // Name of the named street nearest to p within maxDistance, or null.
        const float Cell = 40f;
        Dictionary<long, List<int>> _cells;

        public string NameAt(Vector3 p, float maxDistance = 20f)
        {
            if (_cells == null)
                Index();
            int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell);
            string best = null;
            float bestD = maxDistance * maxDistance;
            var flat = new Vector2(p.x, p.z);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!_cells.TryGetValue(Key(cx + dx, cz + dz), out var list))
                        continue;
                    foreach (int code in list)
                    {
                        var s = streets[code >> 12];
                        int i = code & 0xFFF;
                        Vector2 a = new Vector2(s.points[i].x, s.points[i].z), b = new Vector2(s.points[i + 1].x, s.points[i + 1].z);
                        var ab = b - a;
                        float t = Mathf.Clamp01(Vector2.Dot(flat - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                        float d = (a + ab * t - flat).sqrMagnitude;
                        if (d < bestD)
                        {
                            bestD = d;
                            best = s.name;
                        }
                    }
                }
            return best;
        }

        void Index()
        {
            _cells = new Dictionary<long, List<int>>();
            for (int si = 0; si < streets.Length; si++)
            {
                var s = streets[si];
                if (string.IsNullOrEmpty(s.name) || s.points == null)
                    continue;
                for (int i = 0; i + 1 < s.points.Length && i < 0xFFF; i++)
                {
                    var m = (s.points[i] + s.points[i + 1]) * 0.5f;
                    long k = Key(Mathf.FloorToInt(m.x / Cell), Mathf.FloorToInt(m.z / Cell));
                    if (!_cells.TryGetValue(k, out var list))
                        _cells[k] = list = new List<int>();
                    list.Add((si << 12) | i);
                }
            }
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}
