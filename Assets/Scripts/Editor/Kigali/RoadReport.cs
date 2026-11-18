using System.Collections.Generic;
using System.Linq;
using System.Text;
using KR.Gameplay.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KR.Editor.Kigali
{
    // Drives a probe along every street of the Story scene (the satnav's street network) and reports where a car
    // would struggle: a ledge (the surface jumps more than 8 cm within half a metre beyond the road's own slope),
    // a gap with no road under the centreline, a road standing well above the ground beside it, or a grade too
    // steep to climb (> 25 % over 10 m). Writes the worst places with their coordinates to the Console and
    // <project>/Temp/road_report.txt.
    internal static class RoadReport
    {
        const float Step = 0.5f, MaxLedge = 0.08f, MaxGrade = 0.25f, MaxLift = 0.7f;

        [MenuItem("Kigali Rush/Story/Road report (steps, gaps, steep, floating)")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(StoryModeBuilder.ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var net = AssetDatabase.LoadAssetAtPath<StreetNetwork>("Assets/World/Story/Streets.asset");
            var terrain = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Where(t => !t.name.StartsWith("Surround"))
                                .OrderBy(t => t.transform.position.sqrMagnitude).FirstOrDefault();
            if (net == null || terrain == null)
            {
                Debug.LogError("[RoadReport] Build the Story scene and Story Mode first (Streets.asset, terrain).");
                return;
            }
            var issues = new List<(string kind, float size, Vector3 at, string street)>();
            float metres = 0f;
            foreach (var s in net.streets)
            {
                float? prev = null, prevRise = null;
                var recent = new Queue<float>();
                for (int i = 0; i + 1 < s.points.Length; i++)
                {
                    Vector3 a = s.points[i], b = s.points[i + 1];
                    float len = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                    for (float d = 0f; d < len; d += Step)
                    {
                        var p = Vector3.Lerp(a, b, d / Mathf.Max(len, 0.01f));
                        metres += Step;
                        float ground = terrain.SampleHeight(p) + terrain.transform.position.y;
                        if (!RoadTop(p, ground, out float top))
                        {
                            issues.Add(("gap", 0f, p, s.name));
                            prev = prevRise = null;
                            recent.Clear();
                            continue;
                        }
                        if (top - ground > MaxLift)
                            issues.Add(("floating", top - ground, p, s.name));
                        if (prev.HasValue)
                        {
                            float rise = top - prev.Value;
                            // A ledge: the rise over this half metre differs sharply from the one before (slope alone is fine).
                            if (prevRise.HasValue && Mathf.Abs(rise - prevRise.Value) > MaxLedge)
                                issues.Add(("ledge", Mathf.Abs(rise - prevRise.Value), p, s.name));
                            prevRise = rise;
                        }
                        recent.Enqueue(top);
                        if (recent.Count > 20)
                        {
                            float grade = Mathf.Abs(top - recent.Dequeue()) / (20 * Step);
                            if (grade > MaxGrade)
                                issues.Add(("steep", grade, p, s.name));
                        }
                        prev = top;
                    }
                }
            }
            var sb = new StringBuilder();
            sb.AppendLine($"Road report: {metres / 1000f:0.0} km of street probed every {Step} m.");
            foreach (var g in issues.GroupBy(x => x.kind))
            {
                sb.AppendLine($"{g.Key}: {g.Count()} samples");
                foreach (var x in g.OrderByDescending(x => x.size).Take(15))
                    sb.AppendLine($"   {x.size:0.00} at ({x.at.x:0}, {x.at.z:0}) on {(string.IsNullOrEmpty(x.street) ? "unnamed street" : x.street)}");
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "..", "Temp", "road_report.txt"), sb.ToString());
            Debug.Log("[RoadReport] " + sb);
        }

        // Highest road-surface collider under p (within a few metres of the ground).
        static bool RoadTop(Vector3 p, float ground, out float top)
        {
            top = float.MinValue;
            var from = new Vector3(p.x, ground + 6f, p.z);
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider is MeshCollider && hit.transform.parent != null && hit.transform.parent.name == PcRoadBuilder.GroupName)
                    top = Mathf.Max(top, hit.point.y);
            return top > float.MinValue;
        }
    }
}
