using System.Collections.Generic;
using System.Linq;
using KR.Editor.Util;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Category = KR.Editor.Kigali.ScatterCollector.Category;

namespace KR.Editor.Kigali
{
    // Writes the city's furniture as real objects you can click, move, rotate, scale, duplicate or delete in the
    // editor: buildings, trees and palms (and the conifers that used to be terrain trees), parked cars, street lamps
    // and signal poles, street furniture. (They used to be drawn straight from a data file, which was fast but left
    // nothing to select.)
    //   - Each object keeps the link to its source model, so one click selects that whole tree or lamp.
    //   - Each carries its own collider, so moving it moves what you bump into.
    //   - A lamp's light points and a signal's head are its children: they move with it.
    //   - Objects are grouped by kind, then by 500 m area ("Area 5,9"), to find them in the Hierarchy.
    //   - Far objects are culled per kind (props 220 m, trees 350 m, houses 600 m) to keep the Quest's frame rate.
    // Each kind is an editable layer (EditableLayer): your edits are kept across rebuilds.
    internal static class CityObjects
    {
        public const string HouseLayer = "PcHouses", TreeLayer = "PcTrees";
        const float Area = 500f;

        public static Transform[] Build(ScatterCollector scatter, PcShowcaseBuilder.Catalog cat, Terrain terrain, Transform root,
                                        string detailLayer, out int count)
        {
            var vehicles = new HashSet<GameObject>(cat.Vehicles);
            int detail = LayerMask.NameToLayer(detailLayer), houses = EditorAssetUtil.EnsureLayer(HouseLayer), trees = EditorAssetUtil.EnsureLayer(TreeLayer);
            var conifers = TerrainTrees(terrain);

            var kinds = new (string name, System.Func<GameObject, Category, bool> pick, int layer, bool withConifers)[]
            {
                ("City buildings", (p, c) => c == Category.House || c == Category.Large, -1, false),
                ("Trees and palms", (p, c) => c == Category.Tree && !vehicles.Contains(p), trees, true),
                ("Parked cars", (p, c) => c == Category.Tree && vehicles.Contains(p), detail, false),
                ("Street lamps and signal poles", (p, c) => c == Category.Lamp, detail, false),
                ("Street furniture", (p, c) => c == Category.Prop, detail, false),
            };
            var roots = new List<Transform>();
            count = 0;
            int made = 0;
            foreach (var kind in kinds)
            {
                bool generated = false;
                var placed = Enumerable.Range(0, scatter.Placed.Count).Where(i => kind.pick(scatter.Placed[i].prefab, scatter.Placed[i].category)).ToList();
                var layer = EditableLayer.Use(kind.name, root, _ =>
                {
                    generated = true;
                    var host = new GameObject(kind.name);
                    var areas = new Dictionary<Vector2Int, Transform>();
                    Transform AreaOf(Vector3 p)
                    {
                        var key = new Vector2Int(Mathf.FloorToInt(p.x / Area), Mathf.FloorToInt(p.z / Area));
                        if (!areas.TryGetValue(key, out var t))
                        {
                            t = new GameObject($"Area {key.x},{key.y}").transform;
                            t.SetParent(host.transform, false);
                            areas[key] = t;
                        }
                        return t;
                    }
                    for (int k = 0; k < placed.Count; k++)
                    {
                        if (k % 2000 == 0)
                            EditorUtility.DisplayProgressBar("City objects", $"{kind.name} {k}/{placed.Count}", (float)k / Mathf.Max(1, placed.Count));
                        int i = placed[k];
                        var (prefab, m, category) = scatter.Placed[i];
                        Vector3 pos = m.GetColumn(3);
                        var go = Make(prefab, pos, m.rotation, m.lossyScale, AreaOf(pos));
                        if (go == null)
                            continue;
                        Dress(go, prefab, scatter.PrefabBounds(prefab), m.lossyScale, category, vehicles.Contains(prefab),
                              kind.layer >= 0 ? kind.layer : category == Category.House ? houses : 0);
                        if (scatter.Attached.TryGetValue(i, out var parts))
                            foreach (var t in parts)
                                if (t != null)
                                    t.SetParent(go.transform, true);
                        made++;
                    }
                    if (kind.withConifers)
                        foreach (var (prefab, pos, rot, scale) in conifers)
                        {
                            var go = Make(prefab, pos, rot, scale, AreaOf(pos));
                            if (go == null)
                                continue;
                            Dress(go, prefab, LocalBounds(prefab), scale, Category.Tree, false, trees);
                            made++;
                        }
                    EditorUtility.ClearProgressBar();
                    return host;
                });
                // A kept layer already has its own lamp heads and signal heads; drop the ones made for this build.
                if (!generated)
                    foreach (int i in placed)
                        if (scatter.Attached.TryGetValue(i, out var parts))
                            foreach (var t in parts)
                                if (t != null)
                                    Object.DestroyImmediate(t.gameObject);
                roots.Add(layer.transform);
                count += layer.GetComponentsInChildren<Transform>(true).Count(t => t.parent != null && t.parent.name.StartsWith("Area "));
            }
            // The conifers now live in "Trees and palms"; the terrain no longer draws its own copies.
            terrain.terrainData.SetTreeInstances(new TreeInstance[0], true);
            Debug.Log($"[CityObjects] {count} editable objects ({made} generated this build) in {roots.Count} layers.");
            return roots.ToArray();
        }

        // Terrain trees (the conifers) as (prefab, position, rotation, scale).
        static List<(GameObject, Vector3, Quaternion, Vector3)> TerrainTrees(Terrain terrain)
        {
            var list = new List<(GameObject, Vector3, Quaternion, Vector3)>();
            var data = terrain.terrainData;
            var protos = data.treePrototypes;
            foreach (var t in data.treeInstances)
            {
                if (t.prototypeIndex < 0 || t.prototypeIndex >= protos.Length || protos[t.prototypeIndex].prefab == null)
                    continue;
                var pos = Vector3.Scale(t.position, data.size) + terrain.transform.position;
                list.Add((protos[t.prototypeIndex].prefab, pos, Quaternion.Euler(0f, t.rotation * Mathf.Rad2Deg, 0f),
                          new Vector3(t.widthScale, t.heightScale, t.widthScale)));
            }
            return list;
        }

        static GameObject Make(GameObject prefab, Vector3 pos, Quaternion rot, Vector3 scale, Transform parent)
        {
            if (prefab == null)
                return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (go == null)
                return null;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            EditorAssetUtil.StripRuntimeComponents(go);
            return go;
        }

        // Layer (cull distance), shadows and a collider that fits the kind of object, in the object's own space.
        static readonly Dictionary<GameObject, Bounds> BoundsCache = new Dictionary<GameObject, Bounds>();

        // The model's bounds in its own space (measured on a copy at the origin).
        static Bounds LocalBounds(GameObject prefab)
        {
            if (BoundsCache.TryGetValue(prefab, out var b))
                return b;
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            temp.transform.localScale = Vector3.one;
            b = EditorAssetUtil.RendererBounds(temp);
            Object.DestroyImmediate(temp);
            BoundsCache[prefab] = b;
            return b;
        }

        static void Dress(GameObject go, GameObject prefab, Bounds local, Vector3 scale, Category category, bool vehicle, int layer)
        {
            var settings = ScatterCollector.Settings(category);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.gameObject.layer = layer;
                r.shadowCastingMode = settings.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            go.layer = layer;
            float height = local.size.y * Mathf.Abs(scale.y);
            if (height < 0.45f)
                return;   // kerb details, litter: nothing to bump into
            var box = go.AddComponent<BoxCollider>();
            if ((category == Category.Tree && !vehicle) || category == Category.Lamp)
            {
                // A trunk or a pole, not the crown or the arm.
                float w = category == Category.Lamp ? 0.3f : 0.5f, h = category == Category.Lamp ? height : Mathf.Min(height, 4f);
                var s = new Vector3(Mathf.Max(0.001f, Mathf.Abs(scale.x)), Mathf.Max(0.001f, Mathf.Abs(scale.y)), Mathf.Max(0.001f, Mathf.Abs(scale.z)));
                box.size = new Vector3(w / s.x, h / s.y, w / s.z);
                // Lamps: the pole, wherever the model's pivot is (its arm reaches over the road).
                var foot = category == Category.Lamp ? PcShowcaseBuilder.PoleGeometry(prefab).pole : new Vector2(local.center.x, local.center.z);
                box.center = new Vector3(foot.x, local.min.y + box.size.y * 0.5f, foot.y);
            }
            else
            {
                box.center = local.center;
                box.size = local.size;
            }
        }
    }
}
