using System.Collections.Generic;
using System.IO;
using System.Text;
using KR.Editor.Kigali;
using KR.Gameplay.Player;
using UnityEditor;
using UnityEngine;

namespace KR.Editor.Batch
{
    // Measures the player's car in the Story scene and writes Assets/Resources/Car/car_parts.json, read at run time
    // by CarMirrors and the dashboard screen (the car mesh is not readable in a player build):
    //   mirrors    each mirror glass (the PolyDrive "Mirror" material), found by grouping its triangles:
    //              centre, outward normal, in-plane axes and size, in the car's local space;
    //   cluster    the instrument cluster behind the steering wheel (the "_Dashboard" part and its needles).
    // Run: Unity -batchmode -projectPath <KR> -executeMethod KR.Editor.Batch.CarParts.Export
    public static class CarParts
    {
        public const string OutPath = "Assets/Resources/Car/car_parts.json";

        public static void Export()
        {
            int errors = 0;
            try
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(StoryModeBuilder.ScenePath);
                var car = Object.FindFirstObjectByType<DrivableCar>(FindObjectsInactive.Include);
                if (car == null)
                    throw new System.Exception("No DrivableCar in the Story scene.");
                var t = car.transform;
                var log = new StringBuilder();
                Vector3 eye = car.EyePoint;
                log.AppendLine($"car {t.position:F3} rot {t.eulerAngles:F1} scale {t.lossyScale:F3}; eye local {t.InverseTransformPoint(eye):F3}");

                var mirrors = new List<string>();
                foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mats = r.sharedMaterials;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null)
                        continue;
                    for (int s = 0; s < mats.Length && s < mf.sharedMesh.subMeshCount; s++)
                        if (mats[s] != null && mats[s].name.Contains("Mirror") && r.name.Contains("LOD0"))
                            Mirrors(t, r.transform, mf.sharedMesh, s, eye, mirrors, log);
                }

                string cluster = "null";
                var dash = Find(t, "_Dashboard");
                if (dash != null && dash.TryGetComponent<Renderer>(out var dr))
                {
                    var b = dr.bounds;
                    cluster = $"{{\"centre\":{V(t.InverseTransformPoint(b.center))},\"size\":{V(Vector3.Scale(b.size, Inv(t.lossyScale)))}}}";
                    log.AppendLine($"_Dashboard bounds local centre {t.InverseTransformPoint(b.center):F3} size {b.size:F3}");
                }
                foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
                {
                    if (!(c.name.StartsWith("Needle") || c.name.StartsWith("Dashboard_Icon") || c.name == "SteeringWheel" || c.name.Contains("Steering wheel pivot")))
                        continue;
                    var rr = c.GetComponent<Renderer>();
                    string m = rr != null && rr.sharedMaterial != null ? rr.sharedMaterial.name : "-";
                    log.AppendLine($"{c.name}: local {t.InverseTransformPoint(c.position):F3} size {(rr != null ? rr.bounds.size.ToString("F3") : "-")} mat {m}");
                }
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null && (m.name.Contains("Indicator") || m.name.Contains("Mirror")))
                            log.AppendLine($"material {m.name} on {r.name}");

                Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
                File.WriteAllText(OutPath, $"{{\"mirrors\":[{string.Join(",", mirrors)}],\"cluster\":{cluster}}}\n");
                AssetDatabase.ImportAsset(OutPath);
                Debug.Log("[CarParts]\n" + log);
                File.WriteAllText("Temp/car_parts.txt", log.ToString());
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                errors++;
            }
            EditorApplication.Exit(errors == 0 ? 0 : 1);
        }

        // Groups the mirror triangles into separate pieces of glass (triangles whose centres lie within 0.3 m).
        static void Mirrors(Transform car, Transform part, Mesh mesh, int sub, Vector3 eye, List<string> output, StringBuilder log)
        {
            var verts = mesh.vertices;
            var tris = mesh.GetTriangles(sub);
            var groups = new List<(Vector3 sum, Vector3 normal, float area, List<Vector3> points)>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = car.InverseTransformPoint(part.TransformPoint(verts[tris[i]]));
                Vector3 b = car.InverseTransformPoint(part.TransformPoint(verts[tris[i + 1]]));
                Vector3 c = car.InverseTransformPoint(part.TransformPoint(verts[tris[i + 2]]));
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float area = cross.magnitude * 0.5f;
                if (area < 1e-7f)
                    continue;
                Vector3 centre = (a + b + c) / 3f;
                int g = groups.FindIndex(x => (x.sum / x.area - centre).sqrMagnitude < 0.09f);
                if (g < 0)
                {
                    groups.Add((Vector3.zero, Vector3.zero, 0f, new List<Vector3>()));
                    g = groups.Count - 1;
                }
                var x0 = groups[g];
                x0.points.AddRange(new[] { a, b, c });
                Vector3 n = cross.normalized;
                // Normals all on the same side of the glass.
                if (x0.area > 0f && Vector3.Dot(n, x0.normal) < 0f)
                    n = -n;
                groups[g] = (x0.sum + centre * area, x0.normal + n * area, x0.area + area, x0.points);
            }
            Vector3 eyeLocal = car.InverseTransformPoint(eye);
            foreach (var g in groups)
            {
                if (g.area < 0.002f)
                    continue;
                Vector3 centre = g.sum / g.area;
                Vector3 normal = g.normal.normalized;
                if (Vector3.Dot(normal, eyeLocal - centre) < 0f)
                    normal = -normal;   // the reflecting side faces the driver
                Vector3 right = Vector3.Cross(Vector3.up, normal).normalized;
                Vector3 up = Vector3.Cross(normal, right).normalized;
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                foreach (var p in g.points)
                {
                    float x = Vector3.Dot(p - centre, right), y = Vector3.Dot(p - centre, up);
                    x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                }
                centre += right * (x0 + x1) * 0.5f + up * (y0 + y1) * 0.5f;
                output.Add($"{{\"centre\":{V(centre)},\"normal\":{V(normal)},\"right\":{V(right)},\"up\":{V(up)},\"width\":{x1 - x0:0.0000},\"height\":{y1 - y0:0.0000}}}");
                log.AppendLine($"mirror at {centre:F3} normal {normal:F2} {x1 - x0:0.000} x {y1 - y0:0.000} m, area {g.area:0.0000}");
            }
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var c in root.GetComponentsInChildren<Transform>(true))
                if (c.name == name)
                    return c;
            return null;
        }

        static Vector3 Inv(Vector3 s) => new Vector3(1f / s.x, 1f / s.y, 1f / s.z);

        static string V(Vector3 v) => string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                                    "{{\"x\":{0:0.0000},\"y\":{1:0.0000},\"z\":{2:0.0000}}}", v.x, v.y, v.z);
    }
}
