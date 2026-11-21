using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KR.Editor.Util;
using KR.Gameplay.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KR.Editor.Kigali
{
    // Clears the carriageways of the Story scene (owner decision 2026-11-21: nothing standing in the road, no houses
    // in the road). Every building, tree, palm, bush, lamp, signal pole, parked car and piece of street furniture is
    // measured against the streets (centre line and real half width, Streets.asset). Whatever stands inside the
    // carriageway is removed; things on the kerb, the footpath or beside the road stay. Parked cars may stand at the
    // kerb, but not out in the lanes. Lamps, signal poles, story signs and speed cameras are moved to the kerb
    // instead (they carry the lamp and signal heads the city's systems drive). The cleaned layers are saved back to
    // their editable prefabs, so rebuilds keep it.
    // Report only: Logs/clear_roads.txt lists every object it would remove or move.
    internal static class ClearRoads
    {
        const string StreetsPath = "Assets/World/Story/Streets.asset";
        const float Cell = 16f;

        static readonly string[] Layers =
        {
            "City buildings", "Trees and palms", "Parked cars", "Street lamps and signal poles", "Street furniture",
        };

        struct Seg { public Vector2 a, b; public float half; }

        static Dictionary<long, List<Seg>> _grid;

        [MenuItem("Kigali Rush/Story/Clear the roads (report)")]
        public static void Report() => Run(false);

        [MenuItem("Kigali Rush/Story/Clear the roads")]
        public static void Apply() => Run(true);

        static void Run(bool apply)
        {
            var scene = EditorSceneManager.OpenScene(StoryModeBuilder.ScenePath, OpenSceneMode.Single);
            var streets = AssetDatabase.LoadAssetAtPath<StreetNetwork>(StreetsPath);
            if (streets == null)
            {
                Debug.LogError($"[ClearRoads] {StreetsPath} missing.");
                return;
            }
            BuildGrid(streets);

            var log = new StringBuilder();
            var counts = new Dictionary<string, int>();
            var roots = new List<Transform>();
            foreach (var layerName in Layers)
            {
                var layer = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                                 .FirstOrDefault(t => t.name == layerName);
                if (layer == null)
                {
                    log.AppendLine($"(no layer {layerName})");
                    continue;
                }
                roots.Add(layer);
                bool parked = layerName == "Parked cars";
                bool building = layerName == "City buildings";
                bool poles = layerName == "Street lamps and signal poles";
                bool trunks = poles || layerName == "Trees and palms";
                var victims = new List<GameObject>();
                int total = 0, moved = 0;
                foreach (Transform area in layer)
                    foreach (var obj in area.Cast<Transform>().ToList())
                    {
                        total++;
                        var foot = Footprint(obj.gameObject, trunks);
                        if (foot == null)
                            continue;
                        float deepest = foot.Max(p => Inside(p));
                        // Kerb tolerance: lamps and trees may stand right at the edge; parked cars may sit in the
                        // kerbside strip (up to 2 m in); buildings may touch the edge by just over half a metre.
                        float allowed = parked ? 2.0f : building ? 0.6f : 0.35f;
                        if (deepest <= allowed)
                            continue;
                        var c = obj.position;
                        if (poles)
                        {
                            log.AppendLine($"{layerName}\t{obj.name}\t{deepest:0.0} m in\t({c.x:0},{c.z:0})\tmoved to the kerb");
                            if (apply && !PushOut(obj, foot[0]))
                                log.AppendLine("\t(no kerb found; left)");
                            moved++;
                            continue;
                        }
                        victims.Add(obj.gameObject);
                        log.AppendLine($"{layerName}\t{obj.name}\t{deepest:0.0} m in\t({c.x:0},{c.z:0})");
                    }

                // A house's foundation slab goes with the house.
                if (building)
                {
                    var gone = victims.Where(v => !v.name.StartsWith("Foundation")).Select(v => v.GetComponent<Collider>())
                                      .Where(col => col != null).Select(col => col.bounds).ToList();
                    foreach (Transform area in layer)
                        foreach (Transform obj in area)
                            if (obj.name.StartsWith("Foundation") && !victims.Contains(obj.gameObject) &&
                                gone.Any(b => b.Contains(new Vector3(obj.position.x, b.center.y, obj.position.z))))
                                victims.Add(obj.gameObject);
                }
                counts[layerName + (poles ? " moved" : " removed")] = poles ? moved : victims.Count;
                log.AppendLine($"== {layerName}: {(poles ? moved : victims.Count)} of {total} in the road");
                if (apply)
                    foreach (var v in victims)
                        Object.DestroyImmediate(v);
            }

            // Story signs and the speed cameras (on poles) out of the road too; anything else is only listed.
            var layerSet = new HashSet<Transform>(roots);
            var skip = new[] { "Traffic", "Road", "road", "Marking", "Player", "Terrain", "Surround", "Walker", "Pedestrian", "Zebra", "Kerb", "Footpath", "campus" };
            var poleGroups = new[] { "Street names", "Signboards (OSM names)", "Police posts" };
            var pushed = new HashSet<Transform>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                bool inLayer = false;
                var path = new List<string>();
                Transform pole = null;
                for (var p = r.transform; p != null; p = p.parent)
                {
                    if (layerSet.Contains(p)) { inLayer = true; break; }
                    if (p.parent != null && poleGroups.Contains(p.parent.name))
                        pole = p;
                    path.Insert(0, p.name);
                }
                if (inLayer || path.Any(n => skip.Any(n.Contains)))
                    continue;
                var b = r.bounds;
                if (b.size.y < 0.4f)
                    continue;
                float deepest = -99f;
                for (int i = 0; i <= 6; i++)
                    for (int k = 0; k <= 6; k++)
                        deepest = Mathf.Max(deepest, Inside(new Vector2(b.min.x + b.size.x * i / 6f, b.min.z + b.size.z * k / 6f)));
                if (deepest <= 0.35f)
                    continue;
                if (pole != null && b.size.x < 4f && b.size.z < 4f)
                {
                    if (pushed.Add(pole))
                    {
                        log.AppendLine($"SIGN\t{string.Join("/", path.Take(4))}\t{deepest:0.0} m in\tmoved to the kerb");
                        if (apply)
                            PushOut(pole, Flat(b.center));
                    }
                }
                else if (deepest > 1f)
                    log.AppendLine($"OTHER\t{string.Join("/", path.Take(4))}\t{deepest:0.0} m in\tsize {b.size.x:0}x{b.size.y:0}x{b.size.z:0}\t({b.center.x:0},{b.center.z:0})");
            }
            counts["signs and cameras moved"] = pushed.Count;

            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/clear_roads.txt", log.ToString());
            Debug.Log($"[ClearRoads] {(apply ? "Done" : "Report only")}: " +
                      string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}")) + ". List: Logs/clear_roads.txt");
            if (!apply)
                return;
            FixTraffic();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditableLayer.Keep();
            AssetDatabase.SaveAssets();
        }

        // Traffic models that came with effects (tyre trails, nitro and exhaust particles) were sized and placed with
        // those effects counted: they hovered half a metre above the road and their lamps lay on the road around them.
        // Removes the effects, puts each model back on its wheels and rebuilds its lamps from the model alone.
        static void FixTraffic()
        {
            const string store = "Assets/World/PC/kimironko_kibagabaga/VehicleLightsFixed.asset";
            var meshes = new PcCityLife.MeshStore(store);
            var made = new Dictionary<string, Mesh>();
            int fixedCars = 0;
            foreach (var traffic in Object.FindObjectsByType<RoadNetworkTraffic>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (traffic.IsWalkers)
                    continue;
                foreach (Transform holder in traffic.transform.GetComponentsInChildren<Transform>(true))
                {
                    if (!holder.name.StartsWith("Vehicle ") || holder.parent == null || holder.parent.name.StartsWith("Vehicle "))
                        continue;
                    var lights = holder.Find("Lights");
                    Transform model = null;
                    foreach (Transform c in holder)
                        if (c != lights && c.GetComponentInChildren<MeshRenderer>(true) != null)
                        {
                            model = c;
                            break;
                        }
                    if (model == null)
                        continue;
                    bool hadEffects = model.GetComponentsInChildren<ParticleSystem>(true).Length > 0 ||
                                      model.GetComponentsInChildren<TrailRenderer>(true).Length > 0;
                    EditorAssetUtil.StripRuntimeComponents(model.gameObject);

                    // Measure with the holder upright at its own position.
                    var pos = holder.position;
                    var rot = holder.rotation;
                    holder.rotation = Quaternion.identity;
                    var b = EditorAssetUtil.RendererBounds(model.gameObject);
                    model.position += new Vector3(pos.x - b.center.x, pos.y - b.min.y, pos.z - b.center.z);
                    b = EditorAssetUtil.RendererBounds(model.gameObject);
                    if (lights != null)
                    {
                        string key = model.name;
                        if (!made.TryGetValue(key, out var mesh))
                        {
                            var local = new Bounds(b.center - pos, b.size);
                            made[key] = mesh = meshes.Add(PcShowcaseBuilder.VehicleLightMesh(local), $"Lights_{key}");
                        }
                        lights.localPosition = Vector3.zero;
                        lights.localRotation = Quaternion.identity;
                        lights.GetComponent<MeshFilter>().sharedMesh = mesh;
                    }
                    holder.rotation = rot;
                    if (hadEffects)
                        fixedCars++;
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[ClearRoads] Traffic: {fixedCars} vehicles had effects removed and were put back on their wheels; lamps rebuilt for {made.Count} models.");
        }

        static void BuildGrid(StreetNetwork streets)
        {
            _grid = new Dictionary<long, List<Seg>>();
            foreach (var s in streets.streets)
            {
                if (s.points == null || s.halfWidth <= 0.5f)
                    continue;
                for (int i = 0; i + 1 < s.points.Length; i++)
                {
                    var seg = new Seg { a = Flat(s.points[i]), b = Flat(s.points[i + 1]), half = s.halfWidth };
                    var min = Vector2.Min(seg.a, seg.b) - Vector2.one * (s.halfWidth + 1f);
                    var max = Vector2.Max(seg.a, seg.b) + Vector2.one * (s.halfWidth + 1f);
                    for (int x = Mathf.FloorToInt(min.x / Cell); x <= Mathf.FloorToInt(max.x / Cell); x++)
                        for (int z = Mathf.FloorToInt(min.y / Cell); z <= Mathf.FloorToInt(max.y / Cell); z++)
                        {
                            long key = Key(x, z);
                            if (!_grid.TryGetValue(key, out var list))
                                _grid[key] = list = new List<Seg>();
                            list.Add(seg);
                        }
                }
            }
        }

        // How far p is inside the carriageway (metres; negative = outside).
        static float Inside(Vector2 p) => Nearest(p, out _);

        // The same, with the way out (from the deepest road's centre line towards its kerb).
        static float Nearest(Vector2 p, out Vector2 outward)
        {
            outward = Vector2.zero;
            if (!_grid.TryGetValue(Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell)), out var list))
                return -99f;
            float best = -99f;
            foreach (var s in list)
            {
                var ab = s.b - s.a;
                float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector2.Dot(p - s.a, ab) / ab.sqrMagnitude) : 0f;
                var off = p - (s.a + ab * t);
                float depth = s.half - off.magnitude;
                if (depth > best)
                {
                    best = depth;
                    outward = off.sqrMagnitude > 1e-4f ? off.normalized : new Vector2(-ab.y, ab.x).normalized;
                }
            }
            return best;
        }

        // Moves a pole (lamp, signal, sign, camera) out of the road onto the kerb beside it, on the ground there.
        static bool PushOut(Transform t, Vector2 foot)
        {
            for (int step = 0; step < 4 && Inside(foot) > -0.6f; step++)
            {
                float depth = Nearest(foot, out var dir);
                var move = dir * (depth + 0.8f);
                foot += move;
                t.position += new Vector3(move.x, 0f, move.y);
            }
            var from = new Vector3(t.position.x, t.position.y + 6f, t.position.z);
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, 14f, 1 << 0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                if (!hit.collider.transform.IsChildOf(t) && hit.collider.attachedRigidbody == null && hit.normal.y > 0.7f)
                {
                    t.position = new Vector3(t.position.x, hit.point.y, t.position.z);
                    break;
                }
            return Inside(foot) <= -0.6f;
        }

        // Points across the object's ground footprint (world x,z): its solid collider when it has one (a tree's trunk,
        // a lamp's pole, a building's walls), otherwise its visible bounds (only the pivot for a tree or a pole).
        // Null for an empty object.
        static List<Vector2> Footprint(GameObject go, bool trunkOnly)
        {
            var box = go.GetComponent<BoxCollider>();
            Vector3 centre, ax, az;
            if (box != null)
            {
                var t = go.transform;
                centre = t.TransformPoint(box.center);
                ax = t.TransformVector(new Vector3(box.size.x * 0.5f, 0f, 0f));
                az = t.TransformVector(new Vector3(0f, 0f, box.size.z * 0.5f));
            }
            else
            {
                if (trunkOnly)
                    return new List<Vector2> { Flat(go.transform.position) };
                var rs = go.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0)
                    return null;
                var b = rs[0].bounds;
                foreach (var r in rs)
                    b.Encapsulate(r.bounds);
                centre = b.center;
                ax = new Vector3(b.extents.x, 0f, 0f);
                az = new Vector3(0f, 0f, b.extents.z);
            }
            float lx = new Vector2(ax.x, ax.z).magnitude * 2f, lz = new Vector2(az.x, az.z).magnitude * 2f;
            int nx = Mathf.Clamp(Mathf.CeilToInt(lx), 1, 40), nz = Mathf.Clamp(Mathf.CeilToInt(lz), 1, 40);
            var pts = new List<Vector2>((nx + 1) * (nz + 1)) { Flat(centre) };
            for (int i = 0; i <= nx; i++)
                for (int k = 0; k <= nz; k++)
                    pts.Add(Flat(centre + ax * (2f * i / nx - 1f) + az * (2f * k / nz - 1f)));
            return pts;
        }

        static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);
        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}
