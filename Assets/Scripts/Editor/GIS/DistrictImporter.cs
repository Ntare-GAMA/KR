using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace KR.Editor.GIS
{
    // Builds one reference scene per district from Tools/gis output (real terrain, OSM roads, building blocks).
    // For route planning and scale checks only; runtime levels are built from 60 m pattern prefabs.
    // Data: © OpenStreetMap contributors (ODbL); Copernicus DEM GLO-30.
    public static class DistrictImporter
    {
        const string OutputRoot = "Assets/World/Districts";
        const string MaterialFolder = OutputRoot + "/_Materials";
        const float BuildingChunkSize = 500f;
        const float RoadSampleStep = 10f;
        const float RoadLift = 0.3f;

        internal static readonly Dictionary<string, float> RoadWidths = new Dictionary<string, float>
        {
            { "trunk", 14f }, { "trunk_link", 7f }, { "primary", 12f }, { "primary_link", 7f },
            { "secondary", 9f }, { "secondary_link", 6f }, { "tertiary", 7f }, { "tertiary_link", 5f },
            { "unclassified", 5f }, { "residential", 5f }, { "living_street", 4f },
        };

        [Serializable] internal class TerrainMeta { public string district; public string levels; public UnityTerrain unity_terrain; public Hub[] hubs; }
        [Serializable] internal class Hub { public string name; public float x; public float z; }
        [Serializable] internal class UnityTerrain { public int heightmap_resolution; public float width_m; public float length_m; public float height_m; }
        [Serializable] internal class RoadList { public Road[] roads; }
        [Serializable] internal class Road { public string highway; public string name; public string oneway; public float[] pts; }
        [Serializable] internal class BuildingList { public Building[] buildings; }
        [Serializable] internal class Building { public float h; public float[] pts; }

        internal static string SourceDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "SourceArt", "GIS", "districts"));

        [MenuItem("Kigali Rush/GIS/Build Kimironko + Kibagabaga reference")]
        static void BuildKimironkoKibagabaga() => Build("kimironko_kibagabaga");

        public static void Build(string district)
        {
            string src = Path.Combine(SourceDir, district);
            if (!File.Exists(Path.Combine(src, "terrain.json")))
            {
                Debug.LogError($"[DistrictImporter] No data at {src}. Run Tools/gis/make_district_terrain.py first.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            string outDir = $"{OutputRoot}/{district}";
            EnsureFolder(outDir);
            EnsureFolder(MaterialFolder);

            var meta = JsonUtility.FromJson<TerrainMeta>(File.ReadAllText(Path.Combine(src, "terrain.json")));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var root = new GameObject($"District_{district}").transform;

            try
            {
                EditorUtility.DisplayProgressBar("District Importer", "Terrain", 0.1f);
                var terrain = BuildTerrain(src, meta, outDir, root);

                EditorUtility.DisplayProgressBar("District Importer", "Roads", 0.4f);
                var roads = JsonUtility.FromJson<RoadList>(File.ReadAllText(Path.Combine(src, "roads_unity.json")));
                BuildRoads(roads.roads, terrain, root);

                EditorUtility.DisplayProgressBar("District Importer", "Buildings", 0.7f);
                var buildings = JsonUtility.FromJson<BuildingList>(File.ReadAllText(Path.Combine(src, "buildings_unity.json")));
                BuildBuildings(buildings.buildings, terrain, root);

                PlaceMarkerAndCamera(meta, terrain, root);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            string scenePath = $"{outDir}/{district}_Reference.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"[DistrictImporter] {district} ({meta.levels}) saved to {scenePath}");
        }

        internal static Terrain BuildTerrain(string src, TerrainMeta meta, string outDir, Transform parent)
        {
            var t = meta.unity_terrain;
            int res = t.heightmap_resolution;
            byte[] raw = File.ReadAllBytes(Path.Combine(src, "heightmap_1025.raw"));
            if (raw.Length != res * res * 2)
                throw new InvalidDataException($"Heightmap is {raw.Length} bytes, expected {res * res * 2}.");

            // RAW is 16-bit little-endian, row 0 = south edge, which matches TerrainData [z, x].
            var heights = new float[res, res];
            for (int z = 0, i = 0; z < res; z++)
                for (int x = 0; x < res; x++, i += 2)
                    heights[z, x] = (raw[i] | (raw[i + 1] << 8)) / 65535f;

            var data = new TerrainData { heightmapResolution = res };
            data.size = new Vector3(t.width_m, t.height_m, t.length_m);
            data.SetHeights(0, 0, heights);
            data.terrainLayers = new[]
            {
                GetTerrainLayer("Grass", new Color(0.33f, 0.45f, 0.22f)),
                GetTerrainLayer("RedEarth", new Color(0.55f, 0.30f, 0.18f)),
            };
            PaintBySlope(data);

            string path = $"{outDir}/{meta.district}_TerrainData.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(data, path);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 8;
            return terrain;
        }

        // Steep slopes get exposed red earth, as on Kigali's hillsides.
        static void PaintBySlope(TerrainData data)
        {
            int res = data.alphamapResolution;
            var maps = new float[res, res, 2];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float steep = data.GetSteepness((float)x / (res - 1), (float)y / (res - 1));
                    float earth = Mathf.InverseLerp(14f, 24f, steep);
                    maps[y, x, 0] = 1f - earth;
                    maps[y, x, 1] = earth;
                }
            data.SetAlphamaps(0, 0, maps);
        }

        internal static void BuildRoads(Road[] roads, Terrain terrain, Transform parent)
        {
            var group = new GameObject("Roads").transform;
            group.SetParent(parent, false);
            var majorMat = GetMaterial("Road_Major", new Color(0.18f, 0.18f, 0.19f));
            var minorMat = GetMaterial("Road_Minor", new Color(0.30f, 0.30f, 0.30f));

            var minor = new MeshBuilder();
            foreach (var road in roads)
            {
                if (road.pts == null || road.pts.Length < 6 || !RoadWidths.TryGetValue(road.highway, out float width))
                    continue;
                bool isMajor = road.highway.StartsWith("trunk") || road.highway.StartsWith("primary") ||
                               road.highway.StartsWith("secondary");
                if (isMajor)
                {
                    // Major roads stay separate and named so routes can be traced in the Hierarchy.
                    var mb = new MeshBuilder();
                    AddRibbon(mb, Resample(road.pts), width, terrain);
                    string label = string.IsNullOrEmpty(road.name) ? "(unnamed)" : road.name;
                    CreateMeshObject($"{road.highway} | {label}", mb, group, majorMat);
                }
                else
                {
                    AddRibbon(minor, Resample(road.pts), width, terrain);
                }
            }
            CreateMeshObject("Minor roads (combined)", minor, group, minorMat);
        }

        static List<Vector3> Resample(float[] pts)
        {
            var result = new List<Vector3>();
            for (int i = 0; i + 5 < pts.Length; i += 3)
            {
                var a = new Vector3(pts[i], 0f, pts[i + 2]);
                var b = new Vector3(pts[i + 3], 0f, pts[i + 5]);
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / RoadSampleStep));
                for (int s = 0; s < steps; s++)
                    result.Add(Vector3.Lerp(a, b, (float)s / steps));
            }
            result.Add(new Vector3(pts[pts.Length - 3], 0f, pts[pts.Length - 1]));
            return result;
        }

        static void AddRibbon(MeshBuilder mb, List<Vector3> line, float width, Terrain terrain)
        {
            if (line.Count < 2)
                return;
            int start = mb.Vertices.Count;
            float distance = 0f;
            for (int i = 0; i < line.Count; i++)
            {
                if (i > 0)
                    distance += Vector3.Distance(line[i - 1], line[i]);
                mb.UVs.Add(new Vector2(0f, distance));
                mb.UVs.Add(new Vector2(width, distance));
                Vector3 dir = line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)];
                dir.y = 0f;
                Vector3 side = new Vector3(-dir.z, 0f, dir.x).normalized * (width * 0.5f);
                Vector3 l = line[i] + side, r = line[i] - side;
                l.y = terrain.SampleHeight(l) + RoadLift;
                r.y = terrain.SampleHeight(r) + RoadLift;
                mb.Vertices.Add(l);
                mb.Vertices.Add(r);
            }
            for (int i = 0; i < line.Count - 1; i++)
            {
                int v = start + i * 2;
                AddUpQuad(mb.Triangles(0), mb.Vertices, v, v + 1, v + 3, v + 2);
            }
        }

        // Adds quad a-b-c-d with whichever winding faces +Y.
        static void AddUpQuad(List<int> tris, List<Vector3> verts, int a, int b, int c, int d)
        {
            Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            if (n.y >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(a); tris.Add(c); tris.Add(d); }
            else { tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(a); tris.Add(d); tris.Add(c); }
        }

        internal static void BuildBuildings(Building[] buildings, Terrain terrain, Transform parent,
                                            Func<Building, bool> include = null, Material[] materials = null)
        {
            var group = new GameObject("Buildings (OSM footprints, estimated heights)").transform;
            group.SetParent(parent, false);
            materials ??= new[]
            {
                GetMaterial("Building_Wall", new Color(0.86f, 0.83f, 0.76f)),
                GetMaterial("Building_Roof", new Color(0.62f, 0.27f, 0.18f)),
            };

            var chunks = new Dictionary<Vector2Int, MeshBuilder>();
            foreach (var b in buildings)
            {
                if (include != null && !include(b))
                    continue;
                int n = b.pts.Length / 2;
                if (n < 3)
                    continue;
                var ring = new Vector3[n];
                for (int i = 0; i < n; i++)
                    ring[i] = new Vector3(b.pts[i * 2], 0f, b.pts[i * 2 + 1]);
                if (SignedArea(ring) < 0f)
                    Array.Reverse(ring);

                float ground = float.MaxValue;
                foreach (var p in ring)
                    ground = Mathf.Min(ground, terrain.SampleHeight(p));
                float baseY = ground - 0.5f, topY = ground + b.h;

                var key = new Vector2Int(Mathf.FloorToInt(ring[0].x / BuildingChunkSize),
                                         Mathf.FloorToInt(ring[0].z / BuildingChunkSize));
                if (!chunks.TryGetValue(key, out var mb))
                    chunks[key] = mb = new MeshBuilder();
                AddExtrusion(mb, ring, baseY, topY);
            }

            foreach (var kv in chunks)
                CreateMeshObject($"Chunk {kv.Key.x},{kv.Key.y}", kv.Value, group, materials);
        }

        static float SignedArea(Vector3[] ring)
        {
            float a = 0f;
            for (int i = 0; i < ring.Length; i++)
            {
                var p = ring[i];
                var q = ring[(i + 1) % ring.Length];
                a += p.x * q.z - q.x * p.z;
            }
            return a * 0.5f;
        }

        // Ring must be counter-clockwise seen from above. Unity front faces are clockwise from the viewer.
        static void AddExtrusion(MeshBuilder mb, Vector3[] ring, float baseY, float topY)
        {
            var walls = mb.Triangles(0);
            float u0 = 0f;
            for (int i = 0; i < ring.Length; i++)
            {
                Vector3 a = ring[i], b = ring[(i + 1) % ring.Length];
                float u1 = u0 + Vector3.Distance(a, b);
                int v = mb.Vertices.Count;
                mb.Vertices.Add(new Vector3(a.x, baseY, a.z));
                mb.Vertices.Add(new Vector3(a.x, topY, a.z));
                mb.Vertices.Add(new Vector3(b.x, topY, b.z));
                mb.Vertices.Add(new Vector3(b.x, baseY, b.z));
                mb.UVs.Add(new Vector2(u0, baseY));
                mb.UVs.Add(new Vector2(u0, topY));
                mb.UVs.Add(new Vector2(u1, topY));
                mb.UVs.Add(new Vector2(u1, baseY));
                u0 = u1;
                walls.Add(v); walls.Add(v + 1); walls.Add(v + 2);
                walls.Add(v); walls.Add(v + 2); walls.Add(v + 3);
            }

            // Fan roof: correct for convex footprints, approximate for the rest (reference geometry only).
            var roof = mb.Triangles(1);
            int r0 = mb.Vertices.Count;
            foreach (var p in ring)
            {
                mb.Vertices.Add(new Vector3(p.x, topY, p.z));
                mb.UVs.Add(new Vector2(p.x, p.z));
            }
            for (int i = 1; i < ring.Length - 1; i++)
            {
                roof.Add(r0); roof.Add(r0 + i + 1); roof.Add(r0 + i);
            }
        }

        static void PlaceMarkerAndCamera(TerrainMeta meta, Terrain terrain, Transform parent)
        {
            var centre = new Vector3(meta.unity_terrain.width_m * 0.5f, 0f, meta.unity_terrain.length_m * 0.5f);
            centre.y = terrain.SampleHeight(centre);

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Landmark marker (district centre)";
            marker.transform.SetParent(parent, false);
            marker.transform.position = centre + Vector3.up * 50f;
            marker.transform.localScale = new Vector3(20f, 50f, 20f);
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.GetComponent<MeshRenderer>().sharedMaterial = GetMaterial("Marker", new Color(0.9f, 0.1f, 0.1f));

            var cam = Camera.main;
            if (cam != null)
            {
                cam.farClipPlane = 10000f;
                cam.transform.position = centre + new Vector3(0f, 700f, -1100f);
                cam.transform.LookAt(centre);
            }
        }

        static void CreateMeshObject(string name, MeshBuilder mb, Transform parent, params Material[] materials)
        {
            if (mb.Vertices.Count == 0)
                return;
            var mesh = mb.ToMesh(name);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var used = new Material[mesh.subMeshCount];
            for (int i = 0; i < used.Length; i++)
                used[i] = materials[Mathf.Min(i, materials.Length - 1)];
            mr.sharedMaterials = used;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        static Material GetMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static TerrainLayer GetTerrainLayer(string name, Color color)
        {
            string path = $"{MaterialFolder}/TL_{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer != null)
            {
                // Smoothness from the (opaque) diffuse alpha turns the ground into a mirror.
                if (layer.smoothnessSource != TerrainLayerSmoothnessSource.Constant)
                {
                    layer.smoothnessSource = TerrainLayerSmoothnessSource.Constant;
                    layer.smoothness = 0.05f;
                    EditorUtility.SetDirty(layer);
                }
                return layer;
            }

            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = $"T_{name}" };
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();
            AssetDatabase.CreateAsset(tex, $"{MaterialFolder}/T_{name}.asset");

            layer = new TerrainLayer
            {
                diffuseTexture = tex, tileSize = new Vector2(10f, 10f),
                smoothnessSource = TerrainLayerSmoothnessSource.Constant, smoothness = 0.05f,
            };
            AssetDatabase.CreateAsset(layer, path);
            return layer;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        class MeshBuilder
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> UVs = new List<Vector2>();
            readonly List<List<int>> _submeshes = new List<List<int>>();

            public List<int> Triangles(int submesh)
            {
                while (_submeshes.Count <= submesh)
                    _submeshes.Add(new List<int>());
                return _submeshes[submesh];
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(Vertices);
                if (UVs.Count == Vertices.Count)
                    mesh.SetUVs(0, UVs);
                mesh.subMeshCount = _submeshes.Count;
                for (int i = 0; i < _submeshes.Count; i++)
                    mesh.SetTriangles(_submeshes[i], i);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
