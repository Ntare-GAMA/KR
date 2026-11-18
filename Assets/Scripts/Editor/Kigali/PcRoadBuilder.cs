using System.Collections.Generic;
using System.Linq;
using KR.Editor.GIS;
using KR.Editor.Util;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KR.Editor.Kigali
{
    // Builds the PC road surface from OSM centrelines with photo-scanned materials: asphalt, cobbled
    // neighbourhood streets, concrete-paver footpaths, striped kerbs, grass medians beside one-way carriageways.
    // Markings are real geometry: solid white edge lines, dashed lane lines, dashed yellow centre on two-way
    // roads, stop lines and zebra crossings at signalised junctions.
    //
    // Clean surfaces:
    //   - OSM splits one street into many ways; ways that simply continue each other are merged first, so a
    //     street is one ribbon with continuous markings and no wedges at the joins.
    //   - The terrain is carved to a smoothed road profile under every road, so the road is smooth and the
    //     terrain (or its distant LOD) cannot poke through.
    //   - Every junction (three or more arms) gets one asphalt plate; carriageways, markings, kerbs and
    //     footpaths stop exactly at its edge instead of overlapping each other.
    //   - On bends tighter than the road is wide, the inner edge is held instead of folding back.
    // Meshes are grouped per material and per 1 km chunk.
    // TODO: verify marking colours and widths against Rwanda Transport Development Agency standards.
    internal static class PcRoadBuilder
    {
        public const float DashOn = 3f, DashPeriod = 12f, StopLineAt = 10.4f;
        const float Resample = 6f, BuildSpacing = 4f;
        const float ChunkSize = 1000f;
        const float Paint = 0.012f;
        const float PlateLift = 0.334f;       // just above every carriageway
        const float MaxCut = 30f;             // longest arm cut back from a junction centre

        public static readonly HashSet<string> Major = new HashSet<string>
            { "trunk", "trunk_link", "primary", "primary_link", "secondary", "secondary_link" };
        public static readonly HashSet<string> WithFootpaths = new HashSet<string>(Major) { "tertiary", "tertiary_link" };
        static readonly HashSet<string> Cobbled = new HashSet<string> { "residential", "living_street" };

        public sealed class Junction
        {
            public Vector3 Position;
            public float Width;
            public readonly List<Vector3> Directions = new List<Vector3>();
            public readonly List<bool> DirectionIsMajor = new List<bool>();
            public bool Signalised;
            public int SignalIndex = -1;
        }

        public sealed class Surfaces
        {
            public Material Asphalt, Cobble, Pavers, Grass, Kerb, White, Yellow;
        }

        // Millimetre steps between classes keep overlapping ribbons from flickering, without visible ledges.
        public static float SurfaceLift(string highway) =>
            highway.StartsWith("trunk") ? 0.328f : highway.StartsWith("primary") ? 0.326f :
            highway.StartsWith("secondary") ? 0.324f : highway.StartsWith("tertiary") ? 0.322f : 0.320f;

        public static int Lanes(float width, bool oneway) =>
            oneway ? Mathf.Max(1, Mathf.RoundToInt((width - 1f) / 3.4f)) : Mathf.Max(1, Mathf.RoundToInt((width * 0.5f - 0.5f) / 3.4f));

        sealed class Strip
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> T = new List<int>();
        }

        sealed class Way
        {
            public string Highway;
            public bool Oneway;
            public float Width;
            public List<Vector3> Pts;
            public bool Alive = true;
        }

        sealed class Plate
        {
            public Vector2[] Hull;      // convex, counter-clockwise in (x, z)
            public Vector2 Centre, Min, Max;
        }

        // A point on a centreline with its travel direction and right-hand side.
        struct Sample
        {
            public Vector3 P, Dir, Side;
            public float Dist;
        }

        public static void Build(DistrictImporter.Road[] roads, Terrain terrain, Transform root, string outDir,
                                 IEnumerable<Junction> junctions, Surfaces m, string district)
        {
            var ways = MergeWays(roads);
            CarveTerrain(ways, terrain);
            var plates = BuildPlates(ways);
            var index = new PlateIndex(plates);

            var strips = new Dictionary<(Material, int), Strip>();
            Strip S(Material mat, int chunk)
            {
                if (!strips.TryGetValue((mat, chunk), out var s))
                    strips[(mat, chunk)] = s = new Strip();
                return s;
            }

            foreach (var way in ways)
            {
                var line = Samples(Resample3(way.Pts, BuildSpacing));
                if (line.Count < 2)
                    continue;
                bool oneway = way.Oneway;
                float width = way.Width, half = width * 0.5f, lift = SurfaceLift(way.Highway);
                bool cobbled = Cobbled.Contains(way.Highway);

                void Add(Material mat, List<Sample> l, float left, float right, float y, float vPerMetre = 1f) =>
                    AddStrip(S, mat, l, left, right, y, terrain, index, vPerMetre);

                Add(cobbled ? m.Cobble : m.Asphalt, line, -half, half, lift);

                if (!WithFootpaths.Contains(way.Highway))
                    continue;
                float ml = lift + Paint;
                Add(m.White, line, half - 0.45f, half - 0.3f, ml);
                Add(m.White, line, -half + 0.3f, -half + 0.45f, ml);
                if (oneway)
                {
                    int lanes = Lanes(width, true);
                    float usable = width - 0.9f;
                    for (int k = 1; k < lanes; k++)
                    {
                        float x = -half + 0.45f + usable * k / lanes;
                        foreach (var dash in Dashes(line))
                            Add(m.White, dash, x - 0.075f, x + 0.075f, ml);
                    }
                }
                else
                {
                    foreach (var dash in Dashes(line))
                        Add(m.Yellow, dash, -0.075f, 0.075f, ml);
                    int lanes = Lanes(width, false);
                    for (int k = 1; k < lanes; k++)
                        foreach (float side in new[] { -1f, 1f })
                        {
                            float x = side * (half - 0.45f) * k / lanes;
                            foreach (var dash in Dashes(line))
                                Add(m.White, dash, x - 0.075f, x + 0.075f, ml);
                        }
                }

                // Driving on the right: on a one-way carriageway the median is on the left.
                Add(m.Kerb, line, half, half + 0.3f, lift + 0.12f, 0.5f);
                Add(m.Pavers, line, half + 0.3f, half + 2.8f, lift + 0.10f);
                Add(m.Kerb, line, -half - 0.3f, -half, lift + 0.12f, 0.5f);
                if (oneway)
                    Add(m.Grass, line, -half - 2.3f, -half - 0.3f, lift + 0.08f);
                else
                    Add(m.Pavers, line, -half - 2.8f, -half - 0.3f, lift + 0.10f);
            }

            foreach (var plate in plates)
                AddPlate(S(m.Asphalt, ChunkOf(new Vector3(plate.Centre.x, 0f, plate.Centre.y), terrain)), plate, terrain);

            // Crossings sit on the junction plates (paint above the plate), so they are not clipped.
            foreach (var j in junctions)
            {
                if (!j.Signalised)
                    continue;
                int chunk = ChunkOf(j.Position, terrain);
                float half = j.Width * 0.5f;
                float y = PlateLift + Paint;
                for (int i = 0; i < j.Directions.Count && i < 4; i++)
                {
                    var d = j.Directions[i];
                    // Zebra: 0.5 m bars along the road, 0.5 m gaps, 3 m deep.
                    for (float x = -half + 0.5f; x + 0.5f <= half - 0.4f; x += 1f)
                        AddStrip(S, m.White, Samples(new List<Vector3> { j.Position + d * 5.5f, j.Position + d * 8.5f }), x, x + 0.5f, y, terrain, null, 1f, chunk);
                    // Stop line across the approaching lanes (driver's right = this line's negative side).
                    AddStrip(S, m.White, Samples(new List<Vector3> { j.Position + d * StopLineAt, j.Position + d * (StopLineAt + 0.4f) }),
                             -half + 0.3f, 0f, y, terrain, null, 1f, chunk);
                }
            }

            WriteMeshes(strips, root, outDir, district);
            Debug.Log($"[Roads] {roads.Length} OSM ways merged into {ways.Count} streets; {plates.Count} junction plates.");
        }

        static int ChunkOf(Vector3 p, Terrain terrain)
        {
            int n = Mathf.CeilToInt(terrain.terrainData.size.x / ChunkSize);
            int cx = Mathf.Clamp((int)(p.x / ChunkSize), 0, n - 1), cz = Mathf.Clamp((int)(p.z / ChunkSize), 0, n - 1);
            return cz * n + cx;
        }

        static long Key(Vector3 p) => ((long)Mathf.RoundToInt(p.x * 10f) << 32) ^ (uint)Mathf.RoundToInt(p.z * 10f);

        // ---------- Centrelines ----------

        public static List<Vector3> ResampleLine(float[] pts, float spacing = Resample)
        {
            var result = new List<Vector3>();
            for (int i = 0; i + 5 < pts.Length; i += 3)
            {
                var a = new Vector3(pts[i], 0f, pts[i + 2]);
                var b = new Vector3(pts[i + 3], 0f, pts[i + 5]);
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / spacing));
                for (int s = 0; s < steps; s++)
                    result.Add(Vector3.Lerp(a, b, (float)s / steps));
            }
            if (pts.Length >= 3)
                result.Add(new Vector3(pts[pts.Length - 3], 0f, pts[pts.Length - 1]));
            return result;
        }

        static List<Vector3> Resample3(List<Vector3> pts, float spacing)
        {
            var result = new List<Vector3>();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(pts[i], pts[i + 1]) / spacing));
                for (int s = 0; s < steps; s++)
                    result.Add(Vector3.Lerp(pts[i], pts[i + 1], (float)s / steps));
            }
            if (pts.Count > 0)
                result.Add(pts[pts.Count - 1]);
            return result;
        }

        static List<Sample> Samples(List<Vector3> line)
        {
            var result = new List<Sample>(line.Count);
            float dist = 0f;
            for (int i = 0; i < line.Count; i++)
            {
                if (i > 0)
                    dist += Vector3.Distance(line[i - 1], line[i]);
                Vector3 dir = line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)];
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-6f)
                    dir = i > 0 ? result[i - 1].Dir : Vector3.forward;
                dir.Normalize();
                result.Add(new Sample { P = line[i], Dir = dir, Side = new Vector3(dir.z, 0f, -dir.x), Dist = dist });
            }
            return result;
        }

        static Sample Lerp(Sample a, Sample b, float t)
        {
            var dir = Vector3.Lerp(a.Dir, b.Dir, t);
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : a.Dir;
            return new Sample { P = Vector3.Lerp(a.P, b.P, t), Dir = dir, Side = new Vector3(dir.z, 0f, -dir.x), Dist = Mathf.Lerp(a.Dist, b.Dist, t) };
        }

        // Ways that only continue each other (a node shared by exactly two way ends, same class and direction rules)
        // become one street.
        static List<Way> MergeWays(DistrictImporter.Road[] roads)
        {
            var ways = new List<Way>();
            foreach (var road in roads)
            {
                if (road.pts == null || road.pts.Length < 6 || !DistrictImporter.RoadWidths.TryGetValue(road.highway, out float width))
                    continue;
                var pts = new List<Vector3>();
                for (int i = 0; i + 2 < road.pts.Length; i += 3)
                {
                    var p = new Vector3(road.pts[i], 0f, road.pts[i + 2]);
                    if (pts.Count == 0 || (pts[pts.Count - 1] - p).sqrMagnitude > 0.01f)
                        pts.Add(p);
                }
                if (pts.Count >= 2)
                    ways.Add(new Way { Highway = road.highway, Oneway = road.oneway == "yes", Width = width, Pts = pts });
            }

            var degree = Degrees(ways);
            var ends = new Dictionary<long, List<Way>>();
            void AddEnd(long k, Way w)
            {
                if (!ends.TryGetValue(k, out var list))
                    ends[k] = list = new List<Way>();
                list.Add(w);
            }
            foreach (var w in ways)
            {
                AddEnd(Key(w.Pts[0]), w);
                AddEnd(Key(w.Pts[w.Pts.Count - 1]), w);
            }

            foreach (var node in ends.Keys.ToList())
            {
                var list = ends[node];
                if (list.Count != 2 || degree[node] != 2)
                    continue;
                Way a = list[0], b = list[1];
                if (a == b || !a.Alive || !b.Alive || a.Highway != b.Highway || a.Oneway != b.Oneway)
                    continue;
                bool aEnds = Key(a.Pts[a.Pts.Count - 1]) == node, bStarts = Key(b.Pts[0]) == node;
                if (a.Oneway)
                {
                    if (!(aEnds && bStarts))
                    {
                        // Maybe the other way round: b ends here and a starts here.
                        if (Key(b.Pts[b.Pts.Count - 1]) == node && Key(a.Pts[0]) == node)
                            (a, b) = (b, a);
                        else
                            continue;   // two one-way pieces pointing against each other: not one street
                    }
                }
                else
                {
                    if (!aEnds)
                        a.Pts.Reverse();
                    if (!bStarts)
                        b.Pts.Reverse();
                }
                // Move b's far end over to a, then append.
                long aStart = Key(a.Pts[0]), bEnd = Key(b.Pts[b.Pts.Count - 1]);
                ends[node].Clear();
                ends[bEnd].Remove(b);
                ends[bEnd].Add(a);
                if (!ends[aStart].Contains(a))
                    ends[aStart].Add(a);
                a.Pts.AddRange(b.Pts.Skip(1));
                b.Alive = false;
            }
            return ways.Where(w => w.Alive).ToList();
        }

        // Number of segments meeting at each node.
        static Dictionary<long, int> Degrees(List<Way> ways)
        {
            var degree = new Dictionary<long, int>();
            foreach (var w in ways)
                for (int i = 0; i < w.Pts.Count; i++)
                {
                    long k = Key(w.Pts[i]);
                    degree.TryGetValue(k, out int d);
                    degree[k] = d + (i > 0 ? 1 : 0) + (i < w.Pts.Count - 1 ? 1 : 0);
                }
            return degree;
        }

        static float Reach(Way w) => w.Width * 0.5f + (WithFootpaths.Contains(w.Highway) ? 3.1f : 0.6f);

        // ---------- Terrain ----------

        // Heightmap cells under (and just beside) a road take the road's smoothed profile; a blend band of one
        // heightmap cell joins it to the natural ground. Everything placed afterwards samples the carved terrain,
        // so the road, the junction plates and the props all agree.
        static void CarveTerrain(List<Way> ways, Terrain terrain)
        {
            var data = terrain.terrainData;
            int res = data.heightmapResolution;
            var size = data.size;
            float cellX = size.x / (res - 1), cellZ = size.z / (res - 1), blend = Mathf.Max(cellX, cellZ) * 1.1f;
            var heights = data.GetHeights(0, 0, res, res);
            var score = new float[res * res];
            var target = new float[res * res];
            for (int i = 0; i < score.Length; i++)
                score[i] = float.MaxValue;

            foreach (var way in ways)
            {
                var line = Resample3(way.Pts, BuildSpacing);
                var raw = new float[line.Count];
                for (int i = 0; i < line.Count; i++)
                    raw[i] = data.GetInterpolatedHeight(line[i].x / size.x, line[i].z / size.z);
                // Moving average over about 30 m: removes bumps, keeps the climb.
                var profile = new float[line.Count];
                const int w = 4;
                for (int i = 0; i < line.Count; i++)
                {
                    float sum = 0f;
                    int n = 0;
                    for (int k = Mathf.Max(0, i - w); k <= Mathf.Min(line.Count - 1, i + w); k++, n++)
                        sum += raw[k];
                    profile[i] = sum / n;
                }

                float reach = Reach(way), outer = reach + blend;
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    Vector3 a = line[i], b = line[i + 1];
                    var ab = new Vector2(b.x - a.x, b.z - a.z);
                    float len2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - outer) / cellX));
                    int x1 = Mathf.Min(res - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + outer) / cellX));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, b.z) - outer) / cellZ));
                    int z1 = Mathf.Min(res - 1, Mathf.CeilToInt((Mathf.Max(a.z, b.z) + outer) / cellZ));
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            var p = new Vector2(x * cellX - a.x, z * cellZ - a.z);
                            float t = Mathf.Clamp01(Vector2.Dot(p, ab) / len2);
                            float d = (p - ab * t).magnitude;
                            float s = d - reach;
                            int k = z * res + x;
                            if (s > blend || s >= score[k])
                                continue;
                            score[k] = s;
                            target[k] = Mathf.Lerp(profile[i], profile[i + 1], t);
                        }
                }
            }

            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int k = z * res + x;
                    if (score[k] == float.MaxValue)
                        continue;
                    float weight = score[k] <= 0f ? 1f : 1f - score[k] / blend;
                    heights[z, x] = Mathf.Lerp(heights[z, x], target[k] / size.y, weight);
                }
            data.SetHeights(0, 0, heights);
            EditorUtility.SetDirty(data);
        }

        // ---------- Junction plates ----------

        static List<Plate> BuildPlates(List<Way> ways)
        {
            var degree = Degrees(ways);
            // Two way ends meeting without being merged = a change of road class (or direction rules): the width
            // changes there, so it gets a plate like a junction.
            var endCount = new Dictionary<long, int>();
            foreach (var w in ways)
                foreach (var k in new[] { Key(w.Pts[0]), Key(w.Pts[w.Pts.Count - 1]) })
                {
                    endCount.TryGetValue(k, out int c);
                    endCount[k] = c + 1;
                }
            bool IsJunction(long k) => degree[k] >= 3 || degree[k] == 2 && endCount.TryGetValue(k, out int e) && e >= 2;

            // Arms: for each junction node, every way leaving it.
            var arms = new Dictionary<long, List<(Way way, int index, int step)>>();
            foreach (var w in ways)
                for (int i = 0; i < w.Pts.Count; i++)
                {
                    long k = Key(w.Pts[i]);
                    if (!IsJunction(k))
                        continue;
                    if (!arms.TryGetValue(k, out var list))
                        arms[k] = list = new List<(Way, int, int)>();
                    if (i > 0) list.Add((w, i, -1));
                    if (i < w.Pts.Count - 1) list.Add((w, i, 1));
                }

            var nodes = new List<(Vector2 centre, float reach, List<Vector2> pts)>();
            foreach (var kv in arms)
            {
                var list = kv.Value;
                var origin = list[0].way.Pts[list[0].index];
                var dirs = list.Select(a => Flat(Walk(a.way.Pts, a.index, a.step, 6f) - origin).normalized).ToArray();
                var pts = new List<Vector2>();
                float maxCut = 0f;
                for (int i = 0; i < list.Count; i++)
                {
                    float hi = list[i].way.Width * 0.5f, cut = hi + 1f;
                    for (int j = 0; j < list.Count; j++)
                    {
                        if (j == i)
                            continue;
                        float hj = list[j].way.Width * 0.5f;
                        float extra = WithFootpaths.Contains(list[j].way.Highway) ? 3.1f : 0.5f;
                        float angle = Vector2.Angle(dirs[i], dirs[j]) * Mathf.Deg2Rad;
                        // Distance along arm i at which its edge clears arm j's carriageway (and footpath).
                        float clear = angle < Mathf.PI * 0.5f ? (hi + hj + extra) / Mathf.Max(Mathf.Sin(angle), 0.25f) : hj + extra;
                        cut = Mathf.Max(cut, clear);
                    }
                    cut = Mathf.Min(cut + 0.5f, MaxCut);
                    maxCut = Mathf.Max(maxCut, cut);
                    var c = Walk(list[i].way.Pts, list[i].index, list[i].step, cut);
                    var ahead = Walk(list[i].way.Pts, list[i].index, list[i].step, cut + 1f);
                    var dir = Flat(ahead - c);
                    dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : dirs[i];
                    var side = new Vector2(dir.y, -dir.x);
                    pts.Add(Flat(c) + side * hi);
                    pts.Add(Flat(c) - side * hi);
                }
                nodes.Add((Flat(origin), maxCut, pts));
            }

            // Junctions close together (dual carriageways, staggered crossings) share one plate.
            // Capped at MaxPlate across so an interchange does not chain into one plate paving over its verges.
            const float MaxPlate = 70f;
            var parent = Enumerable.Range(0, nodes.Count).ToArray();
            var boxLo = nodes.Select(n => n.centre - Vector2.one * n.reach).ToArray();
            var boxHi = nodes.Select(n => n.centre + Vector2.one * n.reach).ToArray();
            int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
            void Union(int a, int b)
            {
                a = Find(a);
                b = Find(b);
                if (a == b)
                    return;
                var min = Vector2.Min(boxLo[a], boxLo[b]);
                var max = Vector2.Max(boxHi[a], boxHi[b]);
                if ((max - min).magnitude > MaxPlate)
                    return;
                parent[a] = b;
                boxLo[b] = min;
                boxHi[b] = max;
            }
            var grid = new Dictionary<long, List<int>>();
            const float GridCell = 64f;
            long Cell(Vector2 p) => ((long)Mathf.FloorToInt(p.x / GridCell) << 32) ^ (uint)Mathf.FloorToInt(p.y / GridCell);
            for (int i = 0; i < nodes.Count; i++)
            {
                var c = nodes[i].centre;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        long cell = Cell(c + new Vector2(dx, dz) * GridCell);
                        if (!grid.TryGetValue(cell, out var others))
                            continue;
                        foreach (int j in others)
                            if (Vector2.Distance(c, nodes[j].centre) < (nodes[i].reach + nodes[j].reach) * 0.75f)
                                Union(i, j);
                    }
                long own = Cell(c);
                if (!grid.TryGetValue(own, out var mine))
                    grid[own] = mine = new List<int>();
                mine.Add(i);
            }

            var plates = new List<Plate>();
            foreach (var group in Enumerable.Range(0, nodes.Count).GroupBy(Find))
            {
                var hull = ConvexHull(group.SelectMany(i => nodes[i].pts).ToList());
                if (hull.Length < 3)
                    continue;
                var centre = Vector2.zero;
                foreach (var p in hull)
                    centre += p;
                centre /= hull.Length;
                plates.Add(new Plate
                {
                    Hull = hull, Centre = centre,
                    Min = new Vector2(hull.Min(p => p.x), hull.Min(p => p.y)),
                    Max = new Vector2(hull.Max(p => p.x), hull.Max(p => p.y)),
                });
            }
            return plates;
        }

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        // Point at the given distance along a polyline from pts[index], stepping by step (+1 or -1).
        static Vector3 Walk(List<Vector3> pts, int index, int step, float distance)
        {
            var p = pts[index];
            for (int i = index + step; i >= 0 && i < pts.Count; i += step)
            {
                float d = Vector3.Distance(p, pts[i]);
                if (d < 1e-4f)
                    continue;
                if (d >= distance)
                    return Vector3.Lerp(p, pts[i], distance / d);
                distance -= d;
                p = pts[i];
            }
            return p;
        }

        // Andrew's monotone chain; counter-clockwise in (x, z).
        static Vector2[] ConvexHull(List<Vector2> points)
        {
            var pts = points.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            if (pts.Count < 3)
                return pts.ToArray();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var hull = new Vector2[pts.Count * 2];
            int k = 0;
            foreach (var p in pts)
            {
                while (k >= 2 && Cross(hull[k - 2], hull[k - 1], p) <= 0f) k--;
                hull[k++] = p;
            }
            for (int i = pts.Count - 2, lower = k + 1; i >= 0; i--)
            {
                while (k >= lower && Cross(hull[k - 2], hull[k - 1], pts[i]) <= 0f) k--;
                hull[k++] = pts[i];
            }
            return hull.Take(k - 1).ToArray();
        }

        sealed class PlateIndex
        {
            const float Cell = 50f;
            readonly Dictionary<long, List<Plate>> _cells = new Dictionary<long, List<Plate>>();

            public PlateIndex(List<Plate> plates)
            {
                foreach (var p in plates)
                    for (int z = Mathf.FloorToInt(p.Min.y / Cell); z <= Mathf.FloorToInt(p.Max.y / Cell); z++)
                        for (int x = Mathf.FloorToInt(p.Min.x / Cell); x <= Mathf.FloorToInt(p.Max.x / Cell); x++)
                        {
                            long k = ((long)x << 32) ^ (uint)z;
                            if (!_cells.TryGetValue(k, out var list))
                                _cells[k] = list = new List<Plate>();
                            list.Add(p);
                        }
            }

            public bool Inside(Vector3 p)
            {
                long k = ((long)Mathf.FloorToInt(p.x / Cell) << 32) ^ (uint)Mathf.FloorToInt(p.z / Cell);
                if (!_cells.TryGetValue(k, out var list))
                    return false;
                var q = new Vector2(p.x, p.z);
                foreach (var plate in list)
                {
                    if (q.x < plate.Min.x || q.y < plate.Min.y || q.x > plate.Max.x || q.y > plate.Max.y)
                        continue;
                    bool inside = true;
                    var h = plate.Hull;
                    for (int i = 0; i < h.Length && inside; i++)
                    {
                        Vector2 a = h[i], b = h[(i + 1) % h.Length];
                        inside = (b.x - a.x) * (q.y - a.y) - (b.y - a.y) * (q.x - a.x) >= 0f;
                    }
                    if (inside)
                        return true;
                }
                return false;
            }
        }

        // Concentric rings from the centre to the hull, each vertex on the carved terrain, so the plate follows
        // the ground instead of spanning it flat.
        static void AddPlate(Strip s, Plate plate, Terrain terrain)
        {
            const int Rings = 3;
            int start = s.V.Count;
            Vector3 At(Vector2 q)
            {
                var v = new Vector3(q.x, 0f, q.y);
                v.y = terrain.SampleHeight(v) + PlateLift;
                return v;
            }
            s.V.Add(At(plate.Centre));
            s.UV.Add(plate.Centre);
            int n = plate.Hull.Length;
            for (int r = 1; r <= Rings; r++)
                for (int i = 0; i < n; i++)
                {
                    var q = Vector2.Lerp(plate.Centre, plate.Hull[i], r / (float)Rings);
                    s.V.Add(At(q));
                    s.UV.Add(q);
                }
            void Tri(int a, int b, int c)
            {
                var normal = Vector3.Cross(s.V[b] - s.V[a], s.V[c] - s.V[a]);
                if (normal.y >= 0f) { s.T.Add(a); s.T.Add(b); s.T.Add(c); }
                else { s.T.Add(a); s.T.Add(c); s.T.Add(b); }
            }
            int Ring(int r, int i) => start + 1 + (r - 1) * n + (i % n);
            for (int i = 0; i < n; i++)
                Tri(start, Ring(1, i), Ring(1, i + 1));
            for (int r = 1; r < Rings; r++)
                for (int i = 0; i < n; i++)
                {
                    Tri(Ring(r, i), Ring(r + 1, i), Ring(r + 1, i + 1));
                    Tri(Ring(r, i), Ring(r + 1, i + 1), Ring(r, i + 1));
                }
        }

        // ---------- Strips ----------

        // Pieces of the line covering DashOn metres out of every DashPeriod.
        static IEnumerable<List<Sample>> Dashes(List<Sample> line)
        {
            float total = line[line.Count - 1].Dist;
            Sample At(float d)
            {
                int i = 0;
                while (i < line.Count - 2 && line[i + 1].Dist < d)
                    i++;
                float seg = line[i + 1].Dist - line[i].Dist;
                return Lerp(line[i], line[i + 1], seg > 0f ? (d - line[i].Dist) / seg : 0f);
            }
            for (float s0 = 1f; s0 < total; s0 += DashPeriod)
            {
                float s1 = Mathf.Min(s0 + DashOn, total);
                if (s1 - s0 < 0.5f)
                    yield break;
                var piece = new List<Sample> { At(s0) };
                foreach (var p in line)
                    if (p.Dist > s0 && p.Dist < s1)
                        piece.Add(p);
                piece.Add(At(s1));
                yield return piece;
            }
        }

        // Ribbon between lateral offsets left..right (positive = right of travel), UVs in metres (V scaled by
        // vPerMetre for patterns that repeat along the road, such as kerb stripes). With an index, the ribbon is
        // cut exactly where it enters a junction plate.
        static void AddStrip(System.Func<Material, int, Strip> strips, Material mat, List<Sample> line, float left, float right,
                             float lift, Terrain terrain, PlateIndex plates, float vPerMetre = 1f, int chunk = -1)
        {
            if (line.Count < 2)
                return;
            float mid = (left + right) * 0.5f;
            bool In(Sample q) => plates != null &&
                                 (plates.Inside(q.P + q.Side * left) || plates.Inside(q.P + q.Side * right) || plates.Inside(q.P + q.Side * mid));

            var run = new List<Sample>();
            void Flush()
            {
                if (run.Count >= 2)
                    Emit(strips(mat, chunk >= 0 ? chunk : ChunkOf(run[0].P, terrain)), run, left, right, lift, terrain, vPerMetre);
                run.Clear();
            }

            bool prevIn = In(line[0]);
            if (!prevIn)
                run.Add(line[0]);
            for (int i = 1; i < line.Count; i++)
            {
                bool curIn = In(line[i]);
                if (curIn != prevIn)
                {
                    // Bisect for the plate edge between the two samples.
                    float lo = 0f, hi = 1f;
                    for (int k = 0; k < 12; k++)
                    {
                        float t = (lo + hi) * 0.5f;
                        if (In(Lerp(line[i - 1], line[i], t)) == prevIn) lo = t; else hi = t;
                    }
                    var edge = Lerp(line[i - 1], line[i], prevIn ? hi : lo);
                    if (prevIn)
                        run.Add(edge);
                    else
                    {
                        run.Add(edge);
                        Flush();
                    }
                }
                if (!curIn)
                    run.Add(line[i]);
                prevIn = curIn;
            }
            Flush();
        }

        static void Emit(Strip s, List<Sample> line, float left, float right, float lift, Terrain terrain, float vPerMetre)
        {
            int start = s.V.Count;
            Vector3 prevA = default, prevB = default;
            for (int i = 0; i < line.Count; i++)
            {
                var q = line[i];
                Vector3 a = q.P + q.Side * left, b = q.P + q.Side * right;
                // Inside of a tight bend: hold the edge rather than let it fold back over itself.
                if (i > 0)
                {
                    if (Vector3.Dot(a - prevA, q.Dir) < 0f) a = prevA;
                    if (Vector3.Dot(b - prevB, q.Dir) < 0f) b = prevB;
                }
                prevA = a;
                prevB = b;
                a.y = terrain.SampleHeight(a) + lift;
                b.y = terrain.SampleHeight(b) + lift;
                s.V.Add(a);
                s.V.Add(b);
                s.UV.Add(new Vector2(left, q.Dist * vPerMetre));
                s.UV.Add(new Vector2(right, q.Dist * vPerMetre));
            }
            for (int i = 0; i < line.Count - 1; i++)
            {
                int a = start + i * 2, b = a + 1, c = a + 3, d = a + 2;
                Vector3 n = Vector3.Cross(s.V[b] - s.V[a], s.V[c] - s.V[a]);
                if (n.y >= 0f) { s.T.Add(a); s.T.Add(b); s.T.Add(c); s.T.Add(a); s.T.Add(c); s.T.Add(d); }
                else { s.T.Add(a); s.T.Add(c); s.T.Add(b); s.T.Add(a); s.T.Add(d); s.T.Add(c); }
            }
        }

        public const string GroupName = "Roads (photo asphalt, markings, kerbs, footpaths, crossings)";

        // Height of the walkable surface at p: the highest road, kerb or footpath collider, else the terrain.
        // Needs the road colliders in the scene (call after Build).
        public static float SurfaceHeight(Terrain terrain, Vector3 p)
        {
            float y = terrain.SampleHeight(p) + terrain.transform.position.y;
            var from = new Vector3(p.x, y + 5f, p.z);
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider is MeshCollider && hit.transform.parent != null && hit.transform.parent.name == GroupName)
                    y = Mathf.Max(y, hit.point.y);
            return y;
        }

        static void WriteMeshes(Dictionary<(Material, int), Strip> strips, Transform root, string outDir, string district)
        {
            var group = new GameObject(GroupName).transform;
            group.SetParent(root, false);
            string path = $"{outDir}/{district}_Roads.asset";
            AssetDatabase.DeleteAsset(path);
            Mesh container = null;
            foreach (var kv in strips)
            {
                var (material, chunk) = kv.Key;
                var s = kv.Value;
                if (s.T.Count == 0)
                    continue;
                var mesh = new Mesh { name = $"{material.name}_{chunk}", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(s.V);
                mesh.SetUVs(0, s.UV);
                mesh.SetTriangles(s.T, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                if (container == null)
                {
                    AssetDatabase.CreateAsset(mesh, path);
                    container = mesh;
                }
                else
                    AssetDatabase.AddObjectToAsset(mesh, container);

                var go = new GameObject(mesh.name);
                go.transform.SetParent(group, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                // Road surfaces sit ~0.3 m above the terrain: people and cars must stand on them, not on the
                // terrain underneath. Paint is a few millimetres thick and needs no collider.
                if (!material.name.StartsWith("Paint"))
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            AssetDatabase.SaveAssets();
        }

        public static Material KerbMaterial(string outDir)
        {
            var tex = EditorAssetUtil.GeneratedTexture($"{outDir}/Textures/T_KerbStripes.png", 64, (x, y) =>
                y < 32 ? new Color(0.07f, 0.07f, 0.07f) : new Color(0.92f, 0.92f, 0.90f), 64);
            return EditorAssetUtil.LitMaterial(outDir, "Kerb_Striped", Color.white, 0.2f, tex, 1f);
        }
    }
}
