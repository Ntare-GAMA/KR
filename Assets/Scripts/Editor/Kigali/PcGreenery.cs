using System.Collections.Generic;
using System.Linq;
using KR.Editor.GIS;
using KR.Editor.Util;
using KR.Gameplay.World;
using UnityEditor;
using UnityEngine;

namespace KR.Editor.Kigali
{
    // Kigali's green look for the PC scenes:
    //   ground: manicured lawn around the hubs, terraced hillsides (contour bands), mossy forest floor on the
    //     wooded hills, red laterite only on steep banks, clean verges along the main roads;
    //   details: short lawn grass, taller meadow grass, flower beds (road medians, parks, gardens, verges);
    //   trees: Conifers [BOTD] as terrain trees (per-tree LOD and billboards) lining the avenues, in park groves
    //     and as forest on the hills;
    //   weather: the CTI WindZone driven by WeatherCycle, rain particles.
    // Packs: Outdoor Ground Textures (ADG_Textures), Grass Flowers Pack Free (ALP_Assets), Conifers [BOTD] and
    // CTI Runtime Components (Forst). Entries in Documentation/asset-register.csv.
    internal static class PcGreenery
    {
        const string Alp = "Assets/ALP_Assets/GrassFlowersFREE/Textures";
        const string Adg = "Assets/ADG_Textures/ground_vol1";
        const string Conifers = "Assets/Forst/Conifers [BOTD]/Render Pipeline Support/URP/Prefabs";
        const string WindPrefab = "Assets/Forst/CTI Runtime Components/CTI Runtime Components URP 14plus/Prefabs/CTI Windzone URP.prefab";

        // Terrain layer order.
        const int Lawn = 0, Meadow = 1, ForestFloor = 2, RedEarth = 3, Soil = 4, MarketSand = 5, Yard = 6;

        // Grass detail order.
        const int LawnGrass = 0, TallGrass = 1, FirstFlower = 2;
        static readonly string[] Flowers = { "grassFlower03", "grassFlower06", "grassFlower07", "grassFlower01", "grassFlower10", "grassFlower04" };

        public sealed class Masks
        {
            public int Res;
            public float Cell;
            public bool[] Lawn, Flowers, Verge, Market, Paved, Sport, Farm;
            public float[] Forest, Built, Terrace;
            public float[] Edge;   // metres from the nearest paving, building or market
            public float[] Area;   // m² of the open patch the cell belongs to
            public List<Vector3> Pockets = new List<Vector3>();   // deepest point of each small open patch (y holds its area)
        }

        // ---------- Ground ----------

        public static Masks Paint(Terrain terrain, DistrictImporter.Building[] buildings, PcCityLife.Place[] places,
                                  List<(Vector3 centre, float yaw, float w, float d)> markets,
                                  List<(Vector3 centre, float yaw, float w, float d)> gardens,
                                  DistrictImporter.Road[] roads, Vector3[] hubs, string outDir)
        {
            var data = terrain.terrainData;
            data.terrainLayers = new[]
            {
                Layer(outDir, "Lawn", $"{Alp}/Ground/Grass01_BigUV.png", null, 7f),
                Layer(outDir, "Meadow", Diffuse(6), Normal(6), 3.5f),
                Layer(outDir, "ForestFloor", Diffuse(5), Normal(5), 4.5f),
                Layer(outDir, "RedEarth", Diffuse(13), Normal(13), 5f),
                Layer(outDir, "Soil", Diffuse(1), Normal(1), 3f),
                PcMaterials.Layer(outDir, "red_sand", 5f),
                PcMaterials.Layer(outDir, "park_dirt", 6f),
            };
            const int res = 2048;
            data.alphamapResolution = res;
            float cell = data.size.x / res;
            var m = new Masks
            {
                Res = res, Cell = cell, Lawn = new bool[res * res], Flowers = new bool[res * res], Verge = new bool[res * res],
                Market = new bool[res * res], Forest = new float[res * res], Built = new float[res * res], Terrace = new float[res * res],
                Paved = new bool[res * res], Sport = new bool[res * res], Farm = new bool[res * res],
            };

            foreach (var b in buildings)
            {
                if (b.pts.Length < 2)
                    continue;
                int i = Mathf.Clamp((int)(b.pts[0] / cell), 0, res - 1), j = Mathf.Clamp((int)(b.pts[1] / cell), 0, res - 1);
                m.Built[j * res + i] += 1f;
                if (b.pts.Length >= 6)
                    PcCityLife.Rasterise(PcCityLife.Polygon(b.pts), m.Paved, res, cell);
            }
            PavedRoads(roads, m);
            m.Built = PcCityLife.Blur(PcCityLife.Blur(m.Built, res, 4), res, 4);

            foreach (var p in places)
                if ((p.kind == "park" || p.kind == "pitch" || p.kind == "stadium") && p.pts.Length >= 6)
                    PcCityLife.Rasterise(PcCityLife.Polygon(p.pts), m.Lawn, res, cell);
            foreach (var (centre, yaw, w, d) in gardens)
                PcCityLife.Rasterise(PcCityLife.Corners(centre, yaw, w, d), m.Lawn, res, cell);
            foreach (var (centre, yaw, w, d) in markets)
                PcCityLife.Rasterise(PcCityLife.Corners(centre, yaw, w, d), m.Market, res, cell);
            foreach (var p in places)
                if ((p.kind == "pitch" || p.kind == "stadium") && p.pts.Length >= 6)
                    PcCityLife.Rasterise(PcCityLife.Polygon(p.pts), m.Sport, res, cell);
            OpenSpace(m);
            // Flower borders: a band just inside each park, and the gardens themselves.
            foreach (var p in places)
                if (p.kind == "park" && p.pts.Length >= 6)
                    Border(PcCityLife.Polygon(p.pts), m.Flowers, res, cell, 4f);
            foreach (var (centre, yaw, w, d) in gardens)
                Border(PcCityLife.Corners(centre, yaw, w, d), m.Flowers, res, cell, 1.5f);
            Verges(roads, m);

            var maps = new float[res, res, 7];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    int k = y * res + x;
                    float u = (x + 0.5f) / res, v = (y + 0.5f) / res;
                    float steep = data.GetSteepness(u, v), h = data.GetInterpolatedHeight(u, v);
                    var world = new Vector3(x * cell, 0f, y * cell);
                    float hub = HubDistance(hubs, world);
                    float n1 = Mathf.PerlinNoise(world.x * 0.004f, world.z * 0.004f);
                    float n2 = Mathf.PerlinNoise(world.x * 0.03f + 17f, world.z * 0.03f);
                    float built = Mathf.Clamp01(m.Built[k] / 2.5f);

                    // Wooded hills: away from buildings, on slopes or ridges, in large noise patches.
                    float forest = Mathf.Clamp01((1f - built * 3f) * Mathf.InverseLerp(0.42f, 0.62f, n1 + steep * 0.008f) *
                                                 Mathf.InverseLerp(500f, 1100f, hub));
                    // Terraces: contour bands on open, moderate slopes outside the built-up area.
                    float terrace = 0f;
                    if (forest < 0.5f && built < 0.3f && steep > 7f && steep < 28f)
                    {
                        float band = Mathf.Repeat(h / 3.2f + n2 * 0.4f, 1f);
                        terrace = (band < 0.16f ? 1f : 0f) * Mathf.InverseLerp(7f, 12f, steep);
                    }
                    m.Forest[k] = forest;
                    m.Terrace[k] = terrace;
                    m.Farm[k] = forest < 0.5f && built < 0.3f && steep > 7f && steep < 28f && hub > 900f && !m.Lawn[k];

                    // Flower beds edging the open ground next to paths and buildings, in runs, around the hubs and in
                    // parks and gardens: the first metres of every green space read as planted, not left over.
                    if (!m.Paved[k] && !m.Market[k] && !m.Sport[k] && m.Edge[k] < cell * 1.5f && (m.Lawn[k] || hub < 900f) &&
                        Mathf.PerlinNoise(world.x * 0.06f + 3f, world.z * 0.06f + 11f) > 0.52f)
                        m.Flowers[k] = true;

                    // Manicured lawn near the hubs, meadow further out: Kigali is green and clean.
                    float lawnNear = Mathf.InverseLerp(1800f, 600f, hub);
                    float w0 = Mathf.Lerp(0.35f, 1.2f, lawnNear) * (0.8f + n2 * 0.4f);
                    float w1 = Mathf.Lerp(1f, 0.25f, lawnNear) * (0.6f + n1 * 0.8f);
                    float w2 = forest * 2.5f;
                    float w3 = Mathf.InverseLerp(30f, 40f, steep) * 2f;
                    float w4 = terrace * 0.7f;   // terraces stay green: a soil edge, not a bare band
                    float w5 = 0f;
                    float w6 = built * 0.25f;
                    if (m.Lawn[k] || m.Verge[k]) { w0 = 3f; w1 = 0.3f; w2 = 0f; w3 = 0f; w4 = 0f; w6 = 0f; }
                    if (m.Flowers[k]) w4 = Mathf.Max(w4, 0.6f);
                    if (m.Market[k]) w5 = 4f;
                    float sum = w0 + w1 + w2 + w3 + w4 + w5 + w6;
                    maps[y, x, Lawn] = w0 / sum;
                    maps[y, x, Meadow] = w1 / sum;
                    maps[y, x, ForestFloor] = w2 / sum;
                    maps[y, x, RedEarth] = w3 / sum;
                    maps[y, x, Soil] = w4 / sum;
                    maps[y, x, MarketSand] = w5 / sum;
                    maps[y, x, Yard] = w6 / sum;
                }
            data.SetAlphamaps(0, 0, maps);
            return m;
        }

        static float HubDistance(Vector3[] hubs, Vector3 p)
        {
            float best = float.MaxValue;
            foreach (var h in hubs)
                best = Mathf.Min(best, Vector2.Distance(new Vector2(h.x, h.z), new Vector2(p.x, p.z)));
            return best;
        }

        // Distance from paving (two-pass chamfer) and the size of each open patch, for the planting plan.
        static void OpenSpace(Masks m)
        {
            int res = m.Res, n = res * res;
            float cell = m.Cell, diag = cell * 1.41421f;
            var blocked = new bool[n];
            for (int k = 0; k < n; k++)
                blocked[k] = m.Paved[k] || m.Market[k];

            var d = new float[n];
            for (int k = 0; k < n; k++)
                d[k] = blocked[k] ? 0f : 1e9f;
            void Relax(int k, int x, int y, float step)
            {
                if (x >= 0 && y >= 0 && x < res && y < res)
                    d[k] = Mathf.Min(d[k], d[y * res + x] + step);
            }
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    int k = y * res + x;
                    Relax(k, x - 1, y, cell); Relax(k, x - 1, y - 1, diag); Relax(k, x, y - 1, cell); Relax(k, x + 1, y - 1, diag);
                }
            for (int y = res - 1; y >= 0; y--)
                for (int x = res - 1; x >= 0; x--)
                {
                    int k = y * res + x;
                    Relax(k, x + 1, y, cell); Relax(k, x + 1, y + 1, diag); Relax(k, x, y + 1, cell); Relax(k, x - 1, y + 1, diag);
                }
            m.Edge = d;

            // Connected open patches (4-neighbour flood fill).
            m.Area = new float[n];
            var label = new int[n];
            var queue = new int[n];
            var members = new List<int>();
            int next = 0;
            for (int s = 0; s < n; s++)
            {
                if (blocked[s] || label[s] != 0)
                    continue;
                next++;
                int head = 0, tail = 0, deepest = s;
                queue[tail++] = s;
                label[s] = next;
                members.Clear();
                while (head < tail)
                {
                    int k = queue[head++];
                    members.Add(k);
                    if (d[k] > d[deepest])
                        deepest = k;
                    int x = k % res, y = k / res;
                    void Visit(int nx, int ny)
                    {
                        if (nx < 0 || ny < 0 || nx >= res || ny >= res)
                            return;
                        int q = ny * res + nx;
                        if (blocked[q] || label[q] != 0)
                            return;
                        label[q] = next;
                        queue[tail++] = q;
                    }
                    Visit(x + 1, y); Visit(x - 1, y); Visit(x, y + 1); Visit(x, y - 1);
                }
                float area = members.Count * cell * cell;
                foreach (int k in members)
                    m.Area[k] = area;
                if (area < SmallPatch)
                    m.Pockets.Add(new Vector3((deepest % res + 0.5f) * cell, area, (deepest / res + 0.5f) * cell));
            }
        }

        const float SmallPatch = 250f;

        static string Diffuse(int n) => $"{Adg}/ground{n}/ground{n}_Diffuse.tga";
        static string Normal(int n) => $"{Adg}/ground{n}/ground{n}_Normal.tga";

        static TerrainLayer Layer(string outDir, string name, string diffuse, string normal, float tile)
        {
            string path = $"{outDir}/TL_{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(diffuse);
            layer.normalMapTexture = normal != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normal) : null;
            layer.normalScale = 0.8f;
            layer.tileSize = new Vector2(tile, tile);
            // Constant smoothness: by default URP reads it from the diffuse alpha, and these textures are opaque,
            // which turns the ground into a mirror.
            layer.smoothnessSource = TerrainLayerSmoothnessSource.Constant;
            layer.smoothness = 0.04f;
            layer.metallic = 0f;
            if (layer.diffuseTexture == null)
                Debug.LogWarning($"[Greenery] Missing ground texture {diffuse}.");
            EditorUtility.SetDirty(layer);
            return layer;
        }

        // A band of the given width just inside a polygon's edge.
        static void Border(Vector2[] poly, bool[] mask, int res, float cell, float width)
        {
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
                Vector2 centre = Vector2.zero;
                foreach (var q in poly) centre += q;
                centre /= poly.Length;
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / (cell * 0.5f)));
                for (int s = 0; s <= steps; s++)
                {
                    var p = Vector2.Lerp(a, b, s / (float)steps);
                    var inward = (centre - p).normalized;
                    for (float o = 0.5f; o <= width; o += cell * 0.5f)
                        Mark(mask, res, cell, p + inward * o);
                }
            }
        }

        static void Mark(bool[] mask, int res, float cell, Vector2 p)
        {
            int x = (int)(p.x / cell), y = (int)(p.y / cell);
            if (x >= 0 && y >= 0 && x < res && y < res)
                mask[y * res + x] = true;
        }

        // Carriageway, kerbs and footpaths plus a margin, so grass never pokes through the road surface.
        static void PavedRoads(DistrictImporter.Road[] roads, Masks m)
        {
            foreach (var road in roads)
            {
                if (road.pts == null || road.pts.Length < 6 || !DistrictImporter.RoadWidths.TryGetValue(road.highway, out float width))
                    continue;
                float reach = width * 0.5f + (PcRoadBuilder.WithFootpaths.Contains(road.highway) ? 3.1f : 0.3f) + 1.5f;
                int r = Mathf.CeilToInt(reach / m.Cell);
                foreach (var p in PcRoadBuilder.ResampleLine(road.pts, 2f))
                {
                    int cx = (int)(p.x / m.Cell), cz = (int)(p.z / m.Cell);
                    for (int z = cz - r; z <= cz + r; z++)
                        for (int x = cx - r; x <= cx + r; x++)
                        {
                            if (x < 0 || z < 0 || x >= m.Res || z >= m.Res)
                                continue;
                            float dx = (x + 0.5f) * m.Cell - p.x, dz = (z + 0.5f) * m.Cell - p.z;
                            if (dx * dx + dz * dz <= reach * reach)
                                m.Paved[z * m.Res + x] = true;
                        }
                }
            }
        }

        // Lawn verges beyond the footpaths of main roads, with runs of flower beds along the outer edge.
        static void Verges(DistrictImporter.Road[] roads, Masks m)
        {
            foreach (var road in roads)
            {
                if (road.pts == null || road.pts.Length < 6 || !PcRoadBuilder.WithFootpaths.Contains(road.highway) ||
                    !DistrictImporter.RoadWidths.TryGetValue(road.highway, out float width))
                    continue;
                var line = PcRoadBuilder.ResampleLine(road.pts);
                float half = width * 0.5f;
                bool oneway = road.oneway == "yes";
                for (int i = 0; i < line.Count; i++)
                {
                    var dir = line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)];
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-4f)
                        continue;
                    dir.Normalize();
                    var side = new Vector3(dir.z, 0f, -dir.x);
                    for (float o = half + 3.2f; o <= half + 8f; o += m.Cell * 0.5f)
                    {
                        var r = line[i] + side * o;
                        Mark(m.Verge, m.Res, m.Cell, new Vector2(r.x, r.z));
                        if (!oneway)
                        {
                            var l = line[i] - side * o;
                            Mark(m.Verge, m.Res, m.Cell, new Vector2(l.x, l.z));
                        }
                    }
                    // (One-way medians are a raised grass strip mesh, so their flowers would be buried; none there.)
                    if (!oneway && PcRoadBuilder.Major.Contains(road.highway) && i % 6 < 3)
                    {
                        // Flower beds along the outer edge of the verge, in short runs.
                        var r = line[i] + side * (half + 6.5f);
                        var l = line[i] - side * (half + 6.5f);
                        Mark(m.Flowers, m.Res, m.Cell, new Vector2(r.x, r.z));
                        Mark(m.Flowers, m.Res, m.Cell, new Vector2(l.x, l.z));
                    }
                }
            }
        }

        // ---------- Grass and flowers ----------

        // Grass on all open ground: everywhere except roads, footpaths, buildings, markets and bare steep banks.
        public static void AddDetails(Terrain terrain, Masks m)
        {
            var data = terrain.terrainData;
            var protos = new List<DetailPrototype>
            {
                Detail("grass02", 0.3f, 0.5f, 0.6f, 1.0f, new Color(0.45f, 0.68f, 0.26f), new Color(0.58f, 0.66f, 0.30f), 2.2f),
                Detail("grass01", 0.6f, 1.0f, 0.6f, 1.1f, new Color(0.50f, 0.66f, 0.28f), new Color(0.68f, 0.67f, 0.36f), 1.2f),
            };
            foreach (var f in Flowers)
                protos.Add(Detail(f, 0.35f, 0.6f, 0.4f, 0.7f, Color.white, new Color(0.92f, 0.9f, 0.85f), 1.4f));
            data.detailPrototypes = protos.ToArray();
            data.SetDetailScatterMode(DetailScatterMode.CoverageMode);
            // Small patches: ~30 grass quads per 3.9 m cell, so 32 cells per patch overflows a grass mesh and nothing draws.
            data.SetDetailResolution(m.Res, 8);

            var layers = new int[protos.Count][,];
            for (int i = 0; i < layers.Length; i++)
                layers[i] = new int[m.Res, m.Res];
            var splat = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            for (int y = 0; y < m.Res; y++)
                for (int x = 0; x < m.Res; x++)
                {
                    int k = y * m.Res + x;
                    var world = new Vector3((x + 0.5f) * m.Cell, 0f, (y + 0.5f) * m.Cell);
                    if (m.Market[k] || m.Paved[k])
                        continue;
                    float lawn = splat[y, x, Lawn], meadow = splat[y, x, Meadow], red = splat[y, x, RedEarth];
                    if (red > 0.6f)
                        continue;
                    float n = Mathf.PerlinNoise(world.x * 0.08f, world.z * 0.08f);
                    // Every open cell is at least half covered; lawns and meadows fully.
                    layers[LawnGrass][y, x] = Mathf.RoundToInt(255f * Mathf.Clamp(lawn * 1.3f + meadow * 0.8f + 0.25f, 0.5f, 1f));
                    layers[TallGrass][y, x] = m.Lawn[k] || m.Verge[k]
                        ? 0
                        : Mathf.RoundToInt(255f * Mathf.Clamp01(meadow * 0.8f + m.Forest[k] * 0.4f + m.Terrace[k] * 0.3f) * (0.4f + n * 0.6f));
                    if (m.Flowers[k])
                    {
                        // Each bed is one or two colours, chosen by position, like planted municipal beds.
                        int a = Mathf.FloorToInt(Mathf.PerlinNoise(world.x * 0.02f, world.z * 0.02f) * Flowers.Length) % Flowers.Length;
                        int b = (a + 2) % Flowers.Length;
                        layers[FirstFlower + a][y, x] = 230;
                        layers[FirstFlower + b][y, x] = n > 0.5f ? 140 : 0;
                    }
                    else if (meadow > 0.3f && n > 0.72f)
                    {
                        // Wildflowers sprinkled through the meadows.
                        int a = Mathf.FloorToInt(n * 997f) % Flowers.Length;
                        layers[FirstFlower + a][y, x] = 60;
                    }
                }
            var covered = new int[layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                data.SetDetailLayer(0, 0, i, layers[i]);
                foreach (int v in layers[i])
                    if (v > 0)
                        covered[i]++;
            }
            float km2 = m.Cell * m.Cell / 1e6f;
            Debug.Log($"[Greenery] Grass: lawn {covered[LawnGrass] * km2:F1} km², tall {covered[TallGrass] * km2:F1} km², " +
                      $"flowers {covered.Skip(FirstFlower).Sum() * km2:F2} km² (terrain {data.size.x * data.size.z / 1e6f:F0} km²).");
            if (covered[LawnGrass] == 0 || data.detailPrototypes.Any(p => p.prototypeTexture == null))
                Debug.LogError("[Greenery] Grass did not get placed: check the Grass Flowers pack textures.");
            data.wavingGrassStrength = 0.45f;
            data.wavingGrassAmount = 0.45f;
            data.wavingGrassSpeed = 0.5f;
            data.wavingGrassTint = new Color(0.9f, 0.95f, 0.85f);
            terrain.detailObjectDistance = 250f;
            terrain.detailObjectDensity = 1f;
        }

        static DetailPrototype Detail(string texture, float minH, float maxH, float minW, float maxW, Color healthy, Color dry, float density)
        {
            string path = $"{Alp}/GrassFlowers/{texture}.tga";
            if (AssetImporter.GetAtPath(path) is TextureImporter ti && (!ti.alphaIsTransparency || ti.wrapMode != TextureWrapMode.Clamp || ti.maxTextureSize > 1024))
            {
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.maxTextureSize = 1024;
                ti.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
                Debug.LogWarning($"[Greenery] Missing grass texture {path}.");
            return new DetailPrototype
            {
                prototypeTexture = tex, renderMode = DetailRenderMode.Grass, usePrototypeMesh = false,
                minHeight = minH, maxHeight = maxH, minWidth = minW, maxWidth = maxW, noiseSpread = 0.4f,
                healthyColor = healthy, dryColor = dry, density = density, useDensityScaling = true,
                positionJitter = 80f,   // percent; breaks up the detail grid so grass does not grow in rows
            };
        }

        // ---------- Conifers ----------

        // Terrain trees: Unity handles LOD0 -> LOD1 -> billboard per tree, and the CTI shaders sway with the wind.
        // Returns trunk positions near the hubs for City Life collision.
        public static List<Vector3> PlantConifers(Terrain terrain, PcCityLife.Occupancy grid, DistrictImporter.Road[] roads,
                                                   PcCityLife.Place[] places, Masks m, Vector3[] hubs, System.Random rng)
        {
            var data = terrain.terrainData;
            var prefabs = new[] { "Small", "Medium", "Tall" }
                .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>($"{Conifers}/PF Conifer {n} BOTD URP.prefab"))
                .Where(p => p != null).ToArray();
            var trunks = new List<Vector3>();
            if (prefabs.Length == 0)
            {
                Debug.LogWarning("[Greenery] Conifers [BOTD] URP prefabs not found; no conifers planted.");
                return trunks;
            }
            data.treePrototypes = prefabs.Select(p => new TreePrototype { prefab = p }).ToArray();
            var trees = new List<TreeInstance>();
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float Hub(Vector3 p) => HubDistance(hubs, p);

            // Each tree claims a crown radius; nothing is planted inside another tree's crown.
            const float HashCell = 8f;
            var crowns = new Dictionary<long, List<Vector3>>();   // x, crown radius, z
            float maxCrown = 0f;
            long HashKey(int x, int z) => ((long)x << 32) ^ (uint)z;
            bool Room(Vector3 p, float r)
            {
                int span = Mathf.CeilToInt((r + maxCrown) / HashCell);
                int cx = Mathf.FloorToInt(p.x / HashCell), cz = Mathf.FloorToInt(p.z / HashCell);
                for (int z = cz - span; z <= cz + span; z++)
                    for (int x = cx - span; x <= cx + span; x++)
                        if (crowns.TryGetValue(HashKey(x, z), out var list))
                            foreach (var q in list)
                            {
                                float dx = q.x - p.x, dz = q.z - p.z, min = q.y + r;
                                if (dx * dx + dz * dz < min * min)
                                    return false;
                            }
                return true;
            }

            void Plant(Vector3 p, int proto, float scale, float crown, float tint)
            {
                proto = Mathf.Clamp(proto, 0, prefabs.Length - 1);
                var size = data.size;
                if (p.x < 1f || p.z < 1f || p.x > size.x - 1f || p.z > size.z - 1f || !PlayArea.Contains(p, 200f))
                    return;   // only around the play area: past it the haze hides everything
                trees.Add(new TreeInstance
                {
                    position = new Vector3(p.x / size.x, terrain.SampleHeight(p) / size.y, p.z / size.z),
                    prototypeIndex = proto, widthScale = scale * R(0.9f, 1.1f), heightScale = scale,
                    rotation = R(0f, Mathf.PI * 2f), color = new Color(tint, tint, tint), lightmapColor = Color.white,
                });
                long key = HashKey(Mathf.FloorToInt(p.x / HashCell), Mathf.FloorToInt(p.z / HashCell));
                if (!crowns.TryGetValue(key, out var list))
                    crowns[key] = list = new List<Vector3>();
                list.Add(new Vector3(p.x, crown, p.z));
                maxCrown = Mathf.Max(maxCrown, crown);
                grid.Mark(p, 1.5f);
                if (Hub(p) < 1900f)
                    trunks.Add(p);
            }

            // Avenues: a row on each verge of the main roads near the hubs.
            foreach (var road in roads)
            {
                if (road.pts == null || road.pts.Length < 6 || !PcRoadBuilder.Major.Contains(road.highway) ||
                    !DistrictImporter.RoadWidths.TryGetValue(road.highway, out float width))
                    continue;
                var line = PcRoadBuilder.ResampleLine(road.pts);
                float half = width * 0.5f;
                float along = 0f;
                for (int i = 1; i < line.Count; i++)
                {
                    along += Vector3.Distance(line[i - 1], line[i]);
                    if (along < 16f)
                        continue;
                    along = 0f;
                    var dir = (line[i] - line[i - 1]).normalized;
                    var side = new Vector3(dir.z, 0f, -dir.x);
                    foreach (float s in new[] { 1f, -1f })
                    {
                        var p = line[i] + side * s * (half + 8.5f);
                        if (Hub(p) > 2200f || !grid.Free(p, 2f) || !Room(p, 2.5f))
                            continue;
                        Plant(p, rng.NextDouble() < 0.6 ? 0 : 1, R(0.55f, 0.75f), 2.5f, R(0.88f, 1f));
                    }
                }
            }
            int avenue = trees.Count;

            // ---------- Planting plan for all open ground ----------
            //   Edge ring (first cell beside paths and walls): lawn and flower beds; keeps paths and views clear.
            //   Fringe (to ~10 m in): clumps of small trees with lawn between them.
            //   Core: a closed stand rising toward the middle of each patch, small trees outside, tall ones inside.
            //   Parks and gardens: open lawn glades ringed by groves. Pitches and stadiums: no trees.
            //   Hillside farms: trees only as hedgerows along the terrace contours; the fields stay grass.
            //   Small pockets: one specimen tree at the deepest point.
            int Cell(Vector3 p) =>
                Mathf.Clamp((int)(p.z / m.Cell), 0, m.Res - 1) * m.Res + Mathf.Clamp((int)(p.x / m.Cell), 0, m.Res - 1);
            bool Plantable(int k) =>
                !m.Paved[k] && !m.Market[k] && !m.Sport[k] && m.Area[k] >= SmallPatch && m.Edge[k] >= m.Cell * 1.5f &&
                !(m.Farm[k] && m.Forest[k] < 0.35f && m.Terrace[k] < 0.5f);

            // Spacing per distance band, sized so the whole terrain fits the tree budget: densest where City Life
            // is played, looser on the far hills where billboards read as continuous forest anyway.
            const int MaxTrees = 8000;   // Quest budget, play area only
            const float Fill = 0.7f;   // share of plantable ground under canopy once glades and clumps are left open
            float nearArea = 0f, midArea = 0f, farArea = 0f, cellArea = m.Cell * m.Cell;
            for (int k = 0; k < m.Res * m.Res; k++)
            {
                if (!Plantable(k))
                    continue;
                float hub = Hub(new Vector3((k % m.Res + 0.5f) * m.Cell, 0f, (k / m.Res + 0.5f) * m.Cell));
                if (hub < 1200f) nearArea += cellArea;
                else if (hub < 2500f) midArea += cellArea;
                else farArea += cellArea;
            }
            float budget = MaxTrees - trees.Count - m.Pockets.Count;
            float sNear = 5.5f, sMid = 7f;
            float nearMid = (nearArea / (sNear * sNear) + midArea / (sMid * sMid)) * Fill;
            if (nearMid > budget * 0.8f)
            {
                float k = Mathf.Sqrt(nearMid / (budget * 0.8f));
                sNear *= k;
                sMid *= k;
                nearMid = budget * 0.8f;
            }
            float sFar = Mathf.Max(8f, Mathf.Sqrt(farArea * Fill / Mathf.Max(1f, budget - nearMid)));

            // Specimen trees first, so the open-ground pass flows around them.
            int specimens = 0;
            foreach (var pocket in m.Pockets)
            {
                if (pocket.y < 40f)
                    continue;   // too small: grass and flowers only
                var p = new Vector3(pocket.x, 0f, pocket.z);
                if (m.Sport[Cell(p)] || grid.Blocked(p))
                    continue;
                bool larger = pocket.y > 120f;
                float crown = larger ? 3.2f : 2.4f;
                if (!Room(p, crown))
                    continue;
                Plant(p, larger ? 1 : 0, larger ? R(0.8f, 0.95f) : R(0.6f, 0.75f), crown, R(0.9f, 1f));
                specimens++;
            }

            const float Step = 3.5f;
            int hedges = 0;
            for (float z = Step * 0.5f; z < data.size.z && trees.Count < MaxTrees; z += Step)
                for (float x = Step * 0.5f; x < data.size.x && trees.Count < MaxTrees; x += Step)
                {
                    var p = new Vector3(x + R(-1.4f, 1.4f), 0f, z + R(-1.4f, 1.4f));
                    int k = Cell(p);
                    if (!Plantable(k) || grid.Blocked(p))
                        continue;
                    float hub = Hub(p);
                    float spacing = hub < 1200f ? sNear : hub < 2500f ? sMid : sFar;
                    bool forest = m.Forest[k] >= 0.35f;
                    bool hedge = m.Farm[k] && !forest;
                    float depth = Mathf.InverseLerp(m.Cell * 1.5f, 30f, m.Edge[k]);
                    if (forest)
                        depth = Mathf.Max(depth, 0.7f);
                    if (!forest && !hedge)
                    {
                        if (m.Lawn[k] && Mathf.PerlinNoise(p.x * 0.015f + 31f, p.z * 0.015f + 7f) < 0.4f)
                            continue;   // glade
                        if (depth < 0.25f && Mathf.PerlinNoise(p.x * 0.09f, p.z * 0.09f) < 0.5f)
                            continue;   // lawn between fringe clumps
                    }

                    int proto;
                    double roll = rng.NextDouble();
                    if (hedge || depth < 0.3f) proto = roll < 0.8 ? 0 : 1;
                    else if (depth < 0.65f) proto = roll < 0.7 ? 1 : roll < 0.85 ? 0 : 2;
                    else proto = roll < 0.65 ? 2 : 1;
                    float scale = hedge ? R(0.5f, 0.65f) : Mathf.Lerp(0.6f, 1.15f, depth) * R(0.88f, 1.08f);
                    float crown = spacing * 0.5f * (proto == 0 ? 0.8f : proto == 1 ? 1f : 1.2f) * (hedge ? 0.8f : 1f);
                    if (!Room(p, crown))
                        continue;
                    // Broad patches of slightly different green so stands read as groups, not noise.
                    float tint = Mathf.Lerp(0.8f, 1f, Mathf.PerlinNoise(p.x * 0.015f + 50f, p.z * 0.015f)) * R(0.95f, 1f);
                    Plant(p, proto, scale, crown, tint);
                    if (hedge)
                        hedges++;
                }
            if (trees.Count >= MaxTrees)
                Debug.LogWarning($"[Greenery] Tree budget of {MaxTrees} reached; the last rows of the terrain may be thinner.");
            Debug.Log($"[Greenery] Planting plan: {avenue} avenue, {specimens} specimen, {hedges} hedgerow, " +
                      $"{trees.Count - avenue - specimens - hedges} grove and woodland trees. Spacing near {sNear:F1} m, " +
                      $"mid {sMid:F1} m, far {sFar:F1} m over {(nearArea + midArea + farArea) / 1e6f:F1} km² of open ground.");

            data.SetTreeInstances(trees.ToArray(), true);
            terrain.treeDistance = 900f;   // matches the camera clip; the haze hides the edge
            terrain.treeBillboardDistance = 120f;
            terrain.treeCrossFadeLength = 40f;
            terrain.treeMaximumFullLODCount = 150;
            terrain.drawTreesAndFoliage = true;
            Debug.Log($"[Greenery] {trees.Count} conifers planted ({trunks.Count} near the hubs).");
            return trunks;
        }

        // ---------- Wind and weather ----------

        public static WeatherCycle AddWeather(Terrain terrain, DayNightCycle cycle, string outDir)
        {
            var host = new GameObject("Weather (wind, showers)");
            WindZone wind;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WindPrefab);
            if (prefab != null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.SetParent(host.transform, false);
                wind = go.GetComponent<WindZone>();
            }
            else
            {
                Debug.LogWarning("[Greenery] CTI Windzone prefab not found; trees will not sway.");
                wind = new GameObject("Wind").AddComponent<WindZone>();
                wind.transform.SetParent(host.transform, false);
            }
            wind.mode = WindZoneMode.Directional;

            var rainGo = new GameObject("Rain");
            rainGo.transform.SetParent(host.transform, false);
            var rain = rainGo.AddComponent<ParticleSystem>();
            rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = rain.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 1.4f;
            main.startSpeed = 0f;
            main.startSize = 0.025f;
            main.maxParticles = 12000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            var velocity = rain.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-3f, -1f);
            velocity.y = new ParticleSystem.MinMaxCurve(-24f, -20f);
            velocity.z = new ParticleSystem.MinMaxCurve(-1f, 1f);
            var shape = rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(70f, 1f, 70f);
            var emission = rain.emission;
            emission.rateOverTime = 0f;
            var renderer = rainGo.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.035f;
            renderer.lengthScale = 2f;
            renderer.sharedMaterial = RainMaterial(outDir);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var weather = host.AddComponent<WeatherCycle>();
            weather.Configure(wind, terrain, cycle, rain);
            return weather;
        }

        static Material RainMaterial(string outDir)
        {
            string path = $"{outDir}/PC_Rain.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetColor("_BaseColor", new Color(0.78f, 0.83f, 0.9f, 0.35f));
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
