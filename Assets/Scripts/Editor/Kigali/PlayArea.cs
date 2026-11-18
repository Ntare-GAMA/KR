using System.Collections.Generic;
using System.Linq;
using KR.Editor.GIS;
using UnityEngine;

namespace KR.Editor.Kigali
{
    // The part of the map that is actually built: the two sector centres (Kimironko, Kibagabaga), the ALU campus,
    // and a band along the real road route that joins each pair (shortest drive on the OSM network, preferring
    // main roads). Buildings, roads, places, trees and traffic outside it are not built, so the headset only ever
    // draws the playable neighbourhood. Radii are design choices, not official sector boundaries
    // (TODO: verify against the official sector boundary if the owner supplies it).
    internal static class PlayArea
    {
        // Hub radius by hub name from terrain.json; any other hub gets DefaultRadius.
        static readonly Dictionary<string, float> Radii = new Dictionary<string, float>
        {
            ["Kimironko"] = 850f,
            ["Kibagabaga"] = 750f,
            ["ALU campus"] = 400f,
        };
        const float DefaultRadius = 500f;
        const float RouteHalfWidth = 140f;   // buildings either side of the connecting roads
        const float Cell = 10f;

        static bool[] _mask;
        static int _res;
        public static bool Ready => _mask != null;
        public static float AreaKm2 { get; private set; }

        public static void Init(Vector3[] hubs, string[] names, DistrictImporter.Road[] roads, float size)
        {
            _res = Mathf.CeilToInt(size / Cell);
            _mask = new bool[_res * _res];
            for (int i = 0; i < hubs.Length; i++)
                Stamp(hubs[i], Radii.TryGetValue(names[i], out float r) ? r : DefaultRadius);

            var graph = new Graph(roads);
            int routes = 0;
            for (int a = 0; a < hubs.Length; a++)
                for (int b = a + 1; b < hubs.Length; b++)
                {
                    var route = graph.Route(hubs[a], hubs[b]);
                    if (route == null)
                    {
                        Debug.LogWarning($"[PlayArea] No road route between {names[a]} and {names[b]}.");
                        continue;
                    }
                    routes++;
                    for (int i = 1; i < route.Count; i++)
                    {
                        float len = Vector3.Distance(route[i - 1], route[i]);
                        for (float t = 0f; t <= len; t += Cell)
                            Stamp(Vector3.Lerp(route[i - 1], route[i], len > 0f ? t / len : 0f), RouteHalfWidth);
                    }
                }
            AreaKm2 = _mask.Count(m => m) * Cell * Cell / 1e6f;
            Debug.Log($"[PlayArea] {hubs.Length} hubs, {routes} connecting routes, {AreaKm2:0.0} km² built (map is {size * size / 1e6f:0} km²).");
        }

        static void Stamp(Vector3 c, float r)
        {
            int x0 = Mathf.Max(0, (int)((c.x - r) / Cell)), x1 = Mathf.Min(_res - 1, (int)((c.x + r) / Cell));
            int z0 = Mathf.Max(0, (int)((c.z - r) / Cell)), z1 = Mathf.Min(_res - 1, (int)((c.z + r) / Cell));
            float r2 = r * r;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x + 0.5f) * Cell - c.x, dz = (z + 0.5f) * Cell - c.z;
                    if (dx * dx + dz * dz <= r2)
                        _mask[z * _res + x] = true;
                }
        }

        // World x/z rectangle around everything inside the area.
        public static bool Bounds2D(out Vector2 min, out Vector2 max)
        {
            min = max = Vector2.zero;
            if (_mask == null)
                return false;
            int x0 = int.MaxValue, z0 = int.MaxValue, x1 = -1, z1 = -1;
            for (int z = 0; z < _res; z++)
                for (int x = 0; x < _res; x++)
                    if (_mask[z * _res + x])
                    {
                        if (x < x0) x0 = x;
                        if (x > x1) x1 = x;
                        if (z < z0) z0 = z;
                        if (z > z1) z1 = z;
                    }
            if (x1 < 0)
                return false;
            min = new Vector2(x0 * Cell, z0 * Cell);
            max = new Vector2((x1 + 1) * Cell, (z1 + 1) * Cell);
            return true;
        }

        public static bool Contains(Vector3 p)
        {
            if (_mask == null)
                return true;
            int x = (int)(p.x / Cell), z = (int)(p.z / Cell);
            return x >= 0 && z >= 0 && x < _res && z < _res && _mask[z * _res + x];
        }

        // Inside, or within margin metres of the edge (checked on a coarse ring).
        public static bool Contains(Vector3 p, float margin)
        {
            if (Contains(p))
                return true;
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                if (Contains(p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * margin))
                    return true;
            }
            return false;
        }

        // ---------- data filters ----------

        // Runs of each road that lie inside the area plus margin (so roads leave the area visibly, into the haze).
        public static DistrictImporter.Road[] Clip(DistrictImporter.Road[] roads, float margin)
        {
            var result = new List<DistrictImporter.Road>();
            foreach (var road in roads)
            {
                var pts = road.pts;
                if (pts == null || pts.Length < 6)
                    continue;
                var run = new List<float>();
                void Flush()
                {
                    if (run.Count >= 6)
                        result.Add(new DistrictImporter.Road { highway = road.highway, name = road.name, oneway = road.oneway, pts = run.ToArray() });
                    run = new List<float>();
                }
                for (int i = 0; i + 2 < pts.Length; i += 3)
                {
                    if (Contains(new Vector3(pts[i], 0f, pts[i + 2]), margin))
                    {
                        run.Add(pts[i]);
                        run.Add(pts[i + 1]);
                        run.Add(pts[i + 2]);
                    }
                    else
                        Flush();
                }
                Flush();
            }
            return result.ToArray();
        }

        public static DistrictImporter.Building[] Filter(DistrictImporter.Building[] buildings) =>
            buildings.Where(b => b.pts != null && b.pts.Length >= 6 && Contains(PcCityLife.Centroid(b.pts))).ToArray();

        public static PcCityLife.Place[] Filter(PcCityLife.Place[] places) =>
            places.Where(p => p.pts == null || p.pts.Length < 2 || Contains(PcCityLife.Centroid(p.pts))).ToArray();

        // ---------- road graph (shared OSM nodes join roads) ----------

        sealed class Graph
        {
            readonly Dictionary<long, int> _ids = new Dictionary<long, int>();
            readonly List<Vector3> _pos = new List<Vector3>();
            readonly List<List<(int to, float cost)>> _edges = new List<List<(int, float)>>();

            static readonly HashSet<string> Main = new HashSet<string>
            {
                "trunk", "trunk_link", "primary", "primary_link", "secondary", "secondary_link", "tertiary", "tertiary_link",
            };

            public Graph(DistrictImporter.Road[] roads)
            {
                foreach (var road in roads)
                {
                    if (road.pts == null || road.pts.Length < 6 || road.highway == "footway" || road.highway == "path" ||
                        road.highway == "steps" || road.highway == "cycleway")
                        continue;
                    float weight = Main.Contains(road.highway) ? 0.6f : 1f;   // drivers prefer main roads
                    int prev = -1;
                    for (int i = 0; i + 2 < road.pts.Length; i += 3)
                    {
                        int id = Node(new Vector3(road.pts[i], 0f, road.pts[i + 2]));
                        if (prev >= 0 && prev != id)
                        {
                            float c = Vector3.Distance(_pos[prev], _pos[id]) * weight;
                            _edges[prev].Add((id, c));
                            _edges[id].Add((prev, c));
                        }
                        prev = id;
                    }
                }
            }

            int Node(Vector3 p)
            {
                long key = ((long)Mathf.RoundToInt(p.x * 10f) << 32) ^ (uint)Mathf.RoundToInt(p.z * 10f);
                if (_ids.TryGetValue(key, out int id))
                    return id;
                id = _pos.Count;
                _ids[key] = id;
                _pos.Add(p);
                _edges.Add(new List<(int, float)>());
                return id;
            }

            int Nearest(Vector3 p)
            {
                int best = -1;
                float bestD = float.MaxValue;
                for (int i = 0; i < _pos.Count; i++)
                {
                    // Prefer connected nodes so the search does not start on an isolated stub.
                    if (_edges[i].Count == 0)
                        continue;
                    float dx = _pos[i].x - p.x, dz = _pos[i].z - p.z, d = dx * dx + dz * dz;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }
                return best;
            }

            // Dijkstra with a binary heap.
            public List<Vector3> Route(Vector3 from, Vector3 to)
            {
                int s = Nearest(from), t = Nearest(to);
                if (s < 0 || t < 0)
                    return null;
                var dist = new float[_pos.Count];
                var back = new int[_pos.Count];
                for (int i = 0; i < dist.Length; i++)
                {
                    dist[i] = float.MaxValue;
                    back[i] = -1;
                }
                dist[s] = 0f;
                var heap = new List<(float d, int n)> { (0f, s) };
                while (heap.Count > 0)
                {
                    var (d, n) = Pop(heap);
                    if (d > dist[n])
                        continue;
                    if (n == t)
                        break;
                    foreach (var (m, c) in _edges[n])
                    {
                        float nd = d + c;
                        if (nd < dist[m])
                        {
                            dist[m] = nd;
                            back[m] = n;
                            Push(heap, (nd, m));
                        }
                    }
                }
                if (back[t] < 0 && s != t)
                    return null;
                var path = new List<Vector3>();
                for (int n = t; n >= 0; n = back[n])
                    path.Add(_pos[n]);
                path.Reverse();
                return path;
            }

            static void Push(List<(float d, int n)> h, (float d, int n) item)
            {
                h.Add(item);
                int i = h.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (h[p].d <= h[i].d)
                        break;
                    (h[p], h[i]) = (h[i], h[p]);
                    i = p;
                }
            }

            static (float d, int n) Pop(List<(float d, int n)> h)
            {
                var top = h[0];
                h[0] = h[h.Count - 1];
                h.RemoveAt(h.Count - 1);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < h.Count && h[l].d < h[m].d) m = l;
                    if (r < h.Count && h[r].d < h[m].d) m = r;
                    if (m == i)
                        break;
                    (h[m], h[i]) = (h[i], h[m]);
                    i = m;
                }
                return top;
            }
        }
    }
}
