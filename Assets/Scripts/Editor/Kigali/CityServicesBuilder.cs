using System.Collections.Generic;
using System.IO;
using System.Linq;
using KR.Editor.GIS;
using KR.Editor.Util;
using KR.Gameplay.Player;
using KR.Gameplay.Story;
using KR.Gameplay.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Activity = KR.Editor.Kigali.PcPeople.Activity;

namespace KR.Editor.Kigali
{
    // The living city on top of the Story scene, from the real OpenStreetMap names (named_unity.json, made by
    // Tools/gis/extract_named.py; names exactly as mapped, nothing invented):
    //   - a signboard with its real name on every named place (on the building's street side, or on a post);
    //   - businesses that work: staff at the door you can talk to (order, ask, pay a visit), customers around,
    //     present only during opening hours (generic game hours, not the real ones);
    //   - street-name blades at the ends of every named street, and house-number plaques where OSM has them;
    //   - traffic police directing the busiest junctions and officers on foot patrol;
    //   - people crossing at the zebra crossings when the cars have red;
    //   - groups of people chatting on the pavements, students on the ALU campus (groups, walkers, a receptionist,
    //     the canteen);
    //   - the other driver for the accident scene;
    //   - the street network and the place directory for the satnav.
    // Clusters switch on only near the camera (NearbyOnly), so the Quest pays for what is close.
    internal static class CityServicesBuilder
    {
        const string Out = "Assets/World/Story";
        const float ServiceRadius = 1100f;     // businesses with staff: within this of a sector centre
        const int MaxBusinesses = 140;

        [System.Serializable] class NamedFile { public Named[] named; public Number[] numbers; }
        [System.Serializable] class Named { public string osm, kind, group, name, street, number; public float x, z; public float[] footprint; }
        [System.Serializable] class Number { public string osm, street, number; public float x, z; }

        public sealed class Context
        {
            public Transform Host;
            public Terrain Terrain;
            public RoadNetworkTraffic Vehicles;
            public PlayerRig Rig;
            public StoryDirector Director;
            public ConversationRunner Runner;
            public Navigator Nav;
            public PlaceCatalog Catalog;
            public string District;
            public Vector3 Campus;
            public Material RouteMaterial;
        }

        static Terrain _terrain;
        static RoadClearance _clear;
        static GameObject[] _women, _men;
        static System.Random _rng;
        static Font _font;
        static int _people;
        static GameObject _table, _chair, _cup, _plate, _bottle;
        const string TablePath = "Assets/ModularLowpolyStreetsFree/Prefabs/Other/Cafe_table_1.prefab";
        const string ChairPath = "Assets/ModularLowpolyStreetsFree/Prefabs/Other/Cafe_chair_1.prefab";

        public static void Build(Context c)
        {
            _terrain = c.Terrain;
            _rng = new System.Random(1962);
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _people = 0;
            string src = Path.Combine(DistrictImporter.SourceDir, c.District);
            string namedPath = Path.Combine(src, "named_unity.json");
            if (!File.Exists(namedPath))
            {
                Debug.LogError($"[City] {namedPath} missing. Run Tools/gis/extract_named.py first.");
                return;
            }
            var meta = JsonUtility.FromJson<DistrictImporter.TerrainMeta>(File.ReadAllText(Path.Combine(src, "terrain.json")));
            var hubs = meta.hubs.Select(h => new Vector3(h.x, 0f, h.z)).ToArray();
            var allRoads = JsonUtility.FromJson<DistrictImporter.RoadList>(File.ReadAllText(Path.Combine(src, "roads_unity.json"))).roads;
            allRoads = PcSurround.ClipToMap(allRoads, meta.unity_terrain.width_m);
            if (!PlayArea.Ready)
                PlayArea.Init(hubs, meta.hubs.Select(h => h.name).ToArray(), allRoads, meta.unity_terrain.width_m);
            var roads = PlayArea.Clip(allRoads, 120f);
            _clear = new RoadClearance(roads);
            var file = JsonUtility.FromJson<NamedFile>(File.ReadAllText(namedPath));
            var buildings = JsonUtility.FromJson<DistrictImporter.BuildingList>(File.ReadAllText(Path.Combine(src, "buildings_unity.json"))).buildings;
            var footprints = new FootprintIndex(PlayArea.Filter(buildings));

            var cat = PcShowcaseBuilder.LoadCatalog();
            _women = cat.Pedestrians.Where(m => m != null && m.name.ToLowerInvariant().Contains("woman")).ToArray();
            _men = cat.Pedestrians.Where(m => m != null && !m.name.ToLowerInvariant().Contains("woman")).ToArray();
            if (_women.Length == 0) _women = cat.Pedestrians;
            if (_men.Length == 0) _men = cat.Pedestrians;
            PcPeople.Begin("Assets/World/PC/kimironko_kibagabaga/People");
            _table = AssetDatabase.LoadAssetAtPath<GameObject>(TablePath);
            _chair = AssetDatabase.LoadAssetAtPath<GameObject>(ChairPath);
            (_cup, _plate, _bottle) = CleanCity.Tableware($"{Out}/Tableware");
            Physics.SyncTransforms();

            var root = new GameObject("City life (named places, services, police, crossings, people)").transform;
            root.SetParent(c.Host, false);

            // Satnav data.
            var streets = BuildStreets(roads);
            var directory = new List<DirectoryEntry>();

            // Named places: signs everywhere; staff and customers at businesses near the sectors.
            var signJobs = new List<System.Action<Transform>>();
            var business = Group(root, "Businesses (staff and customers, open hours)");
            var used = new Dictionary<int, int>();       // building index -> signs on it so far
            int businesses = 0, signCount = 0;
            foreach (var n in file.named.Where(n => PlayArea.Contains(new Vector3(n.x, 0f, n.z), 20f))
                                        .OrderBy(n => hubs.Min(h => Flat(h, new Vector3(n.x, 0f, n.z)))))
            {
                var at = new Vector3(n.x, 0f, n.z);
                Front front = FrontOf(footprints, n, at, streets, used);
                directory.Add(new DirectoryEntry
                {
                    name = n.name, kind = n.kind, group = n.group, street = n.street, osm = n.osm,
                    position = front.Door,
                });
                var f = front;
                var place = n;
                if (n.kind == "bus_stop")
                    signJobs.Add(t => PostSign(t, f.Door, f.Out, place.name, GroupColour(place.group), 2.4f));
                else if (front.OnWall)
                    signJobs.Add(t => WallSign(t, f, place.name, GroupColour(place.group)));
                else
                    signJobs.Add(t => PostSign(t, f.Door, f.Out, place.name, GroupColour(place.group), 2.8f));
                signCount++;

                var service = Service(n.kind);
                if (service.conversation != null && businesses < MaxBusinesses && hubs.Any(h => Flat(h, at) < ServiceRadius))
                {
                    Staff(business, n, front, service);
                    businesses++;
                }
            }
            c.Catalog.directory = directory.ToArray();
            EditorUtility.SetDirty(c.Catalog);

            // Signs, street names and house numbers are hand-editable layers, kept across builds (EditableLayer).
            EditableLayer.Use("Signboards (OSM names)", root, _ =>
            {
                var g = new GameObject();
                foreach (var job in signJobs)
                    job(g.transform);
                return g;
            });
            int streetSigns = -1, plaques = -1;
            EditableLayer.Use("Street names", root, _ =>
            {
                var g = new GameObject();
                streetSigns = StreetSigns(g.transform, roads, hubs);
                return g;
            });
            EditableLayer.Use("House numbers", root, _ =>
            {
                var g = new GameObject();
                plaques = HouseNumbers(g.transform, file.numbers, footprints, streets);
                return g;
            });
            int police = Police(Group(root, "Police"), c.Vehicles, hubs);
            int crossers = Crossings(Group(root, "Crossings"), c.Vehicles, hubs);
            int chats = Gatherings(Group(root, "People chatting"), c.Vehicles, hubs);
            int students = Campus(Group(root, "ALU campus life"), c.Campus);

            // The other driver, for the accident scene.
            var driverModel = _men[_rng.Next(_men.Length)];
            var driver = PcPeople.Spawn(driverModel, root, Vector3.zero, 0f, Activity.Talk, _rng, "Other driver (accident scene)", brain: true);
            var driverNpc = driver.AddComponent<Npc>();
            driverNpc.Configure("other_driver", "The other driver", "acc_driver", driver.GetComponent<PersonAnimator>(), null, 3.2f);
            var accident = c.Host.gameObject.AddComponent<AccidentScene>();
            accident.Configure(c.Rig, c.Vehicles, c.Director, c.Runner, driverNpc);

            // Orders at cafes: one cup, plate and bottle in the scene, moved to the table when served.
            var ware = Group(root, "Served items (reused)");
            GameObject Item(GameObject prefab)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.SetParent(ware, false);
                go.SetActive(false);
                return go;
            }
            c.Host.gameObject.AddComponent<ServiceOrders>().Configure(c.Rig, c.Runner, c.Director, Item(_cup), Item(_plate), Item(_bottle));
            c.Host.gameObject.AddComponent<DestinationBeacon>().Configure(c.Nav, c.Rig, c.RouteMaterial);

            // Satnav: every street and every named place; the destination list; live guidance on foot.
            c.Nav.SetDirectory(c.Catalog, streets);
            c.Host.gameObject.AddComponent<DestinationPicker>().Configure(c.Nav, c.Rig);
            var trail = new GameObject("Route trail (on foot)", typeof(MeshFilter), typeof(MeshRenderer));
            trail.transform.SetParent(c.Host, false);
            trail.AddComponent<RouteTrail>().Configure(c.Nav, c.Rig, c.RouteMaterial);

            AssetDatabase.SaveAssets();
            Debug.Log($"[City] {directory.Count} named places ({signCount} signs), {businesses} businesses with staff, {(streetSigns < 0 ? "kept" : streetSigns.ToString())} street-name signs, " +
                      $"{(plaques < 0 ? "kept" : plaques.ToString())} house numbers, {police} police, {crossers} people crossing, {chats} people chatting, {students} on campus; " +
                      $"{_people} people in all; {streets.streets.Length} streets for the satnav.");
        }

        // ---------- satnav streets ----------

        static StreetNetwork BuildStreets(DistrictImporter.Road[] roads)
        {
            var net = EditorAssetUtil.LoadOrCreate<StreetNetwork>($"{Out}/Streets.asset");
            var list = new List<StreetNetwork.Street>();
            foreach (var r in roads)
            {
                if (r.pts == null || r.pts.Length < 6 || !DistrictImporter.RoadWidths.TryGetValue(r.highway, out float width))
                    continue;
                var line = PcRoadBuilder.ResampleLine(r.pts, 15f);
                if (line.Count < 2)
                    continue;
                list.Add(new StreetNetwork.Street
                {
                    name = r.name ?? "", highway = r.highway, oneway = r.oneway == "yes", halfWidth = width * 0.5f,
                    points = line.Select(p => new Vector3(p.x, Surface(p), p.z)).ToArray(),
                });
            }
            net.streets = list.ToArray();
            EditorUtility.SetDirty(net);
            return net;
        }

        // ---------- named places ----------

        struct Front
        {
            public bool OnWall;
            public Vector3 Wall;     // sign centre on the facade, at ground height
            public Vector3 Door;     // where people stand, 1.4 m in front of the facade, on the ground
            public Vector3 Out;      // facade normal (towards the street)
            public Vector3 Along;    // along the facade
            public float Width;      // facade width
        }

        // The street-facing side of the place's building (its own footprint, or the building it stands in or next to).
        static Front FrontOf(FootprintIndex footprints, Named n, Vector3 at, StreetNetwork streets, Dictionary<int, int> used)
        {
            float[] pts = n.footprint != null && n.footprint.Length >= 6 ? n.footprint : null;
            int index = -1;
            if (pts == null && footprints.Find(at, 25f, out index))
                pts = footprints.Points(index);
            if (pts != null && PcCityLife.OrientedBox(pts, _terrain, out var centre, out float yaw, out float w, out float d, out _))
            {
                w *= 0.94f;
                d *= 0.94f;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                Vector3 toStreet = NearestStreetDirection(streets, centre);
                // The face whose normal points most towards the street.
                var faces = new[] { (rot * Vector3.forward, d * 0.5f, w), (rot * Vector3.back, d * 0.5f, w), (rot * Vector3.right, w * 0.5f, d), (rot * Vector3.left, w * 0.5f, d) };
                var best = faces.OrderByDescending(f => Vector3.Dot(f.Item1, toStreet)).First();
                var normal = best.Item1;
                var along = Vector3.Cross(Vector3.up, normal);
                int k = index >= 0 ? (used.TryGetValue(index, out int u) ? u : 0) : 0;
                if (index >= 0)
                    used[index] = k + 1;
                // Several places in one building: spread along the facade, then a row higher.
                float slots = Mathf.Max(1f, Mathf.Floor(best.Item3 / 4f));
                float shift = ((k % slots) - (slots - 1f) * 0.5f) * 4f;
                var wall = centre + normal * (best.Item2 + 0.07f) + along * shift;
                wall.y = 0f;
                var door = wall + normal * 1.4f;
                return new Front
                {
                    OnWall = k < slots * 2, Wall = new Vector3(wall.x, Ground(wall) + (k >= slots ? 0.8f : 0f), wall.z), Door = OnGround(door),
                    Out = normal, Along = along, Width = Mathf.Min(best.Item3, 4f),
                };
            }
            var dir = NearestStreetDirection(streets, at);
            return new Front { OnWall = false, Door = OnGround(at), Out = dir, Along = Vector3.Cross(Vector3.up, dir), Width = 2.5f };
        }

        static Vector3 NearestStreetDirection(StreetNetwork streets, Vector3 p)
        {
            float best = float.MaxValue;
            Vector3 dir = Vector3.forward;
            foreach (var s in streets.streets)
                for (int i = 0; i + 1 < s.points.Length; i++)
                {
                    Vector3 a = s.points[i], b = s.points[i + 1];
                    if (Mathf.Abs(a.x - p.x) > 150f || Mathf.Abs(a.z - p.z) > 150f)
                        continue;
                    var q = Closest(p, a, b);
                    float d = Flat(p, q);
                    if (d < best)
                    {
                        best = d;
                        dir = new Vector3(q.x - p.x, 0f, q.z - p.z);
                    }
                }
            return dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
        }

        static Color GroupColour(string group)
        {
            switch (group)
            {
                case "food": return new Color(0.62f, 0.17f, 0.11f);
                case "shop": return new Color(0.1f, 0.34f, 0.6f);
                case "health": return new Color(0.04f, 0.46f, 0.28f);
                case "finance": return new Color(0.1f, 0.18f, 0.38f);
                case "lodging": return new Color(0.34f, 0.2f, 0.44f);
                case "service": return new Color(0.75f, 0.45f, 0.06f);
                case "education": return new Color(0.08f, 0.3f, 0.52f);
                case "worship": return new Color(0.33f, 0.28f, 0.22f);
                case "leisure": return new Color(0.14f, 0.42f, 0.18f);
                case "transport": return new Color(0.07f, 0.28f, 0.58f);
                default: return new Color(0.22f, 0.24f, 0.27f);
            }
        }

        // A board on the facade above the door, facing the street.
        static void WallSign(Transform parent, Front f, string name, Color colour)
        {
            var host = Cluster(parent, $"Sign: {name}", f.Wall, 160f);
            float width = Mathf.Clamp(0.5f + name.Length * 0.16f, 1.4f, Mathf.Max(1.4f, f.Width - 0.3f));
            Board(host, f.Wall + Vector3.up * 2.2f, f.Out, width, 0.55f, name, colour);
        }

        // A sign on its own post (no building to hang it on, bus stops): readable from both sides.
        static void PostSign(Transform parent, Vector3 at, Vector3 facing, string name, Color colour, float height)
        {
            var spot = at;
            _clear.Push(ref spot, -facing, 0.4f, 4f);
            spot = OnGround(spot);
            var host = Cluster(parent, $"Sign: {name}", spot, 160f);
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Post";
            pole.transform.SetParent(host, false);
            pole.transform.position = spot + Vector3.up * (height * 0.5f);
            pole.transform.localScale = new Vector3(0.07f, height * 0.5f, 0.07f);
            pole.GetComponent<Renderer>().sharedMaterial = EditorAssetUtil.LitMaterial(Out, "SignPost", new Color(0.35f, 0.36f, 0.38f), 0.5f);
            float width = Mathf.Clamp(0.5f + name.Length * 0.14f, 1.2f, 3.2f);
            // A solid plate with one face on each side; faces sharing one spot showed the far one mirrored through.
            var top = spot + Vector3.up * (height + 0.3f);
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            plate.name = "Plate";
            plate.transform.SetParent(host, false);
            plate.transform.SetPositionAndRotation(top, Quaternion.LookRotation(facing));
            plate.transform.localScale = new Vector3(width + 0.04f, 0.54f, 0.03f);
            plate.GetComponent<Renderer>().sharedMaterial = pole.GetComponent<Renderer>().sharedMaterial;
            Board(host, top + facing.normalized * 0.02f, facing, width, 0.5f, name, colour);
            Board(host, top - facing.normalized * 0.02f, -facing, width, 0.5f, name, colour);
        }

        // A coloured board with the name in white, best-fitted (world-space canvas, 100 px per metre).
        static void Board(Transform parent, Vector3 centre, Vector3 facing, float width, float height, string text, Color colour)
        {
            var go = new GameObject("Board", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(width * 100f, height * 100f);
            go.transform.localScale = Vector3.one * 0.01f;
            // A canvas shows its front towards -forward; look away from the reader.
            go.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(-facing));
            var bg = new GameObject("Back", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
            bg.transform.SetParent(go.transform, false);
            bg.color = colour;
            Stretch(bg.rectTransform, 0f);
            var t = new GameObject("Name", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Text>();
            t.transform.SetParent(go.transform, false);
            t.font = _font;
            t.text = text;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 8;
            t.resizeTextMaxSize = 34;
            t.fontStyle = FontStyle.Bold;
            Stretch(t.rectTransform, 6f);
        }

        static void Stretch(RectTransform r, float pad)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(pad, pad);
            r.offsetMax = new Vector2(-pad, -pad);
        }

        // ---------- businesses ----------

        // Staff title, conversation and generic opening hours per kind (game hours; not the places' real hours).
        static (string title, string conversation, float from, float to) Service(string kind)
        {
            switch (kind)
            {
                case "cafe": case "restaurant": case "fast_food": case "bakery": case "dairy": return ("Server", "svc_food", 7f, 22f);
                case "bar": return ("Bartender", "svc_bar", 16f, 2f);
                case "supermarket": case "shop": case "clothes": case "hardware": case "electronics": case "books": case "mall":
                    return ("Shop assistant", "svc_shop", 8f, 20f);
                case "market": return ("Vendor", "svc_shop", 7f, 18f);
                case "pharmacy": return ("Pharmacist", "svc_pharmacy", 8f, 22f);
                case "clinic": return ("Receptionist", "svc_clinic", 8f, 18f);
                case "hospital": return ("Receptionist", "svc_clinic", 0f, 24f);
                case "bank": return ("Bank staff", "svc_bank", 8f, 17f);
                case "hotel": return ("Receptionist", "svc_hotel", 0f, 24f);
                case "salon": return ("Hairdresser", "svc_salon", 8f, 20f);
                case "fuel": case "car_wash": case "garage": return ("Attendant", "svc_garage", 6f, 22f);
                case "internet_cafe": case "office": return ("Receptionist", "svc_office", 8f, 17f);
                case "school": case "university": return ("Secretary", "svc_school", 7f, 17f);
                case "gym": case "sports": case "pool": return ("Trainer", "svc_gym", 6f, 21f);
                default: return (null, null, 0f, 0f);
            }
        }

        static void Staff(Transform parent, Named n, Front f, (string title, string conversation, float from, float to) s)
        {
            if (!Free(f.Door))
                return;
            var host = Cluster(parent, n.name, f.Door, 90f);
            var hours = new GameObject("Open hours").transform;
            hours.SetParent(host, false);
            if (s.to - s.from < 24f)
                hours.gameObject.AddComponent<ScheduleVisibility>().Configure(s.from, s.to);
            bool female = _rng.NextDouble() < 0.5;
            var staff = Person(hours, f.Door, Yaw(f.Out), Activity.Idle, female, $"{s.title}, {n.name}");
            var npc = staff.AddComponent<Npc>();
            npc.Configure($"staff:{n.osm}", $"{s.title} at {n.name}", s.conversation, staff.GetComponent<PersonAnimator>(), n.name);
            if (n.group == "food")
                Terrace(host, f, npc);
            // Customers: one or two at the door, talking to each other.
            int customers = n.group == "food" || n.group == "shop" ? _rng.Next(1, 3) : _rng.Next(0, 2);
            for (int i = 0; i < customers; i++)
            {
                var p = f.Door + f.Out * 1.3f + f.Along * (i == 0 ? 1.1f : -1.1f);
                if (!Free(p) || !_clear.Clear(p, 0.4f))
                    continue;
                p = OnGround(p);
                Person(hours, p, Yaw(f.Door - p), Activity.Talk, _rng.NextDouble() < 0.5, $"Customer, {n.name}");
            }
        }

        // Two tables with chairs in front of a cafe, restaurant or bar, a cup on each (always there; the staff serve
        // orders to them). Spots in the way of the road or a wall are skipped.
        static void Terrace(Transform host, Front f, Npc staff)
        {
            if (_table == null)
                return;
            var tops = new List<Transform>();
            foreach (float side in new[] { -1.8f, 1.8f })
            {
                var at = f.Door + f.Out * 2.6f + f.Along * side;
                if (!Free(at) || !_clear.Clear(at, 1.3f))
                    continue;
                at = OnSurface(at);
                var table = (GameObject)PrefabUtility.InstantiatePrefab(_table);
                table.name = "Terrace table";
                table.transform.SetParent(host, false);
                table.transform.SetPositionAndRotation(at, Quaternion.LookRotation(f.Out));
                if (_chair != null)
                    foreach (float c in new[] { -0.75f, 0.75f })
                    {
                        var chair = (GameObject)PrefabUtility.InstantiatePrefab(_chair);
                        chair.name = "Chair";
                        chair.transform.SetParent(table.transform, true);
                        var cp = at + f.Along * c;
                        chair.transform.SetPositionAndRotation(cp, Quaternion.LookRotation(at - cp));
                    }
                var b = EditorAssetUtil.RendererBounds(table);
                var top = new GameObject("Table top").transform;
                top.SetParent(table.transform, true);
                top.position = new Vector3(at.x, b.max.y + 0.002f, at.z) + f.Out * 0.15f;
                tops.Add(top);
                // Something already on the table: a cup of tea.
                if (_cup != null)
                {
                    var cup = (GameObject)PrefabUtility.InstantiatePrefab(_cup);
                    cup.transform.SetParent(table.transform, true);
                    cup.transform.position = new Vector3(at.x, b.max.y + 0.002f, at.z) - f.Out * 0.2f + f.Along * 0.15f;
                }
            }
            if (tops.Count > 0)
                host.gameObject.AddComponent<ServiceTable>().Configure(staff, tops.ToArray());
        }

        // ---------- street names and house numbers ----------

        static int StreetSigns(Transform parent, DistrictImporter.Road[] roads, Vector3[] hubs)
        {
            var placed = new List<(Vector3 at, string name)>();
            var colour = new Color(0.05f, 0.2f, 0.45f);   // TODO: verify Kigali street-sign colours against a photo.
            foreach (var r in roads)
            {
                if (string.IsNullOrEmpty(r.name) || r.pts == null || r.pts.Length < 6 ||
                    !DistrictImporter.RoadWidths.TryGetValue(r.highway, out float width))
                    continue;
                var line = PcRoadBuilder.ResampleLine(r.pts, 6f);
                if (line.Count < 4)
                    continue;
                foreach (bool start in new[] { true, false })
                {
                    Vector3 end = start ? line[1] : line[line.Count - 2];
                    Vector3 inward = start ? line[2] - line[1] : line[line.Count - 3] - line[line.Count - 2];
                    inward.y = 0f;
                    if (inward.sqrMagnitude < 0.01f || !hubs.Any(h => Flat(h, end) < 1300f))
                        continue;
                    inward.Normalize();
                    if (placed.Any(p => p.name == r.name && Flat(p.at, end) < 60f))
                        continue;
                    var right = new Vector3(inward.z, 0f, -inward.x);
                    var at = end + inward * 4f + right * (width * 0.5f + 1.2f);
                    _clear.Push(ref at, right, 0.4f, 4f);
                    if (!Free(at))
                        continue;
                    // The blade runs along the street so it reads from the junction.
                    PostSign(parent, at, right, r.name, colour, 2.5f);
                    placed.Add((end, r.name));
                }
            }
            return placed.Count;
        }

        static int HouseNumbers(Transform parent, Number[] numbers, FootprintIndex footprints, StreetNetwork streets)
        {
            int count = 0;
            var used = new Dictionary<int, int>();
            foreach (var num in numbers ?? new Number[0])
            {
                var at = new Vector3(num.x, 0f, num.z);
                if (!PlayArea.Contains(at))
                    continue;
                var f = FrontOf(footprints, new Named { name = num.number, osm = num.osm }, at, streets, used);
                if (!f.OnWall)
                    continue;
                string label = string.IsNullOrEmpty(num.street) ? $"No. {num.number}" : $"No. {num.number}, {num.street}";
                var host = Cluster(parent, label, f.Wall, 60f);
                Board(host, f.Wall + Vector3.up * 1.9f + f.Along * 0.9f, f.Out, Mathf.Clamp(0.3f + label.Length * 0.06f, 0.5f, 1.4f), 0.22f,
                      label, new Color(0.08f, 0.25f, 0.5f));
                count++;
            }
            return count;
        }

        // ---------- police ----------

        static int Police(Transform parent, RoadNetworkTraffic vehicles, Vector3[] hubs)
        {
            if (vehicles == null)
                return 0;
            var vest = EditorAssetUtil.LitMaterial(Out, "HiVis", new Color(0.85f, 0.95f, 0.1f), 0.3f);
            var cap = EditorAssetUtil.LitMaterial(Out, "PoliceCap", new Color(0.08f, 0.12f, 0.2f), 0.3f);
            int count = 0;

            // Traffic officers at the signalised junctions nearest each sector centre (up to two per sector).
            var junctions = new List<(Vector3 centre, Vector3 dir, float half)>();
            for (int i = 0; i < vehicles.StopCount; i++)
                if (vehicles.StopLines(i, out var a, out var da, out _, out _, out _, out _))
                    junctions.Add((a + da * vehicles.StopLineBack, da, HalfWidthAt(vehicles, a)));
            foreach (var hub in hubs)
                foreach (var j in junctions.Where(j => Flat(j.centre, hub) < 1200f).OrderBy(j => Flat(j.centre, hub))
                                           .GroupBy(j => new Vector2Int(Mathf.RoundToInt(j.centre.x / 30f), Mathf.RoundToInt(j.centre.z / 30f)))
                                           .Select(g => g.First()).Take(2))
                {
                    var side = new Vector3(j.dir.z, 0f, -j.dir.x);
                    var at = j.centre - j.dir * (j.half + 3.5f) + side * (j.half + 1.6f);
                    _clear.Push(ref at, side, 0.4f, 4f);
                    if (!Free(at))
                        continue;
                    var officer = Officer(parent, OnGround(at), Yaw(j.centre - at), Activity.Wave, "Traffic officer", vest, cap);
                    officer.AddComponent<PolicePost>().Configure(PolicePost.Kind.Officer, 80f);
                    officer.AddComponent<Npc>().Configure($"police:{count}", "Traffic officer", "police_chat", officer.GetComponent<PersonAnimator>());
                    count++;
                }

            // Foot patrols: two officers walking a stretch of footpath near each sector centre.
            foreach (var hub in hubs)
            {
                var path = vehicles.Paths.Where(p => p.points.Length > 20 && p.halfWidth > 2f)
                                         .OrderBy(p => p.points.Min(q => Flat(q, hub))).FirstOrDefault();
                if (path == null)
                    continue;
                int nearest = 0;
                for (int i = 1; i < path.points.Length; i++)
                    if (Flat(path.points[i], hub) < Flat(path.points[nearest], hub))
                        nearest = i;
                int from = Mathf.Max(0, nearest - 12), to = Mathf.Min(path.points.Length - 1, nearest + 12);
                for (int k = 0; k < 2; k++)
                {
                    var route = new List<Vector3>();
                    for (int i = from; i <= to; i++)
                    {
                        var d = path.points[Mathf.Min(i + 1, path.points.Length - 1)] - path.points[Mathf.Max(i - 1, 0)];
                        d.y = 0f;
                        var right = new Vector3(d.z, 0f, -d.x).normalized;
                        var p = path.points[i] + right * (path.halfWidth + 1.3f + k * 0.8f);
                        route.Add(OnSurface(p));
                    }
                    if (route.Count < 4)
                        continue;
                    var holder = new GameObject($"Foot patrol {count}").transform;
                    holder.SetParent(parent, false);
                    var officer = Officer(holder, route[0], 0f, Activity.Walk, "Police officer on patrol", vest, cap);
                    officer.AddComponent<StreetWalker>().Configure(route.ToArray(), 1.1f, new Vector2(6f, 15f));
                    officer.AddComponent<Npc>().Configure($"police:{count}", "Police officer", "police_chat", officer.GetComponent<PersonAnimator>());
                    count++;
                }
            }
            return count;
        }

        // A person in a hi-vis vest and a dark cap (stand-in uniform; no insignia).
        static GameObject Officer(Transform parent, Vector3 at, float yaw, Activity activity, string label, Material vest, Material cap)
        {
            var person = Person(parent, at, yaw, activity, _rng.NextDouble() < 0.4, label);
            var bounds = EditorAssetUtil.RendererBounds(person);
            float h = Mathf.Max(1.4f, bounds.size.y);
            var v = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(v.GetComponent<Collider>());
            v.name = "Hi-vis vest";
            v.transform.SetParent(person.transform, false);
            v.transform.localPosition = new Vector3(0f, h * 0.66f, 0f);
            v.transform.localScale = new Vector3(0.36f, h * 0.11f, 0.25f);
            v.GetComponent<Renderer>().sharedMaterial = vest;
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(c.GetComponent<Collider>());
            c.name = "Cap";
            c.transform.SetParent(person.transform, false);
            c.transform.localPosition = new Vector3(0f, h * 0.985f, 0.01f);
            c.transform.localScale = new Vector3(0.22f, 0.035f, 0.24f);
            c.GetComponent<Renderer>().sharedMaterial = cap;
            return person;
        }

        // ---------- crossings ----------

        static int Crossings(Transform parent, RoadNetworkTraffic vehicles, Vector3[] hubs)
        {
            if (vehicles == null || vehicles.Signals == null)
                return 0;
            var crossings = new List<CrossingWalkers.Crossing>();
            for (int i = 0; i < vehicles.StopCount; i++)
            {
                if (!vehicles.StopLines(i, out var fl, out var fd, out var rl, out var rd, out int junction, out int phase))
                    continue;
                foreach (var (line, dir) in new[] { (fl, fd), (rl, rd) })
                {
                    // The zebra lies between the stop line and the junction (5.5-8.5 m from its centre).
                    var centre = line + dir * (vehicles.StopLineBack - 7f);
                    if (!hubs.Any(h => Flat(h, centre) < 900f) || crossings.Any(x => Flat((x.a + x.b) * 0.5f, centre) < 5f))
                        continue;
                    float half = HalfWidthAt(vehicles, line);
                    var side = new Vector3(dir.z, 0f, -dir.x);
                    crossings.Add(new CrossingWalkers.Crossing
                    {
                        a = OnSurface(centre - side * (half + 1.2f)), b = OnSurface(centre + side * (half + 1.2f)),
                        junction = junction, phase = phase,
                    });
                }
            }
            if (crossings.Count == 0)
                return 0;
            var host = new GameObject("Zebra crossings (cross on red for the cars)");
            host.transform.SetParent(parent, false);
            var walkers = new List<CrossingWalkers.Walker>();
            for (int c = 0; c < crossings.Count; c++)
                for (int k = 0, n = _rng.Next(1, 3); k < n; k++)
                {
                    var cluster = Cluster(host.transform, $"Crossing {c}", (crossings[c].a + crossings[c].b) * 0.5f, 120f);
                    var body = Person(cluster, crossings[c].a, 0f, Activity.Walk, _rng.NextDouble() < 0.5, $"Pedestrian crossing {c}");
                    AddCapsule(body);
                    walkers.Add(new CrossingWalkers.Walker { body = body.transform, crossing = c, lateral = (k - 1) * 0.7f });
                }
            host.AddComponent<CrossingWalkers>().Configure(vehicles.Signals, crossings.ToArray(), walkers.ToArray());
            return walkers.Count;
        }

        static float HalfWidthAt(RoadNetworkTraffic vehicles, Vector3 p)
        {
            float best = float.MaxValue, half = 3.5f;
            foreach (var path in vehicles.Paths)
                for (int i = 0; i + 1 < path.points.Length; i++)
                {
                    float d = Flat(p, Closest(p, path.points[i], path.points[i + 1]));
                    if (d < best)
                    {
                        best = d;
                        half = path.halfWidth;
                    }
                }
            return half;
        }

        // ---------- people chatting and campus life ----------

        static int Gatherings(Transform parent, RoadNetworkTraffic vehicles, Vector3[] hubs)
        {
            if (vehicles == null)
                return 0;
            int people = 0, groups = 0;
            var spots = new List<Vector3>();
            foreach (var path in vehicles.Paths)
                for (int i = 0; i < path.points.Length; i += 18)
                {
                    var p = path.points[i];
                    if (!hubs.Any(h => Flat(h, p) < 900f))
                        continue;
                    var d = path.points[Mathf.Min(i + 1, path.points.Length - 1)] - path.points[Mathf.Max(i - 1, 0)];
                    d.y = 0f;
                    var right = new Vector3(d.z, 0f, -d.x).normalized;
                    var at = p + right * (path.halfWidth + (_rng.NextDouble() < 0.5 ? 2.2f : 3.6f)) * (_rng.NextDouble() < 0.5 ? 1f : -1f);
                    if (spots.Any(s => Flat(s, at) < 40f) || !_clear.Clear(at, 1.4f) || !Free(at))
                        continue;
                    spots.Add(at);
                }
            foreach (var at in spots.Take(45))
            {
                people += Circle(parent, OnSurface(at), _rng.Next(2, 5), $"Chat {groups}");
                groups++;
            }
            return people;
        }

        // Two to four people facing each other, talking.
        static int Circle(Transform parent, Vector3 centre, int size, string label)
        {
            var host = Cluster(parent, label, centre, 90f);
            int n = 0;
            float start = (float)_rng.NextDouble() * 360f;
            for (int i = 0; i < size; i++)
            {
                float a = (start + i * 360f / size) * Mathf.Deg2Rad;
                var p = centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.75f;
                if (!Free(p))
                    continue;
                p = OnSurface(p);
                Person(host, p, Yaw(centre - p), Activity.Talk, _rng.NextDouble() < 0.5, label);
                n++;
            }
            return n;
        }

        static int Campus(Transform parent, Vector3 campus)
        {
            if (campus == Vector3.zero)
                return 0;
            int people = 0;
            var spots = new List<Vector3>();
            for (int tries = 0; tries < 600 && spots.Count < 40; tries++)
            {
                float a = (float)_rng.NextDouble() * Mathf.PI * 2f, r = 10f + (float)_rng.NextDouble() * 120f;
                var p = campus + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (spots.Any(s => Flat(s, p) < 9f) || !_clear.Clear(p, 1.5f) || !Free(p) || !Free(p + Vector3.right * 1.2f) || !Free(p - Vector3.right * 1.2f))
                    continue;
                spots.Add(p);
            }
            // Groups of students talking.
            int groups = Mathf.Min(12, spots.Count / 2);
            for (int i = 0; i < groups; i++)
                people += Circle(parent, OnSurface(spots[i]), _rng.Next(2, 5), $"Students {i}");
            // Students walking between buildings.
            var walk = Group(parent, "Students walking");
            for (int i = groups; i + 1 < spots.Count && i < groups + 16; i += 2)
            {
                var route = Line(spots[i], spots[i + 1]);
                if (route == null)
                    continue;
                var host = Cluster(walk, $"Walker {i}", spots[i], 120f);
                var s = Person(host, route[0], 0f, Activity.Walk, _rng.NextDouble() < 0.5, "Student");
                s.AddComponent<StreetWalker>().Configure(route, 1.25f, new Vector2(5f, 20f));
                people++;
            }
            // Front desk and canteen.
            if (spots.Count > 0)
            {
                var desk = OnSurface(spots[spots.Count - 1]);
                var r = Person(parent, desk, 0f, Activity.Idle, true, "ALU receptionist");
                r.AddComponent<Npc>().Configure("alu_reception", "Receptionist at ALU", "svc_alu", r.GetComponent<PersonAnimator>(), "ALU");
                people++;
            }
            if (spots.Count > 1)
            {
                var canteen = OnSurface(spots[spots.Count - 2]);
                var k = Person(parent, canteen, 0f, Activity.Idle, false, "ALU canteen");
                k.AddComponent<Npc>().Configure("alu_canteen", "Canteen staff at ALU", "svc_food", k.GetComponent<PersonAnimator>(), "the ALU canteen");
                people++;
            }
            return people;
        }

        // A walkable straight route between two points (on the surface every 2 m), or null if something is in the way.
        static Vector3[] Line(Vector3 a, Vector3 b)
        {
            int steps = Mathf.Max(2, Mathf.CeilToInt(Flat(a, b) / 2f));
            var pts = new Vector3[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector3.Lerp(a, b, (float)i / steps);
                if (!Free(p))
                    return null;
                pts[i] = OnSurface(p);
            }
            return pts;
        }

        // ---------- helpers ----------

        static GameObject Person(Transform parent, Vector3 at, float yaw, Activity activity, bool female, string label)
        {
            var pool = female ? _women : _men;
            var model = pool[_rng.Next(pool.Length)];
            _people++;
            return PcPeople.Spawn(model, parent, at, yaw, activity, _rng, label, brain: true);
        }

        static void AddCapsule(GameObject body)
        {
            var capsule = body.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.85f, 0f);
            capsule.height = 1.7f;
            capsule.radius = 0.28f;
            var rb = body.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static Transform Cluster(Transform parent, string name, Vector3 at, float radius)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.position = at;
            t.gameObject.AddComponent<NearbyOnly>().Configure(radius);
            return t;
        }

        static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        static float Ground(Vector3 p) => _terrain.SampleHeight(p) + _terrain.transform.position.y;
        static Vector3 OnGround(Vector3 p) => new Vector3(p.x, Ground(p), p.z);
        static float Surface(Vector3 p) => PcRoadBuilder.SurfaceHeight(_terrain, p);
        static Vector3 OnSurface(Vector3 p) => new Vector3(p.x, Surface(p), p.z);

        static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            ab.y = 0f;
            var ap = p - a;
            ap.y = 0f;
            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector3.Lerp(a, b, t);
        }

        // Nothing solid here: looking down from above, the first thing hit is the ground or a road, not a building,
        // wall, car or prop.
        static bool Free(Vector3 p)
        {
            var from = new Vector3(p.x, Ground(p) + 40f, p.z);
            if (!Physics.Raycast(from, Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore))
                return true;
            if (hit.collider is TerrainCollider)
                return true;
            var parent = hit.collider.transform.parent;
            return parent != null && parent.name == PcRoadBuilder.GroupName;
        }

        // Building footprints near a point (for hanging signs on the right building).
        sealed class FootprintIndex
        {
            const float Cell = 40f;
            readonly DistrictImporter.Building[] _buildings;
            readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();

            public FootprintIndex(DistrictImporter.Building[] buildings)
            {
                _buildings = buildings;
                for (int i = 0; i < buildings.Length; i++)
                {
                    var c = PcCityLife.Centroid(buildings[i].pts);
                    long k = Key(Mathf.FloorToInt(c.x / Cell), Mathf.FloorToInt(c.z / Cell));
                    if (!_cells.TryGetValue(k, out var list))
                        _cells[k] = list = new List<int>();
                    list.Add(i);
                }
            }

            static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

            public float[] Points(int i) => _buildings[i].pts;

            // The building containing p, else the one whose centre is nearest within maxDistance.
            public bool Find(Vector3 p, float maxDistance, out int index)
            {
                index = -1;
                float best = maxDistance;
                int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!_cells.TryGetValue(Key(cx + dx, cz + dz), out var list))
                            continue;
                        foreach (int i in list)
                        {
                            var pts = _buildings[i].pts;
                            if (PcCityLife.Area(pts) < 20f)
                                continue;
                            if (Inside(pts, p))
                            {
                                index = i;
                                return true;
                            }
                            float d = Flat(PcCityLife.Centroid(pts), p);
                            if (d < best)
                            {
                                best = d;
                                index = i;
                            }
                        }
                    }
                return index >= 0;
            }

            static bool Inside(float[] pts, Vector3 p)
            {
                bool inside = false;
                int n = pts.Length / 2;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    float xi = pts[i * 2], zi = pts[i * 2 + 1], xj = pts[j * 2], zj = pts[j * 2 + 1];
                    if ((zi > p.z) != (zj > p.z) && p.x < (xj - xi) * (p.z - zi) / (zj - zi) + xi)
                        inside = !inside;
                }
                return inside;
            }
        }
    }
}
