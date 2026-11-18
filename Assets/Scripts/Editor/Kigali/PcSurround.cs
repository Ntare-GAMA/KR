using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KR.Editor.GIS;
using KR.Editor.Util;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KR.Editor.Kigali
{
    // The land around the PC map: eight terrain tiles (one map-size tile on each side and corner) built from the real
    // Copernicus DEM (Tools/gis/make_surround.py), so the hills carry on to the horizon instead of ending in a void.
    //   seams: each tile's edge takes the map's final heights (after road grading) and eases back to the DEM over
    //     Blend metres;
    //   ground: the same layers and weighting the map uses on its outskirts (meadow, wooded hills, terraced slopes,
    //     red earth on steep banks), driven by the same world-space noise so the paint flows across the seam;
    //   trees: forest and hedgerows in the first TreeBand metres, where they are still drawn; the haze covers the rest.
    // Roads are clipped to the map (ClipToMap): the OSM data runs a few kilometres past the edge with no city around it.
    internal static class PcSurround
    {
        const string GroupName = "Surrounding land (real elevation)";
        const float Blend = 350f;       // m over which the seam correction fades into the DEM
        const float SmoothFrom = 60f;   // m from the seam where the along-edge smoothed correction takes over
        const float TreeBand = 1800f;   // matches the map's treeDistance
        const int MaxTrees = 140000;
        const int AlphaRes = 512;

        [Serializable]
        sealed class Info
        {
            public int resolution;
            public float size_m, origin_x, origin_z, elevation_min_m, elevation_span_m, district_elevation_min_m;
        }

        // ---------- Roads ----------

        // Splits every road into the runs that lie inside the map (inset from the edge so ribbons stay on terrain).
        public static DistrictImporter.Road[] ClipToMap(DistrictImporter.Road[] roads, float size, float inset = 3f)
        {
            float lo = inset, hi = size - inset;
            var result = new List<DistrictImporter.Road>(roads.Length);
            int clipped = 0;
            foreach (var road in roads)
            {
                var pts = road.pts;
                if (pts == null || pts.Length < 6)
                    continue;
                bool inside = true;
                for (int i = 0; i < pts.Length && inside; i += 3)
                    inside = pts[i] >= lo && pts[i] <= hi && pts[i + 2] >= lo && pts[i + 2] <= hi;
                if (inside)
                {
                    result.Add(road);
                    continue;
                }
                clipped++;
                var run = new List<float>();
                void Flush()
                {
                    if (run.Count >= 6)
                        result.Add(new DistrictImporter.Road { highway = road.highway, name = road.name, oneway = road.oneway, pts = run.ToArray() });
                    run = new List<float>();
                }
                for (int i = 0; i + 5 < pts.Length; i += 3)
                {
                    var a = new Vector3(pts[i], pts[i + 1], pts[i + 2]);
                    var b = new Vector3(pts[i + 3], pts[i + 4], pts[i + 5]);
                    if (!Clip(a, b, lo, hi, out float t0, out float t1))
                    {
                        Flush();
                        continue;
                    }
                    if (t0 > 0f || run.Count == 0)
                    {
                        Flush();
                        Append(run, Vector3.Lerp(a, b, t0));
                    }
                    Append(run, Vector3.Lerp(a, b, t1));
                    if (t1 < 1f)
                        Flush();
                }
                Flush();
            }
            Debug.Log($"[Surround] {clipped} roads ran past the map edge and were clipped to it.");
            return result.ToArray();
        }

        static void Append(List<float> run, Vector3 p)
        {
            run.Add(p.x);
            run.Add(p.y);
            run.Add(p.z);
        }

        // Liang-Barsky clip of segment a-b (x and z) to the square [lo, hi].
        static bool Clip(Vector3 a, Vector3 b, float lo, float hi, out float t0, out float t1)
        {
            t0 = 0f;
            t1 = 1f;
            float dx = b.x - a.x, dz = b.z - a.z;
            float[] p = { -dx, dx, -dz, dz };
            float[] q = { a.x - lo, hi - a.x, a.z - lo, hi - a.z };
            for (int i = 0; i < 4; i++)
            {
                if (Mathf.Approximately(p[i], 0f))
                {
                    if (q[i] < 0f)
                        return false;
                    continue;
                }
                float r = q[i] / p[i];
                if (p[i] < 0f) t0 = Mathf.Max(t0, r);
                else t1 = Mathf.Min(t1, r);
                if (t0 > t1)
                    return false;
            }
            return t1 - t0 > 1e-4f;
        }

        // ---------- Terrain ----------

        public static Transform Build(Terrain map, string src, string outDir, Transform parent, string district)
        {
            string infoPath = Path.Combine(src, "surround.json");
            if (!File.Exists(infoPath))
            {
                Debug.LogWarning($"[Surround] No {infoPath}. Run Tools/gis/make_surround.py <SourceArt/GIS> {district} to fill the land around the map.");
                return null;
            }
            var info = JsonUtility.FromJson<Info>(File.ReadAllText(infoPath));
            var mapData = map.terrainData;
            int r = mapData.heightmapResolution, n = info.resolution;
            float size = mapData.size.x;
            if (n != (r - 1) * 3 + 1 || !Mathf.Approximately(info.size_m, size * 3f))
            {
                Debug.LogError($"[Surround] surround.json ({n} samples, {info.size_m} m) does not match the map ({r} samples, {size} m). Re-run make_surround.py.");
                return null;
            }
            byte[] raw = File.ReadAllBytes(Path.Combine(src, $"surround_{n}.raw"));
            if (raw.Length != n * n * 2)
            {
                Debug.LogError($"[Surround] surround_{n}.raw is {raw.Length} bytes, expected {n * n * 2}.");
                return null;
            }

            // Heights in map-local metres (the map's y = 0 is its lowest DEM sample).
            Vector3 origin = map.GetPosition();
            float baseY = origin.y;
            float Raw(int gx, int gz)
            {
                int i = (gz * n + gx) * 2;
                return info.elevation_min_m + (raw[i] | (raw[i + 1] << 8)) / 65535f * info.elevation_span_m - info.district_elevation_min_m;
            }
            var mapHeights = mapData.GetHeights(0, 0, r, r);
            float Map(int ix, int iz) => mapHeights[iz, ix] * mapData.size.y;

            // Seam correction along each map edge (south, north, west, east), exact and smoothed along the edge.
            int o = r - 1;
            float[][] exact = new float[4][], smooth = new float[4][];
            for (int e = 0; e < 4; e++)
            {
                exact[e] = new float[r];
                for (int i = 0; i < r; i++)
                {
                    int ix = e < 2 ? i : e == 2 ? 0 : r - 1;
                    int iz = e >= 2 ? i : e == 0 ? 0 : r - 1;
                    exact[e][i] = Map(ix, iz) - Raw(o + ix, o + iz);
                }
                smooth[e] = BoxBlur(exact[e], 12);
            }

            EditorAssetUtil.EnsureFolder($"{outDir}/Surround");
            var group = new GameObject(GroupName).transform;
            group.SetParent(parent, false);
            var tiles = new Dictionary<Vector2Int, Terrain> { [Vector2Int.zero] = map };
            var candidates = new List<(TerrainData data, List<TreeInstance> trees)>();
            float step = size / (r - 1);

            for (int tz = -1; tz <= 1; tz++)
                for (int tx = -1; tx <= 1; tx++)
                {
                    if (tx == 0 && tz == 0)
                        continue;
                    var heights = new float[r, r];
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int iz = 0; iz < r; iz++)
                        for (int ix = 0; ix < r; ix++)
                        {
                            int gx = (tx + 1) * o + ix, gz = (tz + 1) * o + iz;
                            int cx = Mathf.Clamp(gx, o, 2 * o), cz = Mathf.Clamp(gz, o, 2 * o);
                            float d = new Vector2(gx - cx, gz - cz).magnitude * step;
                            int e = gz < o ? 0 : gz > 2 * o ? 1 : gx < o ? 2 : 3;
                            int along = e < 2 ? cx - o : cz - o;
                            float delta = Mathf.Lerp(exact[e][along], smooth[e][along], Mathf.SmoothStep(0f, 1f, d / SmoothFrom));
                            float h = Raw(gx, gz) + delta * (1f - Mathf.SmoothStep(0f, 1f, d / Blend));
                            heights[iz, ix] = h;
                            lo = Mathf.Min(lo, h);
                            hi = Mathf.Max(hi, h);
                        }
                    float span = Mathf.Max(hi - lo, 1f);
                    for (int iz = 0; iz < r; iz++)
                        for (int ix = 0; ix < r; ix++)
                            heights[iz, ix] = (heights[iz, ix] - lo) / span;

                    var data = new TerrainData { heightmapResolution = r };
                    data.size = new Vector3(size, span, size);
                    data.SetHeights(0, 0, heights);
                    var offset = new Vector3(tx * size, 0f, tz * size);
                    Paint(data, mapData.terrainLayers, offset, lo);
                    string path = $"{outDir}/Surround/{district}_Surround_{tx + 1}{tz + 1}.asset";
                    AssetDatabase.DeleteAsset(path);
                    AssetDatabase.CreateAsset(data, path);

                    var go = Terrain.CreateTerrainGameObject(data);
                    go.name = $"Surround {Compass(tx, tz)}";
                    go.layer = map.gameObject.layer;
                    go.transform.SetParent(group, false);
                    go.transform.position = new Vector3(origin.x + offset.x, baseY + lo, origin.z + offset.z);
                    GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(map.gameObject));
                    var t = go.GetComponent<Terrain>();
                    CopySettings(map, t);
                    tiles[new Vector2Int(tx, tz)] = t;
                    candidates.Add((data, Woods(data, offset, lo, size, new System.Random(7919 * (tx + 2) + 31 * (tz + 2)))));
                }

            int planted = PlantTrees(mapData.treePrototypes, candidates);

            foreach (var kv in tiles)
            {
                Terrain At(int dx, int dz) => tiles.TryGetValue(kv.Key + new Vector2Int(dx, dz), out var t) ? t : null;
                kv.Value.SetNeighbors(At(-1, 0), At(0, 1), At(1, 0), At(0, -1));
            }
            Terrain.SetConnectivityDirty();

            Debug.Log($"[Surround] {tiles.Count - 1} tiles of real terrain around the map ({info.size_m / 1000f:F0} km square), " +
                      $"{planted} trees. Seam correction before blending: up to {exact.SelectMany(a => a).Max(Mathf.Abs):F1} m.");
            return group;
        }

        static string Compass(int tx, int tz) =>
            (tz > 0 ? "North" : tz < 0 ? "South" : "") + (tx > 0 ? "East" : tx < 0 ? "West" : "");

        static float[] BoxBlur(float[] a, int radius)
        {
            var result = new float[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                float sum = 0f;
                int count = 0;
                for (int j = Mathf.Max(0, i - radius); j <= Mathf.Min(a.Length - 1, i + radius); j++)
                {
                    sum += a[j];
                    count++;
                }
                result[i] = sum / count;
            }
            return result;
        }

        static void CopySettings(Terrain map, Terrain t)
        {
            t.materialTemplate = map.materialTemplate;
            t.drawInstanced = map.drawInstanced;
            t.heightmapPixelError = 4f;   // no roads out here, so coarser LOD is fine
            t.basemapDistance = map.basemapDistance;
            t.shadowCastingMode = map.shadowCastingMode;
            t.reflectionProbeUsage = map.reflectionProbeUsage;
            t.groupingID = map.groupingID;
            t.allowAutoConnect = true;
            map.allowAutoConnect = true;
            t.drawTreesAndFoliage = true;
            t.treeDistance = map.treeDistance;
            t.treeBillboardDistance = map.treeBillboardDistance;
            t.treeCrossFadeLength = map.treeCrossFadeLength;
            t.treeMaximumFullLODCount = 200;
            t.detailObjectDistance = 0f;   // grass only on the map itself
        }

        // Same weighting PcGreenery uses on the map's outskirts (no buildings, far from the hubs), in map-local
        // world coordinates so the noise is continuous across the seam.
        static void Paint(TerrainData data, TerrainLayer[] layers, Vector3 offset, float floor)
        {
            if (layers == null || layers.Length == 0)
                return;
            data.terrainLayers = layers;
            data.alphamapResolution = AlphaRes;
            int count = layers.Length;
            bool full = count >= 5;   // Lawn, Meadow, ForestFloor, RedEarth, Soil, ...
            float cell = data.size.x / AlphaRes;
            var maps = new float[AlphaRes, AlphaRes, count];
            for (int y = 0; y < AlphaRes; y++)
                for (int x = 0; x < AlphaRes; x++)
                {
                    float u = (x + 0.5f) / AlphaRes, v = (y + 0.5f) / AlphaRes;
                    float steep = data.GetSteepness(u, v);
                    var world = offset + new Vector3(x * cell, 0f, y * cell);
                    if (!full)
                    {
                        float earth = Mathf.InverseLerp(14f, 24f, steep);
                        maps[y, x, 0] = 1f - earth;
                        if (count > 1) maps[y, x, 1] = earth;
                        continue;
                    }
                    float h = data.GetInterpolatedHeight(u, v) + floor;
                    float n1 = Mathf.PerlinNoise(world.x * 0.004f, world.z * 0.004f);
                    float n2 = Mathf.PerlinNoise(world.x * 0.03f + 17f, world.z * 0.03f);
                    float forest = Forest(n1, steep);
                    float terrace = Terrace(forest, steep, h, n2);
                    float w0 = 0.35f * (0.8f + n2 * 0.4f);
                    float w1 = 0.6f + n1 * 0.8f;
                    float w2 = forest * 2.5f;
                    float w3 = Mathf.InverseLerp(30f, 40f, steep) * 2f;
                    float w4 = terrace * 0.7f;
                    float sum = w0 + w1 + w2 + w3 + w4;
                    maps[y, x, 0] = w0 / sum;
                    maps[y, x, 1] = w1 / sum;
                    maps[y, x, 2] = w2 / sum;
                    maps[y, x, 3] = w3 / sum;
                    maps[y, x, 4] = w4 / sum;
                }
            data.SetAlphamaps(0, 0, maps);
        }

        static float Forest(float n1, float steep) => Mathf.InverseLerp(0.42f, 0.62f, n1 + steep * 0.008f);

        // h is in map-local metres, as on the map, so the contour bands line up across the seam.
        static float Terrace(float forest, float steep, float h, float n2) =>
            forest < 0.5f && steep > 7f && steep < 28f && Mathf.Repeat(h / 3.2f + n2 * 0.4f, 1f) < 0.16f
                ? Mathf.InverseLerp(7f, 12f, steep) : 0f;

        // Candidate trees for one tile: closed woodland on the wooded hills, hedgerows on the terraces, thinning with
        // distance from the map.
        static List<TreeInstance> Woods(TerrainData data, Vector3 offset, float floor, float size, System.Random rng)
        {
            var trees = new List<TreeInstance>();
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            const float Step = 6f;
            for (float z = Step * 0.5f; z < size; z += Step)
                for (float x = Step * 0.5f; x < size; x += Step)
                {
                    var local = new Vector3(x + R(-2.2f, 2.2f), 0f, z + R(-2.2f, 2.2f));
                    var world = offset + local;
                    float edge = DistanceOutside(world, size);
                    if (edge > TreeBand || local.x < 1f || local.z < 1f || local.x > size - 1f || local.z > size - 1f)
                        continue;
                    float u = local.x / size, v = local.z / size;
                    float steep = data.GetSteepness(u, v);
                    float n1 = Mathf.PerlinNoise(world.x * 0.004f, world.z * 0.004f);
                    float n2 = Mathf.PerlinNoise(world.x * 0.03f + 17f, world.z * 0.03f);
                    float forest = Forest(n1, steep);
                    bool hedge = forest < 0.35f && Terrace(forest, steep, data.GetInterpolatedHeight(u, v) + floor, n2) > 0.5f;
                    if (forest < 0.35f && !hedge)
                        continue;
                    if (steep > 38f)
                        continue;   // bare red banks
                    float spacing = hedge ? 7f : Mathf.Lerp(9f, 18f, edge / TreeBand);
                    if (rng.NextDouble() > Step * Step / (spacing * spacing))
                        continue;
                    int proto = hedge ? (rng.NextDouble() < 0.8 ? 0 : 1) : (rng.NextDouble() < 0.6 ? 2 : 1);
                    float scale = hedge ? R(0.5f, 0.65f) : R(0.85f, 1.15f);
                    float tint = Mathf.Lerp(0.8f, 1f, Mathf.PerlinNoise(world.x * 0.015f + 50f, world.z * 0.015f)) * R(0.95f, 1f);
                    trees.Add(new TreeInstance
                    {
                        position = new Vector3(u, data.GetInterpolatedHeight(u, v) / data.size.y, v),
                        prototypeIndex = proto, widthScale = scale * R(0.9f, 1.1f), heightScale = scale,
                        rotation = R(0f, Mathf.PI * 2f), color = new Color(tint, tint, tint), lightmapColor = Color.white,
                    });
                }
            return trees;
        }

        static float DistanceOutside(Vector3 p, float size)
        {
            float dx = Mathf.Max(0f, -p.x, p.x - size), dz = Mathf.Max(0f, -p.z, p.z - size);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static int PlantTrees(TreePrototype[] prototypes, List<(TerrainData data, List<TreeInstance> trees)> tiles)
        {
            if (prototypes == null || prototypes.Length == 0)
                return 0;
            int total = tiles.Sum(t => t.trees.Count);
            double keep = total > MaxTrees ? (double)MaxTrees / total : 1.0;
            var rng = new System.Random(4242);
            int planted = 0;
            foreach (var (data, trees) in tiles)
            {
                data.treePrototypes = prototypes;
                var kept = trees.Where(_ => rng.NextDouble() < keep).Select(t =>
                {
                    t.prototypeIndex = Mathf.Min(t.prototypeIndex, prototypes.Length - 1);
                    return t;
                }).ToArray();
                data.SetTreeInstances(kept, true);
                planted += kept.Length;
            }
            return planted;
        }

        // ---------- Open scene ----------

        [MenuItem("Kigali Rush/Scene/Add Surrounding Land to Open Scene")]
        static void AddToOpenScene()
        {
            var map = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => t.terrainData != null && AssetDatabase.GetAssetPath(t.terrainData).EndsWith("_TerrainData.asset"))
                .OrderByDescending(t => t.terrainData.size.x).FirstOrDefault();
            if (map == null)
            {
                Debug.LogError("[Surround] No map terrain in the open scene. Open a PC scene first.");
                return;
            }
            string assetPath = AssetDatabase.GetAssetPath(map.terrainData);
            string outDir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string district = Path.GetFileName(assetPath).Replace("_TerrainData.asset", "");
            var parent = map.transform.parent;

            var old = parent != null ? parent.Find(GroupName) : null;
            if (old != null)
                UnityEngine.Object.DestroyImmediate(old.gameObject);

            try
            {
                EditorUtility.DisplayProgressBar("Surrounding land", "Terrain tiles, ground and woods", 0.3f);
                if (Build(map, Path.Combine(DistrictImporter.SourceDir, district), outDir, parent, district) == null)
                    return;
                EditorUtility.DisplayProgressBar("Surrounding land", "Trimming geometry past the map edge", 0.9f);
                TrimOutside(map, outDir);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            EditorSceneManager.SaveScene(map.gameObject.scene);
            AssetDatabase.SaveAssets();
        }

        // Removes generated geometry (roads, markings, kerbs) that hangs past the map edge in scenes built before
        // roads were clipped. Imported models are never touched; a rebuild clips the roads at the source.
        static void TrimOutside(Terrain map, string outDir)
        {
            var p0 = map.GetPosition();
            var size = map.terrainData.size;
            const float Slack = 0.5f;
            bool Inside(Vector3 w) =>
                w.x >= p0.x - Slack && w.x <= p0.x + size.x + Slack && w.z >= p0.z - Slack && w.z <= p0.z + size.z + Slack;

            int meshes = 0, triangles = 0;
            foreach (var mf in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable)
                    continue;
                string path = AssetDatabase.GetAssetPath(mesh);
                if (!string.IsNullOrEmpty(path) && !path.StartsWith(outDir + "/"))
                    continue;
                var r = mf.GetComponent<Renderer>();
                if (r != null && Inside(r.bounds.min) && Inside(r.bounds.max))
                    continue;

                var m = mf.transform.localToWorldMatrix;
                var verts = mesh.vertices;
                var keep = new bool[verts.Length];
                for (int i = 0; i < verts.Length; i++)
                    keep[i] = Inside(m.MultiplyPoint3x4(verts[i]));
                int removed = 0;
                var subs = new List<int[]>();
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    if (mesh.GetTopology(s) != MeshTopology.Triangles)
                    {
                        subs.Add(null);
                        continue;
                    }
                    var tris = mesh.GetTriangles(s);
                    var kept = new List<int>(tris.Length);
                    for (int i = 0; i + 2 < tris.Length; i += 3)
                        if (keep[tris[i]] && keep[tris[i + 1]] && keep[tris[i + 2]])
                        {
                            kept.Add(tris[i]);
                            kept.Add(tris[i + 1]);
                            kept.Add(tris[i + 2]);
                        }
                        else removed++;
                    subs.Add(kept.ToArray());
                }
                if (removed == 0)
                    continue;
                for (int s = 0; s < subs.Count; s++)
                    if (subs[s] != null)
                        mesh.SetTriangles(subs[s], s, true);
                EditorUtility.SetDirty(mesh);
                meshes++;
                triangles += removed;
            }
            Debug.Log($"[Surround] Trimmed {triangles} triangles past the map edge from {meshes} generated meshes. " +
                      "Traffic and street furniture on the clipped roads go away on the next scene rebuild.");
        }
    }
}
