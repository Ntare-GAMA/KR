using System.Collections.Generic;
using System.Linq;
using KR.Editor.Kigali;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KR.Editor.Perf
{
    // LOD groups for the city's many small objects, which had none (Logs/perf_report.txt before this: 14,886
    // parked-car renderers with 16.8 M triangles, 9,844 street-furniture renderers, 7,331 lamp and signal renderers,
    // 12,998 building renderers, all with no LOD). Per object:
    //   parked cars   LOD0 full model > LOD1 two-box proxy in the car's own paint > culled
    //   moving traffic LOD0 > culled           street furniture LOD0 > culled
    //   lamps, poles  LOD0 > culled            buildings LOD0 > culled (far away)
    // Also marks the roads and road edges batching-static (they never move). Thresholds are screen-relative
    // heights; on a Quest (about 96° vertical view) a 4.5 m car switches to its proxy at about 55 m and goes at 160 m.
    // Saves the scene and keeps the editable layers (EditableLayer.Keep). Menu: Kigali Rush > Performance > Add LODs.
    public static class LodSetup
    {
        const string ProxyMeshPath = "Assets/World/PC/kimironko_kibagabaga/LodProxyCar.asset";

        struct Rule
        {
            public string layer;
            public float lod0, lod1;   // lod1 <= 0: no proxy level, culled below lod0
            public bool proxy;
        }

        static readonly Rule[] Rules =
        {
            new Rule { layer = "Parked cars", lod0 = 0.035f, lod1 = 0.012f, proxy = true },
            new Rule { layer = "Street furniture", lod0 = 0.015f },
            new Rule { layer = "Street lamps and signal poles", lod0 = 0.01f },
            new Rule { layer = "City buildings", lod0 = 0.006f },
        };

        static readonly string[] StaticGroups =
        {
            "Roads (photo asphalt, markings, kerbs, footpaths, crossings)",
            "Road edges (skirts into the ground; no collision)",
        };

        [MenuItem("Kigali Rush/Performance/Add LODs and static batching")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(PerfReport.StoryScene, OpenSceneMode.Single);
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
            var proxyMesh = ProxyMesh();
            var report = new List<string>();

            foreach (var rule in Rules)
            {
                var layer = all.FirstOrDefault(t => t.name == rule.layer);
                if (layer == null)
                {
                    report.Add($"(no layer {rule.layer})");
                    continue;
                }
                int added = 0;
                foreach (Transform area in layer)
                    foreach (Transform obj in area)
                        if (AddLod(obj.gameObject, rule, proxyMesh))
                            added++;
                report.Add($"{rule.layer}: {added} LOD groups");
            }

            // Moving traffic: each "Vehicle N" holder (its model and baked lamp mesh) is culled when small on screen.
            var traffic = all.FirstOrDefault(t => t.name.StartsWith("Traffic (in lane"));
            if (traffic != null)
            {
                int n = 0;
                foreach (Transform holder in traffic)
                    if (AddLod(holder.gameObject, new Rule { layer = "Traffic", lod0 = 0.02f }, null))
                        n++;
                report.Add($"Traffic: {n} LOD groups");
            }

            int statics = 0;
            foreach (var name in StaticGroups)
            {
                var g = all.FirstOrDefault(t => t.name == name);
                if (g == null)
                    continue;
                foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags | StaticEditorFlags.BatchingStatic);
                    statics++;
                }
            }
            report.Add($"Roads and road edges: {statics} renderers batching-static");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditableLayer.Keep();
            Debug.Log("[LODs] " + string.Join("; ", report));
        }

        static bool AddLod(GameObject obj, Rule rule, Mesh proxyMesh)
        {
            if (obj.GetComponent<LODGroup>() != null || obj.GetComponentInChildren<LODGroup>(true) != null)
                return false;
            var renderers = obj.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled).Cast<Renderer>().ToArray();
            if (renderers.Length == 0)
                return false;
            var group = obj.AddComponent<LODGroup>();
            LOD[] lods;
            if (rule.proxy && proxyMesh != null && rule.lod1 > 0f && MakeProxy(obj, renderers, proxyMesh) is Renderer proxy)
                lods = new[] { new LOD(rule.lod0, renderers), new LOD(rule.lod1, new[] { proxy }) };
            else
                lods = new[] { new LOD(rule.lod0, renderers) };
            group.SetLODs(lods);
            group.fadeMode = LODFadeMode.None;
            group.RecalculateBounds();
            return true;
        }

        // A two-box car (body and cabin) sized to the car's bounds in its own space, in the paint of its largest part.
        static Renderer MakeProxy(GameObject car, Renderer[] parts, Mesh mesh)
        {
            var t = car.transform;
            bool any = false;
            Bounds local = default;
            Renderer biggest = null;
            float biggestSize = 0f;
            foreach (var r in parts)
            {
                var b = r.bounds;
                float size = b.size.sqrMagnitude;
                if (size > biggestSize)
                {
                    biggestSize = size;
                    biggest = r;
                }
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    var p = t.InverseTransformPoint(corner);
                    if (!any)
                    {
                        local = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else
                        local.Encapsulate(p);
                }
            }
            if (!any || biggest == null || local.size.y < 0.5f)
                return null;
            var go = new GameObject("LOD1 proxy");
            go.transform.SetParent(t, false);
            go.transform.localPosition = local.center;
            go.transform.localScale = local.size;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = biggest.sharedMaterials.FirstOrDefault(m => m != null);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mr;
        }

        // Unit-cube car: a body box (lower 55%) and a narrower, shorter cabin on top, centred at the origin.
        static Mesh ProxyMesh()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ProxyMeshPath);
            if (mesh != null)
                return mesh;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            void Box(Vector3 min, Vector3 max)
            {
                int b = verts.Count;
                for (int i = 0; i < 8; i++)
                    verts.Add(new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z));
                int[] faces = { 0, 2, 3, 0, 3, 1,  4, 5, 7, 4, 7, 6,  0, 4, 6, 0, 6, 2,  1, 3, 7, 1, 7, 5,  2, 6, 7, 2, 7, 3,  0, 1, 5, 0, 5, 4 };
                foreach (int f in faces)
                    tris.Add(b + f);
            }
            Box(new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.05f, 0.5f));
            Box(new Vector3(-0.42f, 0.05f, -0.25f), new Vector3(0.42f, 0.5f, 0.22f));
            // Flat-shaded: split vertices per triangle so each face gets its own normal.
            var flatV = new List<Vector3>();
            var flatT = new List<int>();
            foreach (int i in tris)
            {
                flatT.Add(flatV.Count);
                flatV.Add(verts[i]);
            }
            mesh = new Mesh { name = "LOD proxy car" };
            mesh.SetVertices(flatV);
            mesh.SetTriangles(flatT, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, ProxyMeshPath);
            return mesh;
        }
    }
}
