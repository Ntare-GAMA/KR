using System.Collections.Generic;
using UnityEngine;

namespace KR.Gameplay.World
{
    // The drivable road network as a graph, built once from the traffic paths (the same lanes the city's cars use).
    // Path points become nodes, consecutive points become edges (one way where the road is one-way), and path
    // ends are joined to any point within a few metres, which links side roads into junctions.
    // A* finds the shortest drive (respecting one-way streets) or walk (ignoring them).
    public sealed class RoadGraph
    {
        readonly List<Vector3> _pos = new List<Vector3>();
        readonly List<List<(int to, float cost, bool forwardOnly)>> _edges = new List<List<(int, float, bool)>>();
        readonly Dictionary<long, int> _byKey = new Dictionary<long, int>();
        readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        const float Cell = 20f;
        const float JoinDistance = 6f;

        public int Count => _pos.Count;
        public Vector3 Position(int node) => _pos[node];

        // Direction of the road at a node (towards its first neighbour), flat.
        public Vector3 Direction(int node)
        {
            foreach (var (to, _, _) in _edges[node])
            {
                var d = _pos[to] - _pos[node];
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f)
                    return d.normalized;
            }
            return Vector3.forward;
        }

        public RoadGraph(IReadOnlyList<RoadNetworkTraffic.RoadPath> paths)
        {
            if (paths == null)
                return;
            var ends = new List<int>();
            foreach (var path in paths)
            {
                if (path.points == null || path.points.Length < 2)
                    continue;
                int prev = -1;
                for (int i = 0; i < path.points.Length; i++)
                {
                    int id = Node(path.points[i]);
                    if (prev >= 0 && prev != id)
                    {
                        float d = Vector3.Distance(_pos[prev], _pos[id]);
                        _edges[prev].Add((id, d, false));
                        // A one-way road can be walked against the traffic but not driven.
                        _edges[id].Add((prev, d, path.oneway));
                    }
                    prev = id;
                }
                ends.Add(Node(path.points[0]));
                ends.Add(Node(path.points[path.points.Length - 1]));
            }
            foreach (int e in ends)
                foreach (int n in Near(_pos[e], JoinDistance))
                    if (n != e)
                    {
                        float d = Vector3.Distance(_pos[e], _pos[n]);
                        _edges[e].Add((n, d, false));
                        _edges[n].Add((e, d, false));
                    }
        }

        static long Key(Vector3 p) => ((long)Mathf.RoundToInt(p.x) << 32) ^ (uint)Mathf.RoundToInt(p.z);
        static long CellKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        int Node(Vector3 p)
        {
            long k = Key(p);
            if (_byKey.TryGetValue(k, out int id))
                return id;
            id = _pos.Count;
            _byKey[k] = id;
            _pos.Add(p);
            _edges.Add(new List<(int, float, bool)>());
            long c = CellKey(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
            if (!_cells.TryGetValue(c, out var list))
                _cells[c] = list = new List<int>();
            list.Add(id);
            return id;
        }

        IEnumerable<int> Near(Vector3 p, float radius)
        {
            int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell);
            float r2 = radius * radius;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    if (_cells.TryGetValue(CellKey(cx + dx, cz + dz), out var list))
                        foreach (int n in list)
                        {
                            float x = _pos[n].x - p.x, z = _pos[n].z - p.z;
                            if (x * x + z * z <= r2)
                                yield return n;
                        }
        }

        // Nearest node, searching outwards ring by ring; -1 when nothing is within maxDistance.
        public int Nearest(Vector3 p, float maxDistance = 400f)
        {
            int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell);
            int best = -1;
            float bestD = maxDistance * maxDistance;
            int rings = Mathf.CeilToInt(maxDistance / Cell);
            for (int r = 0; r <= rings; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r)
                            continue;
                        if (!_cells.TryGetValue(CellKey(cx + dx, cz + dz), out var list))
                            continue;
                        foreach (int n in list)
                        {
                            float x = _pos[n].x - p.x, z = _pos[n].z - p.z, d = x * x + z * z;
                            if (d < bestD)
                            {
                                bestD = d;
                                best = n;
                            }
                        }
                    }
                // Anything in a further ring is at least r * Cell away.
                if (best >= 0 && r * Cell > Mathf.Sqrt(bestD))
                    break;
            }
            return best;
        }

        // A* over the network. Driving respects one-way streets.
        public List<Vector3> Route(int from, int to, bool driving)
        {
            if (from < 0 || to < 0)
                return null;
            var g = new Dictionary<int, float> { [from] = 0f };
            var back = new Dictionary<int, int>();
            var open = new List<(float f, int n)> { (0f, from) };
            var closed = new HashSet<int>();
            Vector3 goal = _pos[to];
            while (open.Count > 0)
            {
                var (_, n) = Pop(open);
                if (n == to)
                    break;
                if (!closed.Add(n))
                    continue;
                float gn = g[n];
                foreach (var (m, cost, forwardOnly) in _edges[n])
                {
                    if (driving && forwardOnly)
                        continue;
                    float gm = gn + cost;
                    if (g.TryGetValue(m, out float old) && old <= gm)
                        continue;
                    g[m] = gm;
                    back[m] = n;
                    Push(open, (gm + Vector3.Distance(_pos[m], goal), m));
                }
            }
            if (from != to && !back.ContainsKey(to))
                return null;
            var path = new List<Vector3>();
            for (int n = to; ; n = back[n])
            {
                path.Add(_pos[n]);
                if (n == from)
                    break;
            }
            path.Reverse();
            return path;
        }

        static void Push(List<(float f, int n)> h, (float f, int n) item)
        {
            h.Add(item);
            int i = h.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (h[p].f <= h[i].f)
                    break;
                (h[p], h[i]) = (h[i], h[p]);
                i = p;
            }
        }

        static (float f, int n) Pop(List<(float f, int n)> h)
        {
            var top = h[0];
            h[0] = h[h.Count - 1];
            h.RemoveAt(h.Count - 1);
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < h.Count && h[l].f < h[m].f) m = l;
                if (r < h.Count && h[r].f < h[m].f) m = r;
                if (m == i)
                    break;
                (h[m], h[i]) = (h[i], h[m]);
                i = m;
            }
            return top;
        }
    }
}
