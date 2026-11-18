using System.Collections.Generic;
using KR.Editor.GIS;
using UnityEngine;

namespace KR.Editor.Kigali
{
    // Where the carriageways are, so street furniture never stands in a road. A lamp, bin or sign placed beside one
    // street can land on another street that meets or bends near it (junctions, curves); Push moves it out along
    // the given direction until it is clear of every carriageway.
    internal sealed class RoadClearance
    {
        const float Cell = 25f;
        readonly List<(Vector2 a, Vector2 b, float half)> _segments = new List<(Vector2, Vector2, float)>();
        readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();

        public RoadClearance(DistrictImporter.Road[] roads)
        {
            foreach (var r in roads)
            {
                if (r.pts == null || r.pts.Length < 6 || !DistrictImporter.RoadWidths.TryGetValue(r.highway, out float width))
                    continue;
                // Carriageway plus the kerb.
                float half = width * 0.5f + (PcRoadBuilder.WithFootpaths.Contains(r.highway) ? 0.3f : 0.1f);
                for (int i = 0; i + 5 < r.pts.Length; i += 3)
                {
                    var a = new Vector2(r.pts[i], r.pts[i + 2]);
                    var b = new Vector2(r.pts[i + 3], r.pts[i + 5]);
                    int index = _segments.Count;
                    _segments.Add((a, b, half));
                    var min = Vector2.Min(a, b) - Vector2.one * half;
                    var max = Vector2.Max(a, b) + Vector2.one * half;
                    for (int x = Mathf.FloorToInt(min.x / Cell); x <= Mathf.FloorToInt(max.x / Cell); x++)
                        for (int z = Mathf.FloorToInt(min.y / Cell); z <= Mathf.FloorToInt(max.y / Cell); z++)
                        {
                            long k = Key(x, z);
                            if (!_cells.TryGetValue(k, out var list))
                                _cells[k] = list = new List<int>();
                            list.Add(index);
                        }
                }
            }
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        // True when p (with this radius around it) is off every carriageway.
        public bool Clear(Vector3 p, float radius)
        {
            var q = new Vector2(p.x, p.z);
            if (!_cells.TryGetValue(Key(Mathf.FloorToInt(q.x / Cell), Mathf.FloorToInt(q.y / Cell)), out var list))
                return true;
            foreach (int i in list)
            {
                var (a, b, half) = _segments[i];
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                if ((a + ab * t - q).sqrMagnitude < (half + radius) * (half + radius))
                    return false;
            }
            return true;
        }

        // p moved along `away` (flat) in half-metre steps until clear, up to `limit` metres; false if it never is.
        public bool Push(ref Vector3 p, Vector3 away, float radius, float limit = 6f)
        {
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f)
                return Clear(p, radius);
            away.Normalize();
            for (float d = 0f; d <= limit; d += 0.5f)
            {
                var q = p + away * d;
                if (Clear(q, radius))
                {
                    p = q;
                    return true;
                }
            }
            return false;
        }
    }
}
