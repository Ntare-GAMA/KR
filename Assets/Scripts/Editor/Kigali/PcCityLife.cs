using System;
using System.Collections.Generic;
using System.Linq;
using KR.Editor.GIS;
using KR.Editor.Util;
using KR.Gameplay.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Activity = KR.Editor.Kigali.PcPeople.Activity;
using Category = KR.Editor.Kigali.ScatterCollector.Category;

namespace KR.Editor.Kigali
{
    // Real named places from OSM brought to life: market stalls with vendors and shoppers, park strollers, outdoor
    // news screens with watchers, Umuganda community work, people waiting at bus stops and chatting on the street.
    // Memorial sites and Camp Kigali get no activities or captions; real business names are never shown.
    internal static class PcCityLife
    {
        [Serializable] internal class PlaceList { public Place[] places; }
        [Serializable] internal class Place { public string kind; public string name; public float[] pts; }

        public struct Anchor
        {
            public Vector3 Position;
            public float Size;
            public string Name;
        }

        public sealed class Result
        {
            public readonly Dictionary<string, Anchor> Anchors = new Dictionary<string, Anchor>();
            public readonly List<(Vector2[] polygon, float radius, Vector3 centre)> Exclusions = new List<(Vector2[], float, Vector3)>();
            public readonly List<(Vector3 centre, float yaw, float w, float d)> Markets = new List<(Vector3, float, float, float)>();
        }

        public sealed class Context
        {
            public Terrain Terrain;
            public Transform Root;
            public PcShowcaseBuilder.Catalog Cat;
            public ScatterCollector Scatter;
            public System.Random Rng;
            public string OutDir;
            public string District;
            public Occupancy Grid;
            public Place[] Places;
            public DistrictImporter.Road[] Roads;
            public List<(Vector3 pos, float facing)> BusStops;
            public List<(Vector3 pos, float facing)> Footpaths;
            public Func<Vector3, float, bool> NearHub;
        }

        static readonly string[] Sensitive = { "memorial", "genocide", "urwibutso", "camp kigali" };

        public static bool Respectful(Place p) =>
            string.IsNullOrEmpty(p.name) || !Sensitive.Any(s => p.name.ToLowerInvariant().Contains(s));

        // ---------- Geometry ----------

        public static float Area(float[] pts)
        {
            float a = 0f;
            int n = pts.Length / 2;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                a += pts[i * 2] * pts[j * 2 + 1] - pts[j * 2] * pts[i * 2 + 1];
            }
            return Mathf.Abs(a * 0.5f);
        }

        public static Vector3 Centroid(float[] pts)
        {
            float x = 0f, z = 0f;
            int n = pts.Length / 2;
            for (int i = 0; i < n; i++) { x += pts[i * 2]; z += pts[i * 2 + 1]; }
            return new Vector3(x / n, 0f, z / n);
        }

        public static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        public static Vector2[] Polygon(float[] pts)
        {
            var poly = new Vector2[pts.Length / 2];
            for (int i = 0; i < poly.Length; i++)
                poly[i] = new Vector2(pts[i * 2], pts[i * 2 + 1]);
            return poly;
        }

        // Oriented box aligned with the longest edge of a footprint.
        public static bool OrientedBox(float[] pts, Terrain terrain, out Vector3 centre, out float yaw, out float w, out float d, out float ground)
        {
            int n = pts.Length / 2;
            float longest = 0f;
            yaw = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var e = new Vector3(pts[j * 2] - pts[i * 2], 0f, pts[j * 2 + 1] - pts[i * 2 + 1]);
                if (e.magnitude > longest)
                {
                    longest = e.magnitude;
                    yaw = Mathf.Atan2(e.x, e.z) * Mathf.Rad2Deg;
                }
            }
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var inv = Quaternion.Inverse(rot);
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            ground = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3(pts[i * 2], 0f, pts[i * 2 + 1]);
                var q = inv * p;
                min = Vector3.Min(min, q);
                max = Vector3.Max(max, q);
                ground = Mathf.Min(ground, terrain.SampleHeight(p));
            }
            w = max.x - min.x;
            d = max.z - min.z;
            centre = rot * ((min + max) * 0.5f);
            return w >= 3f && d >= 3f;
        }

        static Vector3 Ground(Terrain t, Vector3 p, float lift = 0f)
        {
            p.y = t.SampleHeight(p) + lift;
            return p;
        }

        static float R(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        static GameObject Pick(GameObject[] items, System.Random rng) => items == null || items.Length == 0 ? null : items[rng.Next(items.Length)];

        // ---------- Occupancy ----------

        public sealed class Occupancy
        {
            readonly float _cell;
            readonly int _n;
            readonly bool[] _blocked;

            public Occupancy(float size, float cell)
            {
                _cell = cell;
                _n = Mathf.CeilToInt(size / cell);
                _blocked = new bool[_n * _n];
            }

            public void Mark(float x0, float z0, float x1, float z1)
            {
                int i0 = Mathf.Clamp((int)(x0 / _cell), 0, _n - 1), i1 = Mathf.Clamp((int)(x1 / _cell), 0, _n - 1);
                int j0 = Mathf.Clamp((int)(z0 / _cell), 0, _n - 1), j1 = Mathf.Clamp((int)(z1 / _cell), 0, _n - 1);
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                        _blocked[j * _n + i] = true;
            }

            public void Mark(Vector3 p, float r) => Mark(p.x - r, p.z - r, p.x + r, p.z + r);

            public bool Blocked(Vector3 p) =>
                _blocked[Mathf.Clamp((int)(p.z / _cell), 0, _n - 1) * _n + Mathf.Clamp((int)(p.x / _cell), 0, _n - 1)];

            public bool Free(Vector3 p, float r)
            {
                for (float dz = -r; dz <= r; dz += _cell)
                    for (float dx = -r; dx <= r; dx += _cell)
                        if (Blocked(p + new Vector3(dx, 0f, dz)))
                            return false;
                return true;
            }

            // Nearest free spot of radius r within maxDistance (spiral search), or null.
            public Vector3? FindFree(Vector3 p, float r, float maxDistance)
            {
                for (float ring = 0f; ring <= maxDistance; ring += _cell * 2f)
                    for (int a = 0; a < Mathf.Max(1, Mathf.CeilToInt(ring / 3f)); a++)
                    {
                        float ang = a * Mathf.PI * 2f / Mathf.Max(1, Mathf.CeilToInt(ring / 3f));
                        var q = p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * ring;
                        if (Free(q, r))
                            return q;
                    }
                return null;
            }
        }

        // ---------- Exclusions (computed before buildings are placed) ----------

        public static void CollectExclusions(Result result, Place[] places, Func<Vector3, float, bool> nearHub)
        {
            foreach (var p in places)
            {
                if (p.pts.Length < 6 || !Respectful(p))
                    continue;
                if (p.kind == "stadium")
                    result.Exclusions.Add((Polygon(p.pts), 0f, Vector3.zero));
                else if (p.kind == "conference_centre")
                    result.Exclusions.Add((null, DomeRadius(p.pts) + 4f, Centroid(p.pts)));
            }
        }

        public static bool Excluded(Result result, Vector3 p)
        {
            foreach (var (poly, radius, centre) in result.Exclusions)
                if (poly != null ? Inside(poly, new Vector2(p.x, p.z)) : Vector2.Distance(new Vector2(p.x, p.z), new Vector2(centre.x, centre.z)) < radius)
                    return true;
            return false;
        }

        static float DomeRadius(float[] pts)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i + 1 < pts.Length; i += 2)
            {
                minX = Mathf.Min(minX, pts[i]); maxX = Mathf.Max(maxX, pts[i]);
                minZ = Mathf.Min(minZ, pts[i + 1]); maxZ = Mathf.Max(maxZ, pts[i + 1]);
            }
            return Mathf.Clamp(0.3f * Mathf.Min(maxX - minX, maxZ - minZ), 18f, 32f);
        }

        // ---------- Build ----------

        public static void Build(Context c, Result r)
        {
            var meshes = new MeshStore($"{c.OutDir}/{c.District}_CityMeshes.asset");
            var life = new GameObject("City life (markets, parks, news screens, community)").transform;
            life.SetParent(c.Root, false);

            Markets(c, r, life, meshes);
            Parks(c, r, life);
            Screens(c, r, life, meshes);
            Umuganda(c, r, life);
            StreetPeople(c, life);
            AssetDatabase.SaveAssets();
        }

        static Transform Group(Transform parent, string name, float fromHour, float toHour)
        {
            var g = new GameObject(name).transform;
            g.SetParent(parent, false);
            var schedule = new GameObject($"{name} (people, {fromHour:00.#}h-{toHour:00.#}h)");
            schedule.transform.SetParent(g, false);
            schedule.AddComponent<ScheduleVisibility>().Configure(fromHour, toHour);
            return g;
        }

        static Transform People(Transform group) => group.GetChild(0);

        static GameObject Person(Context c, Transform parent, Vector3 p, float yaw, Activity a) =>
            c.Cat.Pedestrians.Length == 0 ? null : PcPeople.Spawn(Pick(c.Cat.Pedestrians, c.Rng), parent, p, yaw, a, c.Rng);

        static IEnumerable<Place> Near(Context c, string kind, float radius) =>
            c.Places.Where(p => p.kind == kind && Respectful(p) && c.NearHub(Centroid(p.pts), radius));

        static void SetAnchor(Result r, string key, Vector3 pos, float size, string name, bool overwrite = false)
        {
            if (overwrite || !r.Anchors.ContainsKey(key))
                r.Anchors[key] = new Anchor { Position = pos, Size = size, Name = name };
        }

        // Stalls with colourful canopies, crates, vendors and shoppers at real marketplaces.
        static void Markets(Context c, Result r, Transform life, MeshStore meshes)
        {
            var stallMesh = meshes.Add(StallMesh(), "Stall");
            var wood = EditorAssetUtil.LitMaterial(c.OutDir, "PC_StallWood", new Color(0.45f, 0.32f, 0.2f), 0.1f);
            var canopies = new[]
            {
                new Color(0.85f, 0.2f, 0.15f), new Color(0.15f, 0.45f, 0.8f), new Color(0.95f, 0.75f, 0.1f),
                new Color(0.2f, 0.6f, 0.3f), new Color(0.9f, 0.45f, 0.1f), new Color(0.6f, 0.2f, 0.6f),
            }.Select((col, i) => EditorAssetUtil.LitMaterial(c.OutDir, $"PC_Canopy{i}", col, 0.2f)).ToArray();

            var markets = Near(c, "market", 2600f)
                .OrderByDescending(p => p.name.Contains("Kimironko") ? 1 : 0).ThenByDescending(p => p.pts.Length > 2 ? Area(p.pts) : 0f)
                .Take(5).ToList();
            foreach (var p in markets)
            {
                Vector3 centre;
                float yaw, w, d;
                if (p.pts.Length >= 6)
                {
                    if (!OrientedBox(p.pts, c.Terrain, out centre, out yaw, out w, out d, out _))
                        continue;
                }
                else
                {
                    var free = c.Grid.FindFree(new Vector3(p.pts[0], 0f, p.pts[1]), 10f, 60f);
                    if (free == null)
                        continue;
                    centre = free.Value;
                    yaw = (float)c.Rng.NextDouble() * 90f;
                    w = d = 24f;
                }
                var group = Group(life, $"Market ({p.name})", 6f, 19f);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                int stalls = 0;
                for (float x = -w * 0.5f + 2f; x <= w * 0.5f - 2f && stalls < 90; x += 4f)
                    for (float z = -d * 0.5f + 2f; z <= d * 0.5f - 2f && stalls < 90; z += 4.5f)
                    {
                        var pos = Ground(c.Terrain, centre + rot * new Vector3(x, 0f, z));
                        if (c.Grid.Blocked(pos))
                            continue;
                        var stall = new GameObject("Stall");
                        stall.transform.SetParent(group, false);
                        stall.transform.SetPositionAndRotation(pos, rot * Quaternion.Euler(0f, c.Rng.Next(2) * 180f, 0f));
                        stall.AddComponent<MeshFilter>().sharedMesh = stallMesh;
                        stall.AddComponent<MeshRenderer>().sharedMaterials = new[] { wood, canopies[c.Rng.Next(canopies.Length)] };
                        var crate = Pick(c.Cat.Boxes, c.Rng);
                        if (crate != null)
                            c.Scatter.Add(crate, c.Scatter.Fit(crate, Ground(c.Terrain, pos + stall.transform.right * 1.3f), R(c.Rng, 0f, 360f), 0.5f, 0.8f), Category.Prop);
                        if (c.Rng.NextDouble() < 0.5)
                            Person(c, People(group), Ground(c.Terrain, pos - stall.transform.forward * 1.1f), stall.transform.eulerAngles.y, Activity.Vendor);
                        c.Grid.Mark(pos, 1.8f);
                        stalls++;
                    }
                int shoppers = Mathf.Clamp(stalls / 2, 6, 30);
                for (int i = 0; i < shoppers; i++)
                {
                    var person = Person(c, People(group), Ground(c.Terrain, centre + rot * new Vector3(R(c.Rng, -w, w) * 0.4f, 0f, R(c.Rng, -d, d) * 0.4f)),
                                        R(c.Rng, 0f, 360f), Activity.Shopper);
                    // Shoppers stop at stalls to look and haggle.
                    if (person != null)
                        person.AddComponent<Wanderer>().Configure(centre, new Vector2(w * 0.45f, d * 0.45f), yaw, R(c.Rng, 0.9f, 1.3f),
                                                                  0f, 0.6f, new Vector2(3f, 8f));
                }
                SetAnchor(r, "market", Ground(c.Terrain, centre), Mathf.Max(w, d), p.name);
                r.Markets.Add((centre, yaw, w, d));
            }
        }

        // Strollers and people chatting in real parks.
        static void Parks(Context c, Result r, Transform life)
        {
            foreach (var p in Near(c, "park", 1600f).Where(p => p.pts.Length >= 6 && Area(p.pts) > 1200f).Take(8))
            {
                if (!OrientedBox(p.pts, c.Terrain, out var centre, out float yaw, out float w, out float d, out _))
                    continue;
                var group = Group(life, $"Park ({p.name})", 6f, 20f);
                for (int i = 0; i < 6; i++)
                {
                    var person = Person(c, People(group), Ground(c.Terrain, centre), R(c.Rng, 0f, 360f), Activity.Stroll);
                    if (person != null)
                        person.AddComponent<Wanderer>().Configure(centre, new Vector2(w * 0.4f, d * 0.4f), yaw, R(c.Rng, 0.8f, 1.2f),
                                                                  0f, 0.35f, new Vector2(3f, 7f));
                }
                Pairs(c, People(group), Ground(c.Terrain, centre + new Vector3(R(c.Rng, -w, w) * 0.2f, 0f, R(c.Rng, -d, d) * 0.2f)), 2);
                if (!string.IsNullOrEmpty(p.name))
                    SetAnchor(r, "park", Ground(c.Terrain, centre), Mathf.Max(w, d), p.name);
            }
        }

        // Outdoor screens with the news on, and people gathered to watch.
        static void Screens(Context c, Result r, Transform life, MeshStore meshes)
        {
            var screenMat = PcMaterials.Emissive(c.OutDir, "PC_Screen", new Color(0.05f, 0.05f, 0.08f), new Color(0.2f, 0.4f, 1f), 0.9f);
            var standMat = EditorAssetUtil.LitMaterial(c.OutDir, "PC_ScreenStand", new Color(0.15f, 0.15f, 0.17f), 0.4f);
            var standMesh = meshes.Add(BoxMesh(new Vector3(0.25f, 3.2f, 0.25f), new Vector3(0f, 1.6f, 0f)), "ScreenStand");
            var panelMesh = meshes.Add(BoxMesh(new Vector3(3.6f, 2.1f, 0.15f), Vector3.zero), "ScreenPanel");
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var sites = Near(c, "bus_station", 2600f).Select(p => (p, hours: (6f, 22f)))
                .Concat(Near(c, "bar", 1600f).Skip(3).Select(p => (p, hours: (17f, 23.5f)))).Take(8).ToList();
            foreach (var (p, hours) in sites)
            {
                var seed = p.pts.Length >= 6 ? Centroid(p.pts) : new Vector3(p.pts[0], 0f, p.pts[1]);
                var free = c.Grid.FindFree(seed, 6f, 70f);
                if (free == null)
                    continue;
                var spot = Ground(c.Terrain, free.Value);
                float yaw = R(c.Rng, 0f, 360f);
                var group = Group(life, "News screen", hours.Item1, hours.Item2);
                var screen = new GameObject("Screen");
                screen.transform.SetParent(group, false);
                screen.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));
                screen.AddComponent<MeshFilter>().sharedMesh = standMesh;
                screen.AddComponent<MeshRenderer>().sharedMaterial = standMat;
                var panel = new GameObject("Panel");
                panel.transform.SetParent(screen.transform, false);
                panel.transform.localPosition = new Vector3(0f, 3.6f, 0.15f);
                panel.AddComponent<MeshFilter>().sharedMesh = panelMesh;
                var panelRenderer = panel.AddComponent<MeshRenderer>();
                panelRenderer.sharedMaterial = screenMat;
                var tickerGo = new GameObject("Ticker");
                tickerGo.transform.SetParent(panel.transform, false);
                tickerGo.transform.localPosition = new Vector3(0f, -0.8f, 0.09f);
                tickerGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var ticker = tickerGo.AddComponent<TextMesh>();
                ticker.text = "AMAKURU  •  NEWS";
                ticker.font = font;
                ticker.fontSize = 48;
                ticker.characterSize = 0.04f;
                ticker.anchor = TextAnchor.MiddleCenter;
                ticker.color = Color.white;
                tickerGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                screen.AddComponent<ScreenAnimator>().Configure(panelRenderer, ticker, 0f);

                var facing = Quaternion.Euler(0f, yaw, 0f);
                for (int i = 0; i < 7; i++)
                {
                    float a = (i - 3) * 14f * Mathf.Deg2Rad;
                    var pos = spot + facing * new Vector3(Mathf.Sin(a) * 6f, 0f, Mathf.Cos(a) * 6f + R(c.Rng, -0.6f, 0.6f));
                    Person(c, People(group), Ground(c.Terrain, pos), yaw + 180f + R(c.Rng, -10f, 10f), i % 3 == 0 ? Activity.Talk : Activity.Watch);
                }
                c.Grid.Mark(spot, 6f);
                SetAnchor(r, "screen", spot + Vector3.up * 3f, 8f, p.name);
            }
        }

        // Umuganda: on the last Saturday of each month neighbours do community work together.
        static void Umuganda(Context c, Result r, Transform life)
        {
            int sites = 0;
            foreach (var road in c.Roads)
            {
                if (sites >= 3 || road.highway != "residential" || road.pts == null || road.pts.Length < 12)
                    continue;
                var line = PcRoadBuilder.ResampleLine(road.pts);
                var mid = line[line.Count / 2];
                if (!c.NearHub(mid, 1400f) || r.Anchors.TryGetValue("umuganda", out var prev) && Vector3.Distance(prev.Position, mid) < 800f)
                    continue;
                var dir = (line[Mathf.Min(line.Count / 2 + 1, line.Count - 1)] - mid).normalized;
                var right = new Vector3(dir.z, 0f, -dir.x);
                float roadYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                var group = Group(life, "Umuganda community work", 7.5f, 12f);
                for (int i = 0; i < 10; i++)
                {
                    var pos = Ground(c.Terrain, mid + dir * (i * 2.2f - 10f) + right * (3.2f + R(c.Rng, -0.4f, 0.6f)));
                    Person(c, People(group), pos, roadYaw - 90f + R(c.Rng, -30f, 30f), i % 4 == 3 ? Activity.Talk : Activity.Work);
                }
                for (int i = 0; i < 6; i++)
                {
                    var item = Pick(i % 2 == 0 ? c.Cat.TrashBags : c.Cat.Boxes, c.Rng) ?? Pick(c.Cat.Roadworks, c.Rng);
                    if (item != null)
                        c.Scatter.Add(item, c.Scatter.Fit(item, Ground(c.Terrain, mid + dir * (i * 3f - 9f) + right * 4.4f), R(c.Rng, 0f, 360f), 0.7f, 1.2f), Category.Prop);
                }
                if (sites == 0)
                {
                    SetAnchor(r, "umuganda", Ground(c.Terrain, mid), 25f, "Umuganda");
                    SetAnchor(r, "umugandaDir", Ground(c.Terrain, mid + dir * 20f), 0f, "");
                }
                sites++;
            }
        }

        // People waiting at bus stops and chatting in pairs along the footpaths.
        static void StreetPeople(Context c, Transform life)
        {
            var group = Group(life, "Street life (waiting, chatting)", 5.5f, 22.5f);
            foreach (var (pos, facing) in c.BusStops.Take(40))
                for (int i = 0, n = c.Rng.Next(2, 5); i < n; i++)
                {
                    var right = Quaternion.Euler(0f, facing, 0f) * Vector3.right;
                    Person(c, People(group), Ground(c.Terrain, pos + right * R(c.Rng, -2.5f, 2.5f), 0.4f), facing + R(c.Rng, -25f, 25f),
                           i == 0 ? Activity.Talk : Activity.Wait);
                }
            foreach (var (pos, _) in c.Footpaths.Take(40))
                Pairs(c, People(group), Ground(c.Terrain, pos, 0.4f), 1);
        }

        static void Pairs(Context c, Transform parent, Vector3 at, int count)
        {
            for (int k = 0; k < count; k++)
            {
                float yaw = R(c.Rng, 0f, 360f);
                var offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, 0.6f);
                var basePos = at + new Vector3(k * 3f, 0f, 0f);
                Person(c, parent, basePos - offset, yaw, Activity.Talk);
                Person(c, parent, basePos + offset, yaw + 180f, c.Rng.NextDouble() < 0.3 ? Activity.Wave : Activity.Talk);
            }
        }

        // ---------- Terrain ----------

        internal static Vector2[] Corners(Vector3 centre, float yaw, float w, float d)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            return new[] { new Vector3(-w, 0f, -d), new Vector3(w, 0f, -d), new Vector3(w, 0f, d), new Vector3(-w, 0f, d) }
                .Select(v => centre + rot * (v * 0.5f)).Select(v => new Vector2(v.x, v.z)).ToArray();
        }

        internal static float[] Blur(float[] src, int n, int r)
        {
            var tmp = new float[src.Length];
            var dst = new float[src.Length];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float s = 0f;
                    for (int k = -r; k <= r; k++) s += src[y * n + Mathf.Clamp(x + k, 0, n - 1)];
                    tmp[y * n + x] = s / (2 * r + 1);
                }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float s = 0f;
                    for (int k = -r; k <= r; k++) s += tmp[Mathf.Clamp(y + k, 0, n - 1) * n + x];
                    dst[y * n + x] = s / (2 * r + 1);
                }
            return dst;
        }

        internal static void Rasterise(Vector2[] poly, bool[] mask, int res, float cell)
        {
            float minX = poly.Min(p => p.x), maxX = poly.Max(p => p.x), minZ = poly.Min(p => p.y), maxZ = poly.Max(p => p.y);
            for (int y = Mathf.Max(0, (int)(minZ / cell)); y <= Mathf.Min(res - 1, (int)(maxZ / cell)); y++)
                for (int x = Mathf.Max(0, (int)(minX / cell)); x <= Mathf.Min(res - 1, (int)(maxX / cell)); x++)
                    if (Inside(poly, new Vector2((x + 0.5f) * cell, (y + 0.5f) * cell)))
                        mask[y * res + x] = true;
        }

        // ---------- Procedural meshes ----------

        internal sealed class MeshStore
        {
            readonly string _path;
            Mesh _container;

            public MeshStore(string path)
            {
                _path = path;
                AssetDatabase.DeleteAsset(path);
            }

            public Mesh Add(Mesh mesh, string name)
            {
                mesh.name = name;
                if (_container == null)
                {
                    AssetDatabase.CreateAsset(mesh, _path);
                    _container = mesh;
                }
                else
                    AssetDatabase.AddObjectToAsset(mesh, _container);
                return mesh;
            }
        }

        static Mesh BoxMesh(Vector3 size, Vector3 centre)
        {
            var b = new BoxMeshBuilder(1);
            b.Box(0, centre, size);
            return b.ToMesh("Box");
        }

        // Four poles and a table (submesh 0, wood) under a canopy (submesh 1).
        static Mesh StallMesh()
        {
            var b = new BoxMeshBuilder(2);
            foreach (var (x, z) in new[] { (-1.4f, -1f), (1.4f, -1f), (-1.4f, 1f), (1.4f, 1f) })
                b.Box(0, new Vector3(x, 1.2f, z), new Vector3(0.08f, 2.4f, 0.08f));
            b.Box(0, new Vector3(0f, 0.85f, 0f), new Vector3(2.8f, 0.08f, 1.6f));
            b.Box(1, new Vector3(0f, 2.45f, 0f), new Vector3(3.2f, 0.1f, 2.4f));
            return b.ToMesh("Stall");
        }
    }
}
