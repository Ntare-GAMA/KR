using System.Collections.Generic;
using System.Linq;
using KR.Editor.Util;
using UnityEditor;
using UnityEngine;

namespace KR.Editor.Kigali
{
    // Two clean-ups of the city's look, made once and reused by every build:
    //   - Vehicles whose body is plain grey or white (untextured City Pack models) get proper paint: a few variants
    //     per model in real car colours, saved as prefabs next to each other. Police vehicles keep their own look.
    //   - Houses: modest, clean Kigali-style bungalows (plastered walls in light colours, a dark plinth, an iron-sheet
    //     hipped roof, a door and framed windows on every side), generated as simple meshes. They replace the
    //     cartoon house set; FitBox stretches each one to the real OSM footprint.
    internal static class CleanCity
    {
        // ---------- Vehicle paint ----------

        static readonly (string name, Color color)[] Paints =
        {
            ("Red", new Color(0.55f, 0.05f, 0.05f)), ("Blue", new Color(0.08f, 0.2f, 0.6f)),
            ("Green", new Color(0.06f, 0.32f, 0.16f)), ("Maroon", new Color(0.35f, 0.04f, 0.08f)),
            ("Navy", new Color(0.05f, 0.08f, 0.25f)), ("Gold", new Color(0.62f, 0.5f, 0.3f)),
            ("Teal", new Color(0.02f, 0.38f, 0.42f)), ("Orange", new Color(0.8f, 0.33f, 0.05f)),
        };
        const int VariantsPerModel = 4;
        static readonly string[] NotBody = { "glass", "window", "tyre", "tire", "wheel", "light", "rim", "chrome", "interior", "black", "plate", "mirror" };

        public static GameObject[] PaintVehicles(GameObject[] vehicles, string dir)
        {
            EditorAssetUtil.EnsureFolder(dir);
            var result = new List<GameObject>();
            int model = 0;
            foreach (var prefab in vehicles)
            {
                if (prefab == null)
                    continue;
                var body = prefab.name.Contains("Police") ? null : BodyMaterial(prefab);
                if (body == null)
                {
                    result.Add(prefab);
                    continue;
                }
                for (int v = 0; v < VariantsPerModel; v++)
                {
                    var (paintName, color) = Paints[(model * 3 + v) % Paints.Length];
                    string path = $"{dir}/{prefab.name}_{paintName}.prefab";
                    var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (existing != null)
                    {
                        result.Add(existing);
                        continue;
                    }
                    var paint = EditorAssetUtil.LitMaterial(dir, $"CarPaint_{paintName}", color, 0.82f);
                    paint.SetFloat("_Metallic", 0.35f);
                    var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    foreach (var r in temp.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = r.sharedMaterials;
                        bool changed = false;
                        for (int i = 0; i < mats.Length; i++)
                            if (mats[i] == body)
                            {
                                mats[i] = paint;
                                changed = true;
                            }
                        if (changed)
                            r.sharedMaterials = mats;
                    }
                    var saved = PrefabUtility.SaveAsPrefabAsset(temp, path);
                    Object.DestroyImmediate(temp);
                    if (saved != null)
                        result.Add(saved);
                }
                model++;
            }
            Debug.Log($"[CleanCity] Vehicles: {vehicles.Length} models -> {result.Count} with paint variants for {model} grey-bodied models.");
            return result.ToArray();
        }

        // The model's body material if it is an untextured grey or white (no colour of its own), else null.
        // Of several candidates, the one covering the most triangles.
        static Material BodyMaterial(GameObject prefab)
        {
            var tris = new Dictionary<Material, int>();
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : (r as SkinnedMeshRenderer)?.sharedMesh;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !Colourless(m))
                        continue;
                    int count = mesh != null && i < mesh.subMeshCount ? (int)mesh.GetIndexCount(i) / 3 : 0;
                    tris[m] = (tris.TryGetValue(m, out int t) ? t : 0) + count;
                }
            }
            return tris.Count == 0 ? null : tris.OrderByDescending(kv => kv.Value).First().Key;
        }

        static bool Colourless(Material m)
        {
            string n = m.name.ToLowerInvariant();
            if (NotBody.Any(n.Contains))
                return false;
            var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            if (tex != null)
                return false;
            var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            Color.RGBToHSV(c, out _, out float s, out float v);
            return s < 0.12f && v > 0.35f;
        }

        // ---------- Modest houses ----------

        static readonly Color[] Walls =
        {
            new Color(0.93f, 0.92f, 0.88f), new Color(0.95f, 0.9f, 0.78f), new Color(0.88f, 0.82f, 0.7f),
            new Color(0.95f, 0.88f, 0.62f), new Color(0.94f, 0.83f, 0.73f), new Color(0.83f, 0.84f, 0.83f),
        };
        static readonly Color[] Roofs =
        {
            new Color(0.58f, 0.24f, 0.14f), new Color(0.2f, 0.21f, 0.22f), new Color(0.16f, 0.3f, 0.2f),
            new Color(0.45f, 0.18f, 0.12f), new Color(0.26f, 0.31f, 0.37f),
        };

        public static GameObject[] ModestHouses(string dir, int count = 10)
        {
            EditorAssetUtil.EnsureFolder(dir);
            var plinth = EditorAssetUtil.LitMaterial(dir, "House_Plinth", new Color(0.36f, 0.33f, 0.3f), 0.15f);
            var frame = EditorAssetUtil.LitMaterial(dir, "House_Frame", new Color(0.95f, 0.95f, 0.93f), 0.3f);
            var glass = EditorAssetUtil.LitMaterial(dir, "House_Glass", new Color(0.1f, 0.14f, 0.18f), 0.85f);
            var door = EditorAssetUtil.LitMaterial(dir, "House_Door", new Color(0.32f, 0.2f, 0.12f), 0.35f);
            var houses = new List<GameObject>();
            for (int k = 0; k < count; k++)
            {
                string path = $"{dir}/ModestHouse_{k}.prefab";
                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (existing != null)
                {
                    houses.Add(existing);
                    continue;
                }
                var wall = EditorAssetUtil.LitMaterial(dir, $"House_Wall_{k % Walls.Length}", Walls[k % Walls.Length], 0.12f);
                var roof = EditorAssetUtil.LitMaterial(dir, $"House_Roof_{k % Roofs.Length}", Roofs[(k * 2) % Roofs.Length], 0.45f);
                var mesh = HouseMesh(k);
                AssetDatabase.CreateAsset(mesh, $"{dir}/ModestHouse_{k}.asset");
                var go = new GameObject($"ModestHouse_{k}");
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { wall, plinth, roof, frame, glass, door };
                houses.Add(PrefabUtility.SaveAsPrefabAsset(go, path));
                Object.DestroyImmediate(go);
            }
            AssetDatabase.SaveAssets();
            return houses.ToArray();
        }

        // Unit footprint (x, z in -0.5..0.5), walls to 0.72, hipped roof to 1.0. Submeshes: wall, plinth, roof,
        // window frame, glass, door. Variant k changes the window layout a little.
        static Mesh HouseMesh(int k)
        {
            var b = new MeshBuilder(6);
            const float wallTop = 0.72f, plinthTop = 0.06f, eave = 0.06f, ridgeHalf = 0.2f;
            // Walls and plinth (four faces each).
            b.Box(0, new Vector3(-0.5f, plinthTop, -0.5f), new Vector3(0.5f, wallTop, 0.5f));
            b.Box(1, new Vector3(-0.505f, 0f, -0.505f), new Vector3(0.505f, plinthTop, 0.505f));
            // Hipped roof: eaves rectangle up to a ridge along x.
            float e = 0.5f + eave;
            Vector3 a0 = new Vector3(-e, wallTop, -e), a1 = new Vector3(e, wallTop, -e), a2 = new Vector3(e, wallTop, e), a3 = new Vector3(-e, wallTop, e);
            Vector3 r0 = new Vector3(-ridgeHalf, 1f, 0f), r1 = new Vector3(ridgeHalf, 1f, 0f);
            b.Quad(2, a0, r0, r1, a1);      // front slope
            b.Quad(2, a2, r1, r0, a3);      // back slope
            b.Tri(2, a1, r1, a2);           // right hip
            b.Tri(2, a3, r0, a0);           // left hip
            b.Quad(2, a1, a2, a3, a0);      // soffit (underside)
            // Door on the front (-z), windows around.
            b.Box(5, new Vector3(-0.07f, plinthTop, -0.51f), new Vector3(0.07f, 0.5f, -0.495f));
            float[] front = k % 2 == 0 ? new[] { -0.3f, 0.3f } : new[] { -0.32f, -0.18f, 0.25f };
            foreach (float x in front)
                Window(b, new Vector3(x, 0.38f, -0.5f), Vector3.back);
            foreach (float x in new[] { -0.28f, 0.0f, 0.28f })
                Window(b, new Vector3(x, 0.38f, 0.5f), Vector3.forward);
            foreach (float z in k % 3 == 0 ? new[] { 0f } : new[] { -0.2f, 0.2f })
            {
                Window(b, new Vector3(0.5f, 0.38f, z), Vector3.right);
                Window(b, new Vector3(-0.5f, 0.38f, z), Vector3.left);
            }
            var mesh = b.Build($"ModestHouse_{k}");
            return mesh;
        }

        // ---------- Tableware (served at cafes, restaurants and bars) ----------

        // A cup of tea on its saucer, a plate of food, a bottle. Simple shapes in real sizes, made once.
        public static (GameObject cup, GameObject plate, GameObject bottle) Tableware(string dir)
        {
            EditorAssetUtil.EnsureFolder(dir);
            var china = EditorAssetUtil.LitMaterial(dir, "Ware_China", new Color(0.96f, 0.96f, 0.94f), 0.75f);
            var tea = EditorAssetUtil.LitMaterial(dir, "Ware_Tea", new Color(0.55f, 0.36f, 0.2f), 0.85f);
            var rice = EditorAssetUtil.LitMaterial(dir, "Ware_Rice", new Color(0.95f, 0.92f, 0.8f), 0.2f);
            var beans = EditorAssetUtil.LitMaterial(dir, "Ware_Beans", new Color(0.42f, 0.2f, 0.12f), 0.35f);
            var greens = EditorAssetUtil.LitMaterial(dir, "Ware_Greens", new Color(0.18f, 0.42f, 0.14f), 0.3f);
            var glass = EditorAssetUtil.LitMaterial(dir, "Ware_Bottle", new Color(0.55f, 0.75f, 0.85f), 0.9f);
            var cap = EditorAssetUtil.LitMaterial(dir, "Ware_Cap", new Color(0.1f, 0.3f, 0.7f), 0.5f);

            GameObject Save(string name, MeshBuilder b, Material[] mats)
            {
                string path = $"{dir}/{name}.prefab";
                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (existing != null)
                    return existing;
                var mesh = b.Build(name);
                AssetDatabase.CreateAsset(mesh, $"{dir}/{name}.asset");
                var go = new GameObject(name);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
                Object.DestroyImmediate(go);
                return prefab;
            }

            var cupB = new MeshBuilder(2);
            cupB.Cylinder(0, Vector3.zero, 0.075f, 0.008f, 20);                     // saucer
            cupB.Cylinder(0, new Vector3(0f, 0.008f, 0f), 0.042f, 0.085f, 20, false); // cup (open top)
            cupB.Cylinder(1, new Vector3(0f, 0.008f, 0f), 0.039f, 0.075f, 20);       // tea
            var plateB = new MeshBuilder(4);
            plateB.Cylinder(0, Vector3.zero, 0.13f, 0.015f, 24);
            plateB.Cylinder(1, new Vector3(-0.04f, 0.015f, 0.02f), 0.055f, 0.025f, 14);
            plateB.Cylinder(2, new Vector3(0.05f, 0.015f, 0.03f), 0.04f, 0.02f, 14);
            plateB.Cylinder(3, new Vector3(0.01f, 0.015f, -0.06f), 0.04f, 0.018f, 14);
            var bottleB = new MeshBuilder(2);
            bottleB.Cylinder(0, Vector3.zero, 0.035f, 0.2f, 16);
            bottleB.Cylinder(0, new Vector3(0f, 0.2f, 0f), 0.015f, 0.03f, 12);
            bottleB.Cylinder(1, new Vector3(0f, 0.23f, 0f), 0.017f, 0.012f, 12);
            AssetDatabase.SaveAssets();
            return (Save("Ware_CupOfTea", cupB, new[] { china, tea }),
                    Save("Ware_Plate", plateB, new[] { china, rice, beans, greens }),
                    Save("Ware_Bottle", bottleB, new[] { glass, cap }));
        }

        // A white frame with dark glass set just proud of the wall.
        static void Window(MeshBuilder b, Vector3 centre, Vector3 outward)
        {
            var right = Vector3.Cross(outward, Vector3.up);   // the viewer's right, looking at the wall
            const float w = 0.075f, h = 0.12f, f = 0.012f;
            Vector3 o1 = outward * 0.004f, o2 = outward * 0.008f;
            Vector3 c = centre;
            b.Quad(3, c - right * (w + f) - Vector3.up * (h + f) + o1, c - right * (w + f) + Vector3.up * (h + f) + o1,
                      c + right * (w + f) + Vector3.up * (h + f) + o1, c + right * (w + f) - Vector3.up * (h + f) + o1);
            b.Quad(4, c - right * w - Vector3.up * h + o2, c - right * w + Vector3.up * h + o2,
                      c + right * w + Vector3.up * h + o2, c + right * w - Vector3.up * h + o2);
        }

        // Flat-shaded triangles in several submeshes.
        sealed class MeshBuilder
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<int>[] _t;

            public MeshBuilder(int submeshes)
            {
                _t = new List<int>[submeshes];
                for (int i = 0; i < submeshes; i++)
                    _t[i] = new List<int>();
            }

            // Clockwise as seen from the side the face should show on (Unity's front face).
            public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                int i = _v.Count;
                foreach (var p in new[] { a, b, c })
                {
                    _v.Add(p);
                    _n.Add(n);
                    _uv.Add(Mathf.Abs(n.y) > 0.7f ? new Vector2(p.x, p.z) : new Vector2(p.x + p.z, p.y));
                }
                _t[sub].Add(i);
                _t[sub].Add(i + 1);
                _t[sub].Add(i + 2);
            }

            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Tri(sub, a, b, c);
                Tri(sub, a, c, d);
            }

            // An upright cylinder standing on base (its centre at the bottom), with a top cap unless open.
            public void Cylinder(int sub, Vector3 baseCentre, float radius, float height, int sides, bool top = true)
            {
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                    Vector3 p0 = baseCentre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                    Vector3 p1 = baseCentre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                    Vector3 up = Vector3.up * height;
                    Quad(sub, p0, p0 + up, p1 + up, p1);
                    if (top)
                        Tri(sub, baseCentre + up, p1 + up, p0 + up);
                }
            }

            // The four sides of an axis-aligned box (no top or bottom), facing out.
            public void Box(int sub, Vector3 min, Vector3 max)
            {
                Vector3 p000 = min, p100 = new Vector3(max.x, min.y, min.z), p010 = new Vector3(min.x, max.y, min.z), p110 = new Vector3(max.x, max.y, min.z);
                Vector3 p001 = new Vector3(min.x, min.y, max.z), p101 = new Vector3(max.x, min.y, max.z), p011 = new Vector3(min.x, max.y, max.z), p111 = max;
                Quad(sub, p000, p010, p110, p100);   // -z
                Quad(sub, p101, p111, p011, p001);   // +z
                Quad(sub, p100, p110, p111, p101);   // +x
                Quad(sub, p001, p011, p010, p000);   // -x
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, subMeshCount = _t.Length };
                mesh.SetVertices(_v);
                mesh.SetNormals(_n);
                mesh.SetUVs(0, _uv);
                for (int i = 0; i < _t.Length; i++)
                    mesh.SetTriangles(_t[i], i);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }
    }
}
