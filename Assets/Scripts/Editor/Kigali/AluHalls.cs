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
    // ALU campus, second approach (owner request 2026-11-21: "it almost aligns but not perfectly, try something else;
    // insides accessible, the parking lot and everything"). Instead of fitting a rectangle and a 16 m module grid to
    // each OSM footprint, every building now follows its real OSM outline edge by edge:
    //   - storeys from the OSM height (3.6 m each); the ground floor sits level with the highest ground under the
    //     footprint, so it is entered at grade on the uphill side and stands on a brick plinth downhill (the campus
    //     cascades down the hill);
    //   - clay-brick walls with tall blue-tinted windows in a 3.2 m rhythm, slab bands at each floor, round brick
    //     pods at the sharp corners, a parapet and roof;
    //   - the ground floor is hollow and walkable: glass double doors on the edges that meet the ground, a polished
    //     floor, a ceiling with lights, and open-plan learning spaces (study tables, small classrooms with
    //     whiteboards, lounges, open-brick screens), a reception desk inside the main door;
    //   - a car park inside the gate (20 marked bays, some cars), with a brick path to the nearest entrance.
    //     TODO: verify the car park's position against the owner's footage: OSM does not map it.
    // Upper floors are not reachable (no stairs yet). The perimeter fence and the pond from the first approach stay.
    internal static partial class AluCampus
    {
        const string District = "kimironko_kibagabaga";
        const string OutDir = "Assets/World/PC/" + District;

        sealed class Inside
        {
            public Material Floor, Ceiling, Desk, Chair, Board, Sofa, Asphalt, Line;
        }

        [MenuItem("Kigali Rush/Story/Rebuild ALU campus (from OSM outlines)")]
        public static void RebuildHalls()
        {
            var scene = EditorSceneManager.OpenScene(StoryModeBuilder.ScenePath, OpenSceneMode.Single);
            var old = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                           .FirstOrDefault(t => t.name == $"{PlaceName} (campus)");
            var terrain = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                .Where(t => !t.name.StartsWith("Surround")).OrderBy(t => t.transform.position.sqrMagnitude).FirstOrDefault();
            if (old == null || terrain == null)
            {
                Debug.LogError("[ALU] No campus or terrain in the Story scene.");
                return;
            }
            string src = Path.Combine(DistrictImporter.SourceDir, District);
            var buildings = JsonUtility.FromJson<DistrictImporter.BuildingList>(File.ReadAllText(Path.Combine(src, "buildings_unity.json"))).buildings;
            var roads = JsonUtility.FromJson<DistrictImporter.RoadList>(File.ReadAllText(Path.Combine(src, "roads_unity.json"))).roads;
            var places = JsonUtility.FromJson<PcCityLife.PlaceList>(File.ReadAllText(Path.Combine(src, "places_unity.json"))).places;
            var site = Find(places);
            if (site == null)
            {
                Debug.LogError("[ALU] Campus not in places_unity.json.");
                return;
            }
            var parent = old.parent;
            Object.DestroyImmediate(old.gameObject);
            var c = new Context
            {
                Terrain = terrain, Root = parent, OutDir = OutDir, Cat = PcShowcaseBuilder.LoadCatalog(),
                Rng = new System.Random(1962), Roads = roads,
            };
            BuildHalls(c, site, buildings);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        static void BuildHalls(Context c, Site site, DistrictImporter.Building[] buildings)
        {
            var m = MakeMaterials(c.OutDir);
            var inside = new Inside
            {
                Floor = EditorAssetUtil.LitMaterial(c.OutDir, "ALU_Floor_Polished", new Color(0.72f, 0.7f, 0.66f), 0.55f),
                Ceiling = EditorAssetUtil.LitMaterial(c.OutDir, "ALU_Ceiling", new Color(0.95f, 0.94f, 0.92f), 0.05f),
                Desk = EditorAssetUtil.LitMaterial(c.OutDir, "ALU_Desk", new Color(0.62f, 0.45f, 0.3f), 0.4f),
                Chair = EditorAssetUtil.LitMaterial(c.OutDir, "ALU_Chair", new Color(0.13f, 0.14f, 0.16f), 0.35f),
                Board = EditorAssetUtil.LitMaterial(c.OutDir, "ALU_Whiteboard", new Color(0.97f, 0.97f, 0.97f), 0.8f),
                Sofa = EditorAssetUtil.LitMaterial(c.OutDir, "ALU_Sofa", new Color(0.2f, 0.36f, 0.4f), 0.15f),
                Asphalt = EditorAssetUtil.LitMaterial(c.OutDir, "ALU_CarPark", new Color(0.2f, 0.2f, 0.21f), 0.25f),
                Line = EditorAssetUtil.LitMaterial(c.OutDir, "ALU_BayLine", new Color(0.95f, 0.95f, 0.92f), 0.3f),
            };
            var geo = new Geo();
            var deco = new Geo();
            var root = new GameObject($"{PlaceName} (campus)").transform;
            root.SetParent(c.Root, false);
            var lights = new GameObject("Indoor lights").transform;
            lights.SetParent(root, false);

            var footprints = buildings.Where(b => b.pts.Length >= 6 && PcCityLife.Inside(site.Poly, Flat(PcCityLife.Centroid(b.pts))))
                                      .OrderByDescending(b => PcCityLife.Area(b.pts)).ToList();
            var entrances = new List<(Vector3 at, Vector3 outward)>();
            int halls = 0;
            foreach (var b in footprints)
            {
                var ring = Ring(b.pts);
                if (ring.Count < 3)
                    continue;
                Hall(c, geo, deco, m, inside, ring, b.h, lights, entrances, halls == 0);
                halls++;
            }
            foreach (var (at, _) in entrances)
            {
                var marker = new GameObject("Entrance").transform;
                marker.SetParent(root, false);
                marker.position = at;
            }
            // The car park beside the main entrance, a driveway in from the gate, palms along it and at the doors.
            var gate = Gate(c, site, out _);
            var park = entrances.Count > 0 ? CarPark(c, geo, deco, inside, site, footprints, root, entrances[0].at, entrances[0].outward) : null;
            if (park.HasValue)
            {
                var (pc, pf) = park.Value;
                var door = entrances.OrderBy(e => (e.at - pc).sqrMagnitude).First();
                BrickPath(c, geo, m, pc + pf * 9.5f, door.at + door.outward * 2.5f);
                var gate3 = new Vector3(gate.x, 0f, gate.y);
                var edge = pc - pf * 9.5f;
                Driveway(c, geo, deco, inside, gate3, edge);
                Palms(c, root, gate3, edge, 14f, 5.5f);
            }
            foreach (var (at, outward) in entrances)
            {
                var side = new Vector3(outward.z, 0f, -outward.x);
                Palms(c, root, at + outward * 4f + side * 3.5f, at + outward * 4f + side * 3.6f, 50f, 0f);
                Palms(c, root, at + outward * 4f - side * 3.5f, at + outward * 4f - side * 3.6f, 50f, 0f);
            }
            Pond(c, geo, deco, m, site, footprints);
            Fence(c, geo, deco, m, site, root);

            var store = new PcCityLife.MeshStore($"{c.OutDir}/ALU_Campus_Meshes.asset");
            geo.Emit("Campus (brick, floors, furniture)", root, store, collider: true);
            deco.Emit("Campus details (glass, chairs, lines)", root, store, collider: false);
            Debug.Log($"[ALU] Rebuilt from {halls} OSM outlines: {entrances.Count} entrances, car park {(park.HasValue ? park.Value.Item1.ToString("F0") : "none")}.");
        }

        // The footprint as a counter-clockwise ring (seen from above), without repeated or collinear points.
        static List<Vector3> Ring(float[] pts)
        {
            var ring = new List<Vector3>();
            for (int i = 0; i + 1 < pts.Length; i += 2)
            {
                var p = new Vector3(pts[i], 0f, pts[i + 1]);
                if (ring.Count == 0 || (ring[ring.Count - 1] - p).sqrMagnitude > 0.25f)
                    ring.Add(p);
            }
            if (ring.Count > 1 && (ring[0] - ring[ring.Count - 1]).sqrMagnitude < 0.25f)
                ring.RemoveAt(ring.Count - 1);
            for (int i = ring.Count - 1; i >= 0 && ring.Count > 3; i--)
            {
                var a = ring[(i + ring.Count - 1) % ring.Count];
                var p = ring[i];
                var q = ring[(i + 1) % ring.Count];
                if (Vector3.Cross((p - a).normalized, (q - p).normalized).magnitude < 0.06f)
                    ring.RemoveAt(i);
            }
            float area = 0f;
            for (int i = 0; i < ring.Count; i++)
                area += ring[i].x * ring[(i + 1) % ring.Count].z - ring[(i + 1) % ring.Count].x * ring[i].z;
            if (area < 0f)
                ring.Reverse();
            return ring;
        }

        static Vector3 Outward(Vector3 a, Vector3 b)
        {
            var d = (b - a).normalized;
            return new Vector3(d.z, 0f, -d.x);
        }

        // A straight piece of wall along edge a-b, from u0 to u1 metres along it, between heights y0 and y1.
        static void Wall(Geo g, Material mat, Vector3 a, Vector3 b, float u0, float u1, float y0, float y1, float thick, float outward = 0f)
        {
            if (u1 - u0 < 0.02f || y1 - y0 < 0.02f)
                return;
            var d = (b - a).normalized;
            var centre = a + d * ((u0 + u1) * 0.5f) + Outward(a, b) * outward;
            centre.y = (y0 + y1) * 0.5f;
            g.Box(mat, centre, new Vector3(thick, y1 - y0, u1 - u0), Quaternion.LookRotation(d));
        }

        static void Hall(Context c, Geo g, Geo deco, Mats m, Inside ins, List<Vector3> ring, float height, Transform lights,
                         List<(Vector3, Vector3)> entrances, bool main)
        {
            int storeys = Mathf.Clamp(Mathf.RoundToInt(height / Storey), 1, 3);
            // Ground under the outline and inside it.
            float gMin = float.MaxValue, gMax = float.MinValue;
            var poly = ring.Select(Flat).ToArray();
            foreach (var p in Samples(ring, 1.5f))
            {
                float y = Ground(c.Terrain, p);
                gMin = Mathf.Min(gMin, y);
                gMax = Mathf.Max(gMax, y);
            }
            float floor = gMax + 0.12f, top = floor + storeys * Storey;

            // Entrances: edges at least 6 m long where the ground just outside is within reach of the floor
            // (steps make up up to 1.2 m). The longest such edge, and one more on another side for big halls.
            var doors = new List<int>();
            var order = Enumerable.Range(0, ring.Count)
                .Select(i => (i, len: Vector3.Distance(ring[i], ring[(i + 1) % ring.Count]),
                              drop: floor - Ground(c.Terrain, Mid(ring[i], ring[(i + 1) % ring.Count]) + Outward(ring[i], ring[(i + 1) % ring.Count]) * 2.5f)))
                .Where(e => e.len >= 6f && e.drop > -0.3f && e.drop < 1.2f)
                .OrderBy(e => e.drop).ThenByDescending(e => e.len).ToList();
            if (order.Count > 0)
                doors.Add(order[0].i);
            if (PcCityLife.Area(ring.SelectMany(p => new[] { p.x, p.z }).ToArray()) > 1200f)
            {
                var other = order.Skip(1).FirstOrDefault(e => Vector3.Dot(Outward(ring[e.i], ring[(e.i + 1) % ring.Count]),
                                                                          Outward(ring[doors[0]], ring[(doors[0] + 1) % ring.Count])) < 0.3f);
                if (other.len > 0f)
                    doors.Add(other.i);
            }

            // Plinth down to the ground, and the floors.
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                float len = Vector3.Distance(a, b);
                Wall(g, m.Brick, a, b, -0.15f, len + 0.15f, gMin - 0.6f, floor, 0.3f);
            }
            g.Cap(ins.Floor, ring, floor, true);
            g.Cap(ins.Ceiling, ring, floor + Storey - 0.15f, false);
            g.Cap(m.Roof, ring, top, true);

            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                float len = Vector3.Distance(a, b);
                var dir = (b - a).normalized;
                var outN = Outward(a, b);
                int panels = Mathf.Max(1, Mathf.RoundToInt(len / 3.2f));
                float pw = len / panels;
                int doorPanel = doors.Contains(i) ? panels / 2 : -1;
                for (int s = 0; s < storeys; s++)
                {
                    float y0 = floor + s * Storey;
                    for (int k = 0; k < panels; k++)
                    {
                        float u0 = k * pw, u1 = u0 + pw;
                        if (s == 0 && k == doorPanel)
                        {
                            float w = Mathf.Min(2.4f, pw - 0.5f);
                            float d0 = (u0 + u1 - w) * 0.5f, d1 = d0 + w;
                            Wall(g, m.Brick, a, b, u0, d0, y0, y0 + Storey, 0.3f);
                            Wall(g, m.Brick, a, b, d1, u1, y0, y0 + Storey, 0.3f);
                            Wall(g, m.Brick, a, b, d0, d1, y0 + 2.8f, y0 + Storey, 0.3f);
                            // Glass double doors, standing open inwards; a dark frame.
                            foreach (float side in new[] { -1f, 1f })
                            {
                                var hinge = a + dir * (side < 0f ? d0 : d1) - outN * 0.1f;
                                var leafDir = Quaternion.AngleAxis(side * 80f, Vector3.up) * (-dir * side);
                                var leaf = hinge + leafDir * (w * 0.25f);
                                leaf.y = y0 + 1.35f;
                                deco.Box(m.Glass, leaf, new Vector3(0.05f, 2.7f, w * 0.5f), Quaternion.LookRotation(leafDir));
                            }
                            Wall(deco, m.Frame, a, b, d0 - 0.06f, d0, y0, y0 + 2.8f, 0.34f);
                            Wall(deco, m.Frame, a, b, d1, d1 + 0.06f, y0, y0 + 2.8f, 0.34f);
                            Wall(deco, m.Frame, a, b, d0, d1, y0 + 2.74f, y0 + 2.8f, 0.34f);
                            var at = a + dir * ((d0 + d1) * 0.5f);
                            at.y = y0;
                            entrances.Add((at, outN));
                            Steps(c, g, m, at, outN, dir, w + 0.8f, floor);
                            continue;
                        }
                        // Pier, window, pier; brick under the sill and over the head.
                        float pier = Mathf.Min(0.45f, pw * 0.2f);
                        float sill = s == 0 ? 0.9f : 1.0f, head = 3.0f;
                        Wall(g, m.Brick, a, b, u0, u1, y0, y0 + sill, 0.3f);
                        Wall(g, m.Brick, a, b, u0, u1, y0 + head, y0 + Storey, 0.3f);
                        Wall(g, m.Brick, a, b, u0, u0 + pier, y0 + sill, y0 + head, 0.3f);
                        Wall(g, m.Brick, a, b, u1 - pier, u1, y0 + sill, y0 + head, 0.3f);
                        Wall(deco, m.Glass, a, b, u0 + pier, u1 - pier, y0 + sill, y0 + head, 0.06f);
                        Wall(deco, m.Frame, a, b, u0 + pier, u1 - pier, y0 + sill - 0.05f, y0 + sill, 0.36f);
                    }
                    // Slab band at the top of each storey.
                    Wall(g, m.Cornice, a, b, -0.3f, len + 0.3f, y0 + Storey - 0.16f, y0 + Storey + 0.12f, 0.9f, 0.15f);
                }
                // Parapet.
                Wall(g, m.Cornice, a, b, -0.15f, len + 0.15f, top, top + 0.9f, 0.3f);
            }

            // Round brick pods at the sharp outer corners, rising above the roof.
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[(i + ring.Count - 1) % ring.Count];
                var p = ring[i];
                var q = ring[(i + 1) % ring.Count];
                var d1 = (p - a).normalized;
                var d2 = (q - p).normalized;
                bool convex = d1.x * d2.z - d1.z * d2.x > 0f;
                float turn = Vector3.Angle(d1, d2);
                if (convex && turn > 50f && Vector3.Distance(a, p) > 5f && Vector3.Distance(p, q) > 5f)
                {
                    g.Cylinder(m.Brick, m.Roof, p, 1.5f, gMin - 0.6f, top + 1.4f, 20);
                    g.Cylinder(m.Cornice, m.Roof, p, 1.8f, top + 1.4f, top + 1.7f, 20, bottom: true);
                }
            }

            Furnish(c, g, deco, m, ins, ring, poly, floor, lights, entrances, main);
        }

        // Points along the outline and across the inside, for sampling the ground.
        static IEnumerable<Vector3> Samples(List<Vector3> ring, float step)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / step));
                for (int k = 0; k < n; k++)
                    yield return Vector3.Lerp(a, b, (float)k / n);
            }
            var poly = ring.Select(Flat).ToArray();
            Bounds2(poly, out var min, out var max);
            for (float z = min.y; z <= max.y; z += step * 2f)
                for (float x = min.x; x <= max.x; x += step * 2f)
                    if (PcCityLife.Inside(poly, new Vector2(x, z)))
                        yield return new Vector3(x, 0f, z);
        }

        static Vector3 Mid(Vector3 a, Vector3 b) => (a + b) * 0.5f;

        // Brick steps from the ground outside a door up to the floor (or a level landing when it is at grade).
        static void Steps(Context c, Geo g, Mats m, Vector3 door, Vector3 outward, Vector3 along, float width, float floor)
        {
            var rot = Quaternion.LookRotation(outward);
            g.Box(m.Pavers, new Vector3(door.x, floor - 0.1f, door.z) + outward * 1.0f, new Vector3(width, 0.2f, 2.0f), rot);
            float ground = Ground(c.Terrain, door + outward * 2.5f);
            float rise = floor - ground;
            int n = Mathf.Clamp(Mathf.CeilToInt(rise / 0.17f), 0, 10);
            for (int k = 0; k < n; k++)
            {
                float y = floor - (k + 1) * rise / n;
                var centre = new Vector3(door.x, (y + ground - 0.4f) * 0.5f, door.z) + outward * (2.0f + 0.3f * k + 0.15f);
                g.Box(m.Pavers, centre, new Vector3(width, y - ground + 0.4f + 0.001f, 0.3f), rot);
            }
        }

        // The ground floor as open-plan learning spaces on a 6.5 m grid, kept clear of the walls and the doors.
        static void Furnish(Context c, Geo g, Geo deco, Mats m, Inside ins, List<Vector3> ring, Vector2[] poly, float floor,
                            Transform lights, List<(Vector3 at, Vector3 outward)> entrances, bool main)
        {
            float ClearOfWalls(Vector2 p)
            {
                float best = float.MaxValue;
                for (int i = 0; i < poly.Length; i++)
                    best = Mathf.Min(best, Vector2.Distance(p, ClosestOnSegment(poly[i], poly[(i + 1) % poly.Length], p)));
                return best;
            }
            var mine = entrances.Where(e => PcCityLife.Inside(poly, Flat(e.at - e.outward * 1.5f))).ToList();
            bool NearDoor(Vector2 p) => mine.Any(e => Vector2.Distance(p, Flat(e.at - e.outward * 3f)) < 4.5f);

            // Reception desk inside the main door.
            if (main && mine.Count > 0)
            {
                var e = mine[0];
                var at = e.at - e.outward * 6.5f;
                if (PcCityLife.Inside(poly, Flat(at)) && ClearOfWalls(Flat(at)) > 2f)
                {
                    var rot = Quaternion.LookRotation(e.outward);
                    g.Box(ins.Desk, new Vector3(at.x, floor + 0.53f, at.z), new Vector3(3.2f, 1.06f, 0.7f), rot);
                    g.Box(m.Cornice, new Vector3(at.x, floor + 1.08f, at.z), new Vector3(3.4f, 0.05f, 0.85f), rot);
                }
            }

            Bounds2(poly, out var min, out var max);
            int k = 0, lamps = 0;
            for (float z = min.y + 3.25f; z <= max.y; z += 6.5f)
                for (float x = min.x + 3.25f; x <= max.x; x += 6.5f)
                {
                    var p = new Vector2(x, z);
                    if (!PcCityLife.Inside(poly, p) || ClearOfWalls(p) < 3.2f || NearDoor(p))
                        continue;
                    var at = new Vector3(x, floor, z);
                    var rot = Quaternion.Euler(0f, (k % 2) * 90f, 0f);
                    switch ((k * 7 + (int)(x + z)) % 4)
                    {
                        case 0: StudyTable(g, deco, ins, at, rot); break;
                        case 1: Classroom(g, deco, ins, at, rot); break;
                        case 2: Lounge(g, deco, ins, m, at, rot); break;
                        default:
                            g.Box(m.Screen, at + Vector3.up * 1.1f, new Vector3(3.2f, 2.2f, 0.12f), rot);
                            StudyTable(g, deco, ins, at + rot * new Vector3(0f, 0f, 1.6f), rot);
                            break;
                    }
                    if (lamps < 8 && k % 2 == 0)
                    {
                        var l = new GameObject("Ceiling light").AddComponent<Light>();
                        l.transform.SetParent(lights, false);
                        l.transform.position = at + Vector3.up * (Storey - 0.5f);
                        l.type = LightType.Point;
                        l.range = 11f;
                        l.intensity = 1.4f;
                        l.color = new Color(1f, 0.96f, 0.88f);
                        l.shadows = LightShadows.None;
                        l.renderMode = LightRenderMode.Auto;
                        lamps++;
                    }
                    k++;
                }
        }

        static void Chair(Geo deco, Inside ins, Vector3 at, Quaternion facing)
        {
            deco.Box(ins.Chair, at + Vector3.up * 0.45f, new Vector3(0.45f, 0.05f, 0.45f), facing);
            deco.Box(ins.Chair, at + facing * new Vector3(0f, 0.75f, -0.21f), new Vector3(0.45f, 0.55f, 0.04f), facing);
            foreach (var c in new[] { new Vector3(-0.19f, 0f, -0.19f), new Vector3(0.19f, 0f, -0.19f), new Vector3(-0.19f, 0f, 0.19f), new Vector3(0.19f, 0f, 0.19f) })
                deco.Box(ins.Chair, at + facing * (c + Vector3.up * 0.22f), new Vector3(0.04f, 0.44f, 0.04f), facing);
        }

        static void Table(Geo g, Inside ins, Vector3 at, Quaternion rot, float w, float d)
        {
            g.Box(ins.Desk, at + Vector3.up * 0.74f, new Vector3(w, 0.05f, d), rot);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    g.Box(ins.Chair, at + rot * new Vector3(sx * (w * 0.5f - 0.08f), 0.36f, sz * (d * 0.5f - 0.08f)), new Vector3(0.05f, 0.72f, 0.05f), rot);
        }

        // A table for four.
        static void StudyTable(Geo g, Geo deco, Inside ins, Vector3 at, Quaternion rot)
        {
            Table(g, ins, at, rot, 1.8f, 0.9f);
            foreach (float sx in new[] { -0.45f, 0.45f })
                foreach (float sz in new[] { -1f, 1f })
                    Chair(deco, ins, at + rot * new Vector3(sx, 0f, sz * 0.75f), rot * Quaternion.Euler(0f, sz > 0f ? 180f : 0f, 0f));
        }

        // A small class: a whiteboard on its stand and two rows of desks facing it.
        static void Classroom(Geo g, Geo deco, Inside ins, Vector3 at, Quaternion rot)
        {
            var front = at + rot * new Vector3(0f, 0f, 2.4f);
            g.Box(ins.Board, front + Vector3.up * 1.45f, new Vector3(2.4f, 1.1f, 0.05f), rot);
            foreach (float sx in new[] { -1.1f, 1.1f })
                g.Box(ins.Chair, front + rot * new Vector3(sx, 0.95f, 0f), new Vector3(0.06f, 1.9f, 0.06f), rot);
            foreach (float row in new[] { 0.4f, -1.2f })
                foreach (float sx in new[] { -0.8f, 0.8f })
                {
                    var desk = at + rot * new Vector3(sx, 0f, row);
                    Table(g, ins, desk, rot, 1.2f, 0.55f);
                    Chair(deco, ins, desk + rot * new Vector3(0f, 0f, -0.6f), rot);
                }
        }

        // Two sofas facing over a low table, with a plant.
        static void Lounge(Geo g, Geo deco, Inside ins, Mats m, Vector3 at, Quaternion rot)
        {
            foreach (float sz in new[] { -1f, 1f })
            {
                var s = at + rot * new Vector3(0f, 0f, sz * 1.3f);
                var face = rot * Quaternion.Euler(0f, sz > 0f ? 180f : 0f, 0f);
                g.Box(ins.Sofa, s + Vector3.up * 0.22f, new Vector3(2.0f, 0.44f, 0.85f), face);
                g.Box(ins.Sofa, s + face * new Vector3(0f, 0.6f, -0.35f), new Vector3(2.0f, 0.5f, 0.18f), face);
            }
            g.Box(ins.Desk, at + Vector3.up * 0.21f, new Vector3(1.1f, 0.42f, 0.6f), rot);
            var pot = at + rot * new Vector3(1.6f, 0f, 0f);
            g.Cylinder(m.Frame, m.Earth, pot, 0.25f, pot.y, pot.y + 0.5f, 10);
            deco.Cylinder(m.Planting, m.Planting, pot, 0.4f, pot.y + 0.5f, pot.y + 1.3f, 8);
        }

        // The boundary point nearest a public road: the campus gate, and the direction of the boundary there.
        static Vector2 Gate(Context c, Site site, out Vector2 along)
        {
            var poly = site.Poly;
            Vector2 gate = poly[0];
            along = Vector2.right;
            float bestD = float.MaxValue;
            if (c.Roads != null)
                foreach (var road in c.Roads)
                    for (int i = 0; i + 2 < road.pts.Length; i += 3)
                    {
                        var rp = new Vector2(road.pts[i], road.pts[i + 2]);
                        if (PcCityLife.Inside(poly, rp))
                            continue;
                        for (int k = 0; k < poly.Length; k++)
                        {
                            var q = ClosestOnSegment(poly[k], poly[(k + 1) % poly.Length], rp);
                            float dd = Vector2.Distance(q, rp);
                            if (dd < bestD)
                            {
                                bestD = dd;
                                gate = q;
                                along = (poly[(k + 1) % poly.Length] - poly[k]).normalized;
                            }
                        }
                    }
            return gate;
        }

        // Car park beside the main entrance (TODO: verify against the owner's footage): two rows of ten bays either
        // side of an aisle, levelled, with some parked cars, its long side facing the door. Returns its centre and
        // the direction towards the door.
        static (Vector3, Vector3)? CarPark(Context c, Geo g, Geo deco, Inside ins, Site site, List<DistrictImporter.Building> footprints,
                                           Transform root, Vector3 door, Vector3 doorOut)
        {
            var poly = site.Poly;
            const float W = 27f, D = 18f;
            var buildingPolys = footprints.Select(b => PcCityLife.Polygon(b.pts)).ToList();
            bool Clear(Vector2 p, Vector2 u, Vector2 v)
            {
                for (float x = -W * 0.5f - 2f; x <= W * 0.5f + 2f; x += 3f)
                    for (float z = -D * 0.5f - 2f; z <= D * 0.5f + 2f; z += 3f)
                    {
                        var q = p + u * x + v * z;
                        if (!PcCityLife.Inside(poly, q) || buildingPolys.Any(bp => PcCityLife.Inside(bp, q)))
                            return false;
                    }
                return true;
            }
            Vector2? spot = null;
            Vector2 toDoor = Vector2.up;
            foreach (float dist in new[] { 22f, 28f, 36f, 46f, 58f })
                foreach (float turn in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f })
                {
                    if (spot != null)
                        break;
                    var dir3 = Quaternion.Euler(0f, turn, 0f) * doorOut;
                    var dir = new Vector2(dir3.x, dir3.z).normalized;
                    var p = Flat(door) + dir * dist;
                    var v = -dir;
                    var u = new Vector2(v.y, -v.x);
                    if (Clear(p, u, v))
                    {
                        spot = p;
                        toDoor = v;
                    }
                }
            if (spot == null)
            {
                Debug.LogWarning("[ALU] No clear ground for the car park near the main entrance.");
                return null;
            }
            var inward = toDoor;
            var centre = new Vector3(spot.Value.x, 0f, spot.Value.y);
            var rot = Quaternion.LookRotation(new Vector3(inward.x, 0f, inward.y));
            float level = Ground(c.Terrain, centre);
            Level(c.Terrain, centre, rot, W + 6f, D + 6f, level);
            centre.y = level;

            var host = new GameObject("ALU car park").transform;
            host.SetParent(root, false);
            host.SetPositionAndRotation(centre, rot);
            g.Box(ins.Asphalt, centre + Vector3.up * -0.05f, new Vector3(W, 0.2f, D), rot);
            // Bays: 2.6 m x 5.5 m, rows along the far and near edges, a 6 m aisle between.
            var rng = c.Rng;
            var cars = c.Cat.Vehicles.Where(v => !new[] { "Bus", "Truck", "Container", "Ambulance", "Police", "Van" }.Any(v.name.Contains)).ToArray();
            int parked = 0;
            for (int row = 0; row < 2; row++)
            {
                float v = row == 0 ? D * 0.5f - 2.75f : -D * 0.5f + 2.75f;
                for (int k = 0; k <= 10; k++)
                {
                    float u = -13f + k * 2.6f;
                    deco.Box(ins.Line, centre + rot * new Vector3(u, 0.06f, v), new Vector3(0.1f, 0.02f, 5.5f), rot);
                }
                deco.Box(ins.Line, centre + rot * new Vector3(0f, 0.06f, row == 0 ? D * 0.5f - 0.05f : -D * 0.5f + 0.05f), new Vector3(26f, 0.02f, 0.1f), rot);
                for (int k = 0; k < 10 && cars.Length > 0; k++)
                {
                    if (rng.NextDouble() > 0.45)
                        continue;
                    float u = -13f + 1.3f + k * 2.6f;
                    var at = centre + rot * new Vector3(u, 0f, v);
                    var holder = new GameObject($"Parked car {parked + 1}").transform;
                    holder.SetParent(host, true);
                    holder.SetPositionAndRotation(at, rot * Quaternion.Euler(0f, row == 0 ? 0f : 180f, 0f));
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(cars[rng.Next(cars.Length)]);
                    model.transform.SetParent(holder, false);
                    EditorAssetUtil.StripRuntimeComponents(model);
                    AssetCatalog.FitVehicle(model, 4.5f);
                    var b = EditorAssetUtil.RendererBounds(model);
                    var box = holder.gameObject.AddComponent<BoxCollider>();
                    box.center = holder.InverseTransformPoint(b.center);
                    box.size = new Vector3(1.8f, b.size.y, 4.4f);
                    parked++;
                }
            }
            return (centre, rot * Vector3.forward);
        }

        // Levels the terrain under a rectangle to height y (with a 4 m blend to the ground around it).
        static void Level(Terrain t, Vector3 centre, Quaternion rot, float w, float d, float y)
        {
            var data = t.terrainData;
            var origin = t.transform.position;
            int res = data.heightmapResolution;
            var size = data.size;
            float target = Mathf.Clamp01((y - origin.y) / size.y);
            float reach = Mathf.Max(w, d) * 0.5f + 6f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((centre.x - reach - origin.x) / size.x * (res - 1)), 0, res - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((centre.x + reach - origin.x) / size.x * (res - 1)), 0, res - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((centre.z - reach - origin.z) / size.z * (res - 1)), 0, res - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((centre.z + reach - origin.z) / size.z * (res - 1)), 0, res - 1);
            var h = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
            var inv = Quaternion.Inverse(rot);
            for (int j = 0; j <= z1 - z0; j++)
                for (int i = 0; i <= x1 - x0; i++)
                {
                    var world = new Vector3(origin.x + (x0 + i) / (float)(res - 1) * size.x, 0f, origin.z + (z0 + j) / (float)(res - 1) * size.z);
                    var local = inv * (world - new Vector3(centre.x, 0f, centre.z));
                    float ox = Mathf.Max(0f, Mathf.Abs(local.x) - w * 0.5f), oz = Mathf.Max(0f, Mathf.Abs(local.z) - d * 0.5f);
                    float k = 1f - Mathf.Clamp01(Mathf.Sqrt(ox * ox + oz * oz) / 4f);
                    if (k > 0f)
                        h[j, i] = Mathf.Lerp(h[j, i], target, Mathf.SmoothStep(0f, 1f, k));
                }
            data.SetHeights(x0, z0, h);
        }

        // A 6 m asphalt driveway over the ground from a to b, with a kerb line either side.
        static void Driveway(Context c, Geo g, Geo deco, Inside ins, Vector3 a, Vector3 b)
        {
            var flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float len = flat.magnitude;
            if (len < 2f)
                return;
            var dir = flat / len;
            int n = Mathf.CeilToInt(len / 3f);
            for (int k = 0; k < n; k++)
            {
                var p0 = a + dir * (k * len / n);
                var p1 = a + dir * ((k + 1) * len / n);
                p0.y = Ground(c.Terrain, p0);
                p1.y = Ground(c.Terrain, p1);
                var mid = (p0 + p1) * 0.5f + Vector3.up * 0.02f;
                var rot = Quaternion.LookRotation(p1 - p0);
                g.Box(ins.Asphalt, mid, new Vector3(6f, 0.14f, Vector3.Distance(p0, p1) + 0.1f), rot);
                foreach (float side in new[] { -2.85f, 2.85f })
                    deco.Box(ins.Line, mid + rot * new Vector3(side, 0.075f, 0f), new Vector3(0.12f, 0.01f, Vector3.Distance(p0, p1)), rot);
            }
        }

        // Palms every spacing metres along a-b, offset to both sides (offset 0: one palm at a).
        static void Palms(Context c, Transform root, Vector3 a, Vector3 b, float spacing, float offset)
        {
            if (c.Cat?.Palms == null || c.Cat.Palms.Length == 0)
                return;
            var host = root.Find("Palms") ?? new GameObject("Palms").transform;
            host.SetParent(root, false);
            var flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float len = flat.magnitude;
            var dir = len > 0.01f ? flat / len : Vector3.forward;
            var side = new Vector3(dir.z, 0f, -dir.x);
            for (float d = 4f; d <= Mathf.Max(4f, len - 4f); d += spacing)
                foreach (float s in offset > 0f ? new[] { -offset, offset } : new[] { 0f })
                {
                    var p = a + dir * (offset > 0f ? d : 0f) + side * s;
                    p.y = Ground(c.Terrain, p);
                    var palm = (GameObject)PrefabUtility.InstantiatePrefab(c.Cat.Palms[c.Rng.Next(c.Cat.Palms.Length)]);
                    palm.transform.SetParent(host, true);
                    palm.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, (float)c.Rng.NextDouble() * 360f, 0f));
                    EditorAssetUtil.StripRuntimeComponents(palm);
                    var trunk = palm.AddComponent<CapsuleCollider>();
                    trunk.radius = 0.3f;
                    trunk.height = 4f;
                    trunk.center = new Vector3(0f, 2f, 0f);
                    if (offset <= 0f)
                        return;
                }
        }

        // A 2.4 m brick path over the ground from a to b.
        static void BrickPath(Context c, Geo g, Mats m, Vector3 a, Vector3 b)
        {
            var flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float len = flat.magnitude;
            if (len < 1f)
                return;
            var dir = flat / len;
            int n = Mathf.CeilToInt(len / 2f);
            for (int k = 0; k < n; k++)
            {
                var p = a + dir * ((k + 0.5f) * len / n);
                p.y = Ground(c.Terrain, p) + 0.03f;
                g.Box(m.Pavers, p, new Vector3(2.4f, 0.12f, len / n + 0.05f), Quaternion.LookRotation(dir));
            }
        }

        // Renders of the rebuilt campus (-captureDir <folder>): aerial, the car park and inside the main entrance.
        public static void CaptureHalls(string folder)
        {
            Directory.CreateDirectory(folder);
            EditorSceneManager.OpenScene(StoryModeBuilder.ScenePath, OpenSceneMode.Single);
            var root = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                             .FirstOrDefault(t => t.name == $"{PlaceName} (campus)");
            if (root == null)
                return;
            var park = root.Find("ALU car park");
            var doors = root.Cast<Transform>().Where(t => t.name == "Entrance").ToList();
            var all = new Bounds(root.position, Vector3.zero);
            var halls = root.Find("Campus (brick, floors, furniture)");
            bool first = true;
            foreach (var r in (halls != null ? halls : root).GetComponentsInChildren<Renderer>())
            {
                if (first) { all = r.bounds; first = false; }
                else all.Encapsulate(r.bounds);
            }
            var shots = new List<(string, Vector3, Vector3)>
            {
                ("1 Aerial", all.center + new Vector3(-120f, 90f, -120f), all.center),
                ("2 Overhead", all.center + Vector3.up * 220f + Vector3.back * 1f, all.center),
            };
            if (park != null)
                shots.Add(("3 Car park", park.position + park.forward * -26f + Vector3.up * 9f, park.position));
            for (int i = 0; i < doors.Count && i < 3; i++)
            {
                var floorAt = doors[i].position;
                var outward = Vector3.zero;
                // The entrance faces away from its hall's inside: the nearest ceiling light is inside.
                var light = root.GetComponentsInChildren<Light>().OrderBy(l => (l.transform.position - floorAt).sqrMagnitude).FirstOrDefault();
                var inward = light != null ? Vector3.ProjectOnPlane(light.transform.position - floorAt, Vector3.up).normalized : Vector3.forward;
                shots.Add(($"4 Entrance {i + 1} outside", floorAt - inward * 9f + Vector3.up * 1.7f, floorAt + Vector3.up * 1.5f));
                shots.Add(($"5 Inside {i + 1}", floorAt + inward * 1.5f + Vector3.up * 1.65f, floorAt + inward * 12f + Vector3.up * 1.2f));
            }
            var cam = new GameObject("Capture").AddComponent<Camera>();
            cam.fieldOfView = 65f;
            cam.farClipPlane = 3000f;
            var rt = new RenderTexture(1600, 900, 24);
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            foreach (var (name, pos, look) in shots)
            {
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
                cam.Render();
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), tex.EncodeToPNG());
            }
            RenderTexture.active = null;
            Object.DestroyImmediate(cam.gameObject);
            Debug.Log($"[ALU] {shots.Count} renders in {folder}.");
        }
    }
}
