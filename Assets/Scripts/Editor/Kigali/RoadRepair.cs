using System.Collections.Generic;
using System.Linq;
using System.Text;
using KR.Gameplay.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace KR.Editor.Kigali
{
    // Repairs the Story scene's road surface where the tester found faults (owner 2026-11-23):
    //  1. "places that are not filled ... it's hard to pass through": along every street (the satnav network), at the
    //     centreline and across the carriageway, wherever the ground shows through the road (terrain at or above the
    //     road surface) the terrain is lowered 25 cm under it; wherever there is no road surface at all between road on
    //     either side (up to 30 m), an asphalt patch with a collider fills the gap at the height of the road around it.
    //  2. "zebra crossing marks mid air, they should be on the road embedded": every zebra bar and stop line is laid
    //     back onto the road surface under it (the markings layer is kept across builds, so it had stayed at the
    //     height of an older road surface).
    // Writes what it did to the Console and <project>/Temp/road_repair.txt. Rebuilding the roads (Story scene build)
    // replaces the patches; run this again after a rebuild.
    internal static class RoadRepair
    {
        const float Step = 0.5f, Paint = 0.012f, TerrainBelow = 0.25f, MaxGap = 30f;
        const string MarkingsName = "Road markings (zebra crossings, stop lines)";
        const string PatchName = "Road patches (filled gaps)";
        const string PatchMeshPath = "Assets/World/Story/RoadPatches.asset";

        [MenuItem("Kigali Rush/Story/Repair roads (fill gaps, lower ground, lay markings)")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(StoryModeBuilder.ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var net = AssetDatabase.LoadAssetAtPath<StreetNetwork>("Assets/World/Story/Streets.asset");
            var terrain = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Where(t => !t.name.StartsWith("Surround"))
                                .OrderBy(t => t.transform.position.sqrMagnitude).FirstOrDefault();
            var group = GameObject.Find(PcRoadBuilder.GroupName);
            if (net == null || terrain == null || group == null)
            {
                Debug.LogError("[RoadRepair] Needs the Story scene with its roads, terrain and street network.");
                return;
            }
            var sb = new StringBuilder();

            // Old patches out first, so they are measured afresh.
            var old = group.transform.Find(PatchName);
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            Physics.SyncTransforms();

            int lowered = LowerGround(net, terrain, sb);
            Physics.SyncTransforms();
            int patched = FillGaps(net, terrain, group.transform, sb);
            Physics.SyncTransforms();
            int laid = LayMarkings(sb);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditableLayer.Keep();
            AssetDatabase.SaveAssets();
            sb.Insert(0, $"Road repair: {lowered} terrain points lowered under the road, {patched} gap samples filled, {laid} marking vertices laid on the road.\n");
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "..", "Temp", "road_repair.txt"), sb.ToString());
            Debug.Log("[RoadRepair] " + sb);
        }

        // ---------- Samples across the carriageway ----------

        static IEnumerable<(Vector3 p, Vector3 side, float t)> Samples(StreetNetwork.Street s)
        {
            for (int i = 0; i + 1 < s.points.Length; i++)
            {
                Vector3 a = s.points[i], b = s.points[i + 1];
                var flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
                float len = flat.magnitude;
                if (len < 0.01f)
                    continue;
                var side = new Vector3(flat.z, 0f, -flat.x) / len;
                for (float d = 0f; d < len; d += Step)
                    yield return (Vector3.Lerp(a, b, d / len), side, i + d / len);
            }
        }

        static float Ground(Terrain t, Vector3 p) => t.SampleHeight(p) + t.transform.position.y;

        static bool RoadTop(Vector3 p, float ground, out float top)
        {
            top = float.MinValue;
            var from = new Vector3(p.x, ground + 6f, p.z);
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider is MeshCollider && hit.transform.parent != null && hit.transform.parent.name == PcRoadBuilder.GroupName && hit.normal.y > 0.6f)
                    top = Mathf.Max(top, hit.point.y);
            return top > float.MinValue;
        }

        // ---------- 1a. Ground showing through the road ----------

        static int LowerGround(StreetNetwork net, Terrain terrain, StringBuilder sb)
        {
            var data = terrain.terrainData;
            int res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var origin = terrain.transform.position;
            var size = data.size;
            int changed = 0;
            var where = new List<Vector3>();
            foreach (var s in net.streets)
            {
                float half = Mathf.Max(2f, s.halfWidth);
                foreach (var (p, side, _) in Samples(s))
                    for (float lat = -0.8f; lat <= 0.81f; lat += 0.4f)
                    {
                        var q = p + side * (lat * half);
                        float ground = Ground(terrain, q);
                        if (!RoadTop(q, ground, out float top) || ground < top - 0.05f)
                            continue;
                        // Lower every heightmap point within a metre and a half to well under the road there.
                        float want = (top - TerrainBelow - origin.y) / size.y;
                        int cx = Mathf.RoundToInt((q.x - origin.x) / size.x * (res - 1));
                        int cz = Mathf.RoundToInt((q.z - origin.z) / size.z * (res - 1));
                        int r = Mathf.CeilToInt(1.5f / (size.x / (res - 1)));
                        for (int z = cz - r; z <= cz + r; z++)
                            for (int x = cx - r; x <= cx + r; x++)
                                if (x >= 0 && z >= 0 && x < res && z < res && heights[z, x] > want)
                                {
                                    heights[z, x] = want;
                                    changed++;
                                }
                        if (where.Count < 400)
                            where.Add(q);
                    }
            }
            if (changed > 0)
            {
                Undo.RegisterCompleteObjectUndo(data, "Lower ground under roads");
                data.SetHeights(0, 0, heights);
                EditorUtility.SetDirty(data);
                terrain.Flush();
            }
            sb.AppendLine($"Ground through the road: {where.Count} samples (first ones):");
            foreach (var q in Cluster(where).Take(20))
                sb.AppendLine($"   ({q.x:0}, {q.z:0})");
            return changed;
        }

        // ---------- 1b. No road at all between road on either side ----------

        static int FillGaps(StreetNetwork net, Terrain terrain, Transform group, StringBuilder sb)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var uvs = new List<Vector2>();
            var where = new List<Vector3>();
            int filled = 0;
            foreach (var s in net.streets)
            {
                float half = Mathf.Max(2f, s.halfWidth);
                for (float lat = -0.7f; lat <= 0.71f; lat += 0.35f)
                {
                    // Walk this line along the street; a run of samples with no road between two with road is a gap.
                    var run = new List<(Vector3 q, Vector3 side)>();
                    float? before = null;
                    foreach (var (p, side, _) in Samples(s))
                    {
                        var q = p + side * (lat * half);
                        float ground = Ground(terrain, q);
                        if (RoadTop(q, ground, out float top))
                        {
                            if (before.HasValue && run.Count > 0 && run.Count * Step <= MaxGap)
                            {
                                for (int k = 0; k < run.Count; k++)
                                {
                                    float y = Mathf.Lerp(before.Value, top, (k + 1f) / (run.Count + 1f)) - 0.004f;
                                    Quad(verts, tris, uvs, run[k].q, run[k].side, y, half * 0.35f + 0.15f);
                                    filled++;
                                    if (where.Count < 400)
                                        where.Add(run[k].q);
                                }
                            }
                            run.Clear();
                            before = top;
                        }
                        else
                            run.Add((q, side));
                    }
                }
            }
            if (verts.Count > 0)
            {
                var mesh = new Mesh { name = "Road patches", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                AssetDatabase.DeleteAsset(PatchMeshPath);
                AssetDatabase.CreateAsset(mesh, PatchMeshPath);
                var go = new GameObject(PatchName);
                go.transform.SetParent(group, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Asphalt(group);
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                go.isStatic = true;
                go.layer = group.gameObject.layer;
            }
            sb.AppendLine($"Gaps with no road: {where.Count} samples (first ones):");
            foreach (var q in Cluster(where).Take(20))
                sb.AppendLine($"   ({q.x:0}, {q.z:0})");
            return filled;
        }

        // A flat piece of road Step long and 2 * halfWide across, centred on q, at height y.
        static void Quad(List<Vector3> v, List<int> t, List<Vector2> uv, Vector3 q, Vector3 side, float y, float halfWide)
        {
            var fwd = new Vector3(-side.z, 0f, side.x) * (Step * 0.6f);
            var c = new Vector3(q.x, y, q.z);
            int i = v.Count;
            v.Add(c - fwd - side * halfWide);
            v.Add(c - fwd + side * halfWide);
            v.Add(c + fwd + side * halfWide);
            v.Add(c + fwd - side * halfWide);
            foreach (var p in v.Skip(i))
                uv.Add(new Vector2(p.x * 0.25f, p.z * 0.25f));
            // Facing up whatever the winding of side and forward.
            var n = Vector3.Cross(v[i + 1] - v[i], v[i + 2] - v[i]);
            if (n.y >= 0f) { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
            else { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
        }

        static Material Asphalt(Transform group)
        {
            foreach (var mr in group.GetComponentsInChildren<MeshRenderer>())
                if (mr.sharedMaterial != null && mr.sharedMaterial.name.StartsWith("Asphalt"))
                    return mr.sharedMaterial;
            var any = group.GetComponentInChildren<MeshRenderer>();
            return any != null ? any.sharedMaterial : null;
        }

        // ---------- 2. Markings onto the road ----------

        static int LayMarkings(StringBuilder sb)
        {
            var root = GameObject.Find(MarkingsName);
            if (root == null)
            {
                sb.AppendLine("Markings: layer not found.");
                return 0;
            }
            int moved = 0, bars = 0;
            float worst = 0f;
            var done = new HashSet<Mesh>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || !done.Add(mesh))
                    continue;
                var t = mf.transform;
                var v = mesh.vertices;
                bool any = false;
                for (int i = 0; i < v.Length; i++)
                {
                    var w = t.TransformPoint(v[i]);
                    // The road surface right under the paint, searched from 3 m above it to 4 m below.
                    float top = float.MinValue;
                    foreach (var hit in Physics.RaycastAll(w + Vector3.up * 3f, Vector3.down, 7f, ~0, QueryTriggerInteraction.Ignore))
                        if (hit.collider is MeshCollider && hit.transform.parent != null && hit.transform.parent.name == PcRoadBuilder.GroupName && hit.normal.y > 0.6f)
                            top = Mathf.Max(top, hit.point.y);
                    if (top == float.MinValue)
                        continue;
                    float want = top + Paint;
                    if (Mathf.Abs(w.y - want) < 0.003f)
                        continue;
                    worst = Mathf.Max(worst, Mathf.Abs(w.y - want));
                    w.y = want;
                    v[i] = t.InverseTransformPoint(w);
                    moved++;
                    any = true;
                }
                if (!any)
                    continue;
                bars++;
                mesh.vertices = v;
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                EditorUtility.SetDirty(mesh);
            }
            sb.AppendLine($"Markings: {bars} bars and stop lines laid on the road (largest move {worst:0.00} m).");
            return moved;
        }

        // One point per 10 m cell, so the report lists places rather than every sample.
        static IEnumerable<Vector3> Cluster(List<Vector3> points) =>
            points.GroupBy(p => (Mathf.FloorToInt(p.x / 10f), Mathf.FloorToInt(p.z / 10f))).Select(g => g.First());
    }
}
