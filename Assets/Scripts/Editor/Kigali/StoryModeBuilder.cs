using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KR.Editor.GIS;
using KR.Editor.Util;
using KR.Gameplay.Player;
using KR.Gameplay.Story;
using KR.Gameplay.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KR.Editor.Kigali
{
    // Adds Story Mode ("Rush Week") to the Kimironko–Kibagabaga scene: writes the milestone, rule, knowledge-card
    // and news content as assets, places milestones at real OSM locations (ALU campus, Kimironko Market,
    // Kibagabaga), sets up police posts and speed cameras, and wires the director, rule monitor, live weather,
    // Kigali clock, rush-hour traffic and HUD. Re-running rebuilds the content and replaces the Story Mode objects.
    public static class StoryModeBuilder
    {
        public const string ScenePath = "Assets/Scenes/PC Only - Story Kimironko Kibagabaga.unity";
        const string District = "kimironko_kibagabaga";
        const string Out = "Assets/World/Story";
        const string Law = "Law 014/2026";

        [Serializable] class PlaceList { public Place[] places; }
        [Serializable] class Place { public string kind; public string name; public float[] pts; }

        [MenuItem("Kigali Rush/Story/Add Story Mode to Kimironko scene")]
        static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Build();
        }

        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorAssetUtil.EnsureFolder($"{Out}/Milestones");

            var motor = UnityEngine.Object.FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Include);
            var rig = UnityEngine.Object.FindFirstObjectByType<PlayerRig>(FindObjectsInactive.Include);
            var cycle = UnityEngine.Object.FindFirstObjectByType<DayNightCycle>(FindObjectsInactive.Include);
            var weatherCycle = UnityEngine.Object.FindFirstObjectByType<WeatherCycle>(FindObjectsInactive.Include);
            // The district terrain, not one of the eight "Surround" tiles around it (they share the Terrain type).
            var terrain = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => !t.name.StartsWith("Surround"))
                .OrderBy(t => t.transform.position.sqrMagnitude)
                .FirstOrDefault();
            var vehicles = UnityEngine.Object.FindObjectsByType<RoadNetworkTraffic>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => !t.IsWalkers);
            if (motor == null || terrain == null)
            {
                Debug.LogError("[StoryMode] The scene has no City Life player or terrain. Build it first (BatchTasks.BuildStoryScene).");
                return;
            }

            // Real places from the OSM extract, in the scene's metre coordinates.
            var places = JsonUtility.FromJson<PlaceList>(File.ReadAllText(Path.Combine(DistrictImporter.SourceDir, District, "places_unity.json"))).places;
            Vector3 Centre(string name)
            {
                var p = places.FirstOrDefault(x => string.Equals(x.name, name, StringComparison.OrdinalIgnoreCase));
                if (p == null)
                    throw new InvalidOperationException($"Place '{name}' not in places_unity.json");
                float sx = 0f, sz = 0f;
                int n = p.pts.Length / 2;
                for (int i = 0; i < n; i++)
                {
                    sx += p.pts[i * 2];
                    sz += p.pts[i * 2 + 1];
                }
                return OnGround(terrain, new Vector3(sx / n, 0f, sz / n));
            }
            Vector3 campus = Centre("African Leadership University");
            Vector3 market = Centre("Kimironko Market");
            Vector3 hospital = Centre("kibagabaga hospital");

            // Home: the player's Kibagabaga home (PlayerBuilder), otherwise a street near the hospital.
            Vector3 home = rig != null && rig.Home != Vector3.zero ? rig.Home : hospital + new Vector3(220f, 0f, -120f);
            home = OnGround(terrain, home);
            Vector3 mama = RoadPointNear(vehicles, terrain, home, 350f);
            Vector3 campusGate = RoadPointNear(vehicles, terrain, campus, 0f);

            var cards = Cards();
            var milestones = new List<MilestoneDefinition>();
            Vector3 from = home;
            MilestoneDefinition Add(string id, int day, string title, string brief, string placeName, Vector3 at, float radius,
                                    float dwell, string dwellLabel, string anim, string card, float opensAt, string speaker = null, params string[] lines)
            {
                var m = EditorAssetUtil.LoadOrCreate<MilestoneDefinition>($"{Out}/Milestones/{id}.asset");
                m.id = id;
                m.day = day;
                m.title = title;
                m.brief = brief;
                m.placeName = placeName;
                m.position = at;
                m.radius = radius;
                m.dwellSeconds = dwell;
                m.dwellLabel = dwellLabel;
                m.dwellAnimation = anim;
                m.knowledgeCardId = card;
                m.opensAtHour = opensAt;
                m.speaker = speaker;
                m.arrivalLines = lines;
                // Par: road distance (straight line x 1.35) at a town pace of 7 m/s, plus the stay and a minute of slack.
                m.parSeconds = Mathf.Round(Flat(from, at) * 1.35f / 7f + dwell + 60f);
                EditorUtility.SetDirty(m);
                milestones.Add(m);
                from = at;
                return m;
            }

            // Characters stand at the roadside near their places (fictional people; lines in story_script.json).
            Vector3 roadNearHome = RoadPointNear(vehicles, terrain, home, 650f);
            Vector3 officerAt = Roadside(vehicles, terrain, roadNearHome, 4.5f);
            Vector3 mamanAt = Roadside(vehicles, terrain, mama, 4.5f);
            Vector3 alineAt = Roadside(vehicles, terrain, campusGate, 5f);
            Vector3 librarianAt = Roadside(vehicles, terrain, RoadPointNear(vehicles, terrain, campusGate, 25f), 5f);
            Vector3 vendorAt = Roadside(vehicles, terrain, RoadPointNear(vehicles, terrain, market, 20f), 5f);
            // Parking test: a marked bay at the kerb about 60 m from the campus gate, along the road.
            Vector3 bayRoad = RoadPointNear(vehicles, terrain, campusGate, 60f);
            Vector3 bayDir = RoadDirection(vehicles, bayRoad);
            Vector3 bayAt = OnGround(terrain, bayRoad + Vector3.Cross(Vector3.up, bayDir) * 3.2f);
            float bayYaw = Quaternion.LookRotation(bayDir).eulerAngles.y;

            MilestoneDefinition Talk(MilestoneDefinition m, string conversation)
            {
                m.kind = MissionKind.Talk;
                m.conversationId = conversation;
                m.parSeconds += 90f;            // time to talk
                EditorUtility.SetDirty(m);
                return m;
            }

            // Rush Week. Monday: first class, the library, tea with Maman Fils. Tuesday: a road lesson with Officer
            // Mukamana, the parking test, the market, the group meeting. Wednesday: the poster photo, submission and
            // thanking Maman Fils. Story script: Documentation/story-script.md.
            Add("mon_1_class", 0, "Make the 9:00 class", "Your first class at ALU starts at 9:00 and you are still at home in Kibagabaga. Morning traffic builds up fast; walk or drive, but the clock is already running.",
                "ALU campus", campusGate, 45f, 0f, null, null, "card_drive_right", 7.5f, "Aline", "You made it! Sit with us, the lecture is starting.");
            milestones[0].startAtPoint = true;
            milestones[0].startPoint = home;
            EditorUtility.SetDirty(milestones[0]);
            Talk(Add("mon_2_library", 0, "Find a book for the summative", "Your group needs the reading list for the summative. Ask Jean-Paul at the campus library which books to start with.",
                "ALU campus", librarianAt, 20f, 0f, null, null, "card_study", 9f), "jp_library");
            Talk(Add("mon_3_tea", 0, "Tea with Maman Fils", "Your friend's mother lives in Kibagabaga and has asked you for tea. Arriving late is rude, and so is leaving before the tea is finished.",
                "Kibagabaga", mamanAt, 20f, 0f, null, null, "card_greetings", 15f), "mf_tea");
            Talk(Add("tue_1_lesson", 1, "A road lesson with Officer Mukamana", "Officer Mukamana runs a short road-safety talk for new drivers near Kibagabaga. Listen, then answer her three questions (you need two right).",
                "Kibagabaga", officerAt, 20f, 0f, null, null, "card_officer", 7.5f), "off_lesson");
            var park = Add("tue_2_parking", 1, "The parking test", "Drive to the marked bay near the campus gate, stop inside it facing along the road, and shift to P.",
                "ALU car park", bayAt, 25f, 0f, null, null, "card_refuse_stop", 8.5f);
            park.kind = MissionKind.ParkInBay;
            park.bayYaw = bayYaw;
            park.parSeconds += 60f;
            EditorUtility.SetDirty(park);
            Talk(Add("tue_3_market", 1, "Fabric from Mzee Gakuba", "Your group's poster needs fabric for the display. Mzee Gakuba sells it at Kimironko Market; he closes his stall early today.",
                "Kimironko Market", vendorAt, 20f, 0f, null, null, "card_market", 10f), "gk_market");
            Add("tue_4_group", 1, "Group meeting on campus", "Meet your group on campus and put the summative together.",
                "ALU campus", campus, 70f, 30f, "Working with your group...", "Act_School", "card_cell", 14f);
            var photo = Add("wed_1_photo", 2, "A photo for the poster", "The poster needs a photo of the campus. Stand back so the buildings fill the middle of the frame, then take the shot.",
                "ALU campus", campus, 70f, 0f, null, null, null, 8f);
            photo.kind = MissionKind.Photo;
            photo.parSeconds += 60f;
            EditorUtility.SetDirty(photo);
            Add("wed_2_submit", 2, "Submit before the deadline", "The summative closes this afternoon. Get to campus and submit it with time to spare.",
                "ALU campus", campus, 70f, 15f, "Submitting...", "Act_School", null, 13f);
            Talk(Add("wed_3_thanks", 2, "Thank Maman Fils", "The summative is in. Stop by Maman Fils's house to tell her how it went and thank her for the tea.",
                "Kibagabaga", mamanAt, 20f, 0f, null, null, null, 16f), "mf_thanks");

            var week = EditorAssetUtil.LoadOrCreate<StoryWeek>($"{Out}/RushWeek.asset");
            week.title = "Rush Week";
            week.milestones = milestones.ToArray();
            week.rules = Rules();
            week.cards = cards;
            week.news = News();
            week.urbanSpeedLimitKmh = 40f;          // placeholder until the Minister's Order is supplied
            week.speedLimitFromOrder = false;
            EditorUtility.SetDirty(week);
            AssetDatabase.SaveAssets();

            // Hand edits to signs and names live under Story Mode; save them before it is replaced.
            EditableLayer.Keep();
            // Replace any earlier Story Mode objects.
            foreach (var old in new[] { "Story Mode", "Police posts" })
            {
                var go = GameObject.Find(old);
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);
            }
            var host = new GameObject("Story Mode");
            var beacon = Beacon(host.transform);
            var monitor = host.AddComponent<RoadRulesMonitor>();
            var stopSigns = UnityEngine.Object.FindFirstObjectByType<StopSigns>(FindObjectsInactive.Include);
            monitor.Configure(week, motor.transform, vehicles, stopSigns);
            var coach = host.AddComponent<DrivingCoach>();
            coach.Configure(monitor);
            var kigaliWeather = host.AddComponent<KigaliWeather>();
            kigaliWeather.Configure(weatherCycle);
            if (cycle != null)
                host.AddComponent<KigaliClock>().Configure(cycle);
            var rhythm = host.AddComponent<TrafficRhythm>();
            rhythm.Configure(vehicles);
            var director = host.AddComponent<StoryDirector>();
            director.Configure(week, motor.transform, motor, rig, monitor, kigaliWeather, rhythm, beacon, coach);
            host.AddComponent<StoryHud>().Configure(director);

            // Satnav: route planning on the road network, the maps and the route ribbon over the road.
            var navigator = host.AddComponent<Navigator>();
            navigator.Configure(director, rig, vehicles);
            Transform satnav = null;
            if (rig != null && rig.Car != null)
                foreach (var t in rig.Car.GetComponentsInChildren<Transform>(true))
                    if (t.name == PlayerBuilder.SatnavMount)
                        satnav = t;
            host.AddComponent<NavigationUI>().Configure(navigator, rig, satnav, RibbonMaterial());
            VrBuilder.Add(rig, director);

            int posts = PlacePolice(vehicles, terrain, new[] { market, campusGate, hospital });

            // Conversations, places, photos and teleport walking.
            var talk = host.AddComponent<ConversationRunner>();
            talk.Configure(director, rig);
            var catalog = BuildPlaces(places, terrain, home, campus, market, mamanAt);
            var discoveries = host.AddComponent<Discoveries>();
            discoveries.Configure(catalog, director, navigator);
            var cameraBody = EditorAssetUtil.LitMaterial(Out, "PhotoCamera", new Color(0.08f, 0.08f, 0.09f), 0.6f);
            host.AddComponent<PhotoCamera>().Configure(rig, director, cameraBody);
            host.AddComponent<StoryWiring>();
            var ribbon = RibbonMaterial();
            host.AddComponent<Teleporter>().Configure(rig, ribbon);
            int npcs = PlaceCharacters(host.transform, terrain, rig != null ? rig.Player.transform.position : home,
                (officerAt, "officer", "Officer Mukamana", "off_chat", PcPeople.Activity.Idle, true),
                (mamanAt, "maman_fils", "Maman Fils", "mf_chat", PcPeople.Activity.Talk, true),
                (alineAt, "aline", "Aline", "al_chat", PcPeople.Activity.Talk, true),
                (librarianAt, "jean_paul", "Jean-Paul", "jp_chat", PcPeople.Activity.Idle, false),
                (vendorAt, "gakuba", "Mzee Gakuba", "gk_chat", PcPeople.Activity.Vendor, false));
            ParkingBay(host.transform, bayAt, bayYaw);

            // The living city: real place names, working businesses, police, crossings, people, accident scene, satnav data.
            CityServicesBuilder.Build(new CityServicesBuilder.Context
            {
                Host = host.transform, Terrain = terrain, Vehicles = vehicles, Rig = rig, Director = director, Runner = talk,
                Nav = navigator, Catalog = catalog, District = District, Campus = campus, RouteMaterial = ribbon,
            });
            Debug.Log($"[StoryMode] {npcs} characters, {catalog.places.Length} places to discover ({catalog.tour.Length} on the Kimironko tour), parking bay at {bayAt:F0}.");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[StoryMode] Added Rush Week to {ScenePath}: {milestones.Count} milestones, {week.rules.Length} rules, {cards.Length} cards, " +
                      $"{posts} police posts. Home {home}, campus {campusGate}, market {market}, Maman Fils {mama}.");
        }

        // Glowing route ribbon: unlit, see-through cyan, drawn over the road.
        static Material RibbonMaterial()
        {
            const string path = "Assets/World/VR/M_RouteRibbon.mat";
            EditorAssetUtil.EnsureFolder("Assets/World/VR");
            AssetDatabase.DeleteAsset(path);
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetColor("_BaseColor", new Color(0.2f, 0.8f, 1f, 0.55f));
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        static Vector3 OnGround(Terrain t, Vector3 p) => new Vector3(p.x, t.SampleHeight(p) + t.transform.position.y, p.z);

        // A point on a vehicle road whose distance from 'origin' is closest to 'distance' (0 = nearest road point).
        static Vector3 RoadPointNear(RoadNetworkTraffic vehicles, Terrain terrain, Vector3 origin, float distance)
        {
            if (vehicles == null || vehicles.Paths == null)
                return origin;
            Vector3 best = origin;
            float bestErr = float.MaxValue;
            foreach (var path in vehicles.Paths)
                foreach (var p in path.points)
                {
                    float err = Mathf.Abs(Flat(p, origin) - distance);
                    if (err < bestErr)
                    {
                        bestErr = err;
                        best = p;
                    }
                }
            return OnGround(terrain, best);
        }

        // A point beside the road (to the right of its direction), on the ground.
        static Vector3 Roadside(RoadNetworkTraffic vehicles, Terrain terrain, Vector3 roadPoint, float offset)
        {
            var dir = RoadDirection(vehicles, roadPoint);
            return OnGround(terrain, roadPoint + Vector3.Cross(Vector3.up, dir) * offset);
        }

        static Vector3 RoadDirection(RoadNetworkTraffic vehicles, Vector3 at)
        {
            Vector3 best = Vector3.forward;
            float bestD = float.MaxValue;
            if (vehicles != null && vehicles.Paths != null)
                foreach (var path in vehicles.Paths)
                    for (int i = 1; i < path.points.Length; i++)
                    {
                        float d = Flat((path.points[i - 1] + path.points[i]) * 0.5f, at);
                        if (d < bestD)
                        {
                            bestD = d;
                            best = path.points[i] - path.points[i - 1];
                        }
                    }
            best.y = 0f;
            return best.sqrMagnitude > 0.01f ? best.normalized : Vector3.forward;
        }

        // The characters you can talk to: real animated people from the pedestrian set, with an Npc marker.
        static int PlaceCharacters(Transform parent, Terrain terrain, Vector3 faceTowards,
                                   params (Vector3 at, string id, string name, string chat, PcPeople.Activity role, bool female)[] people)
        {
            var cat = PcShowcaseBuilder.LoadCatalog();
            var women = cat.Pedestrians.Where(m => m != null && m.name.ToLowerInvariant().Contains("woman")).ToArray();
            var men = cat.Pedestrians.Where(m => m != null && !m.name.ToLowerInvariant().Contains("woman")).ToArray();
            PcPeople.Begin("Assets/World/PC/kimironko_kibagabaga/People");
            var host = new GameObject("Characters").transform;
            host.SetParent(parent, false);
            var rng = new System.Random(77);
            int n = 0;
            foreach (var p in people)
            {
                var pool = p.female && women.Length > 0 ? women : men.Length > 0 ? men : cat.Pedestrians;
                if (pool.Length == 0)
                    break;
                var model = pool[n % pool.Length];
                var to = faceTowards - p.at;
                float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                var holder = PcPeople.Spawn(model, host, p.at, yaw, p.role, rng, p.name, brain: true);
                holder.AddComponent<Npc>().Configure(p.id, p.name, p.chat, holder.GetComponent<PersonAnimator>());
                n++;
            }
            return n;
        }

        // White lines of a parking bay, 2.6 m x 5.5 m.
        static void ParkingBay(Transform parent, Vector3 at, float yaw)
        {
            var paint = EditorAssetUtil.LitMaterial(Out, "BayPaint", new Color(0.95f, 0.95f, 0.92f), 0.3f);
            var bay = new GameObject("Parking test bay").transform;
            bay.SetParent(parent, false);
            bay.SetPositionAndRotation(at + Vector3.up * 0.03f, Quaternion.Euler(0f, yaw, 0f));
            void Line(Vector3 local, Vector3 size)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
                g.name = "Line";
                g.transform.SetParent(bay, false);
                g.transform.localPosition = local;
                g.transform.localScale = size;
                g.GetComponent<Renderer>().sharedMaterial = paint;
                g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Line(new Vector3(-1.3f, 0f, 0f), new Vector3(0.12f, 0.02f, 5.5f));
            Line(new Vector3(1.3f, 0f, 0f), new Vector3(0.12f, 0.02f, 5.5f));
            Line(new Vector3(0f, 0f, 2.75f), new Vector3(2.72f, 0.02f, 0.12f));
            Line(new Vector3(0f, 0f, -2.75f), new Vector3(2.72f, 0.02f, 0.12f));
        }

        // Places to discover: public places from the OSM extract near each sector centre (markets, parks, schools,
        // universities, hospitals, bus stations, sports grounds, places of worship), plus the story's own places.
        // Private businesses (restaurants, bars, hotels, shops) are left out. Card text is left empty for a person
        // to write and review.
        static PlaceCatalog BuildPlaces(Place[] places, Terrain terrain, Vector3 home, Vector3 campus, Vector3 market, Vector3 maman)
        {
            var sectors = new (string name, Vector3 centre, float radius)[]
            {
                ("Kimironko", new Vector3(2910f, 0f, 2459f), 850f),
                ("Kibagabaga", new Vector3(1310f, 0f, 4446f), 750f),
                ("ALU campus", new Vector3(5657f, 0f, 4577f), 400f),
            };
            var kinds = new Dictionary<string, string>
            {
                ["market"] = "market", ["park"] = "park", ["school"] = "school", ["university"] = "university",
                ["hospital"] = "health centre", ["bus_station"] = "bus station", ["stadium"] = "sports ground",
                ["sports_centre"] = "sports ground", ["pitch"] = "sports ground", ["worship"] = "place of worship",
            };
            var list = new List<PlaceRecord>();
            var used = new HashSet<string>();
            void AddPlace(string id, string name, string category, Vector3 at, float radius)
            {
                string sector = null;
                foreach (var s in sectors)
                    if (Flat(at, s.centre) < s.radius)
                        sector = s.name;
                if (sector == null || !used.Add(id))
                    return;
                list.Add(new PlaceRecord { id = id, name = name, category = category, sector = sector,
                                           position = OnGround(terrain, at), radius = radius, xp = 50 });
            }
            AddPlace("alu_campus", "African Leadership University", "university", campus, 70f);
            AddPlace("kimironko_market", "Kimironko Market", "market", market, 45f);
            AddPlace("maman_fils", "Maman Fils's house", "home", maman, 20f);
            foreach (var p in places)
            {
                if (string.IsNullOrWhiteSpace(p.name) || p.pts == null || p.pts.Length < 2 || !kinds.TryGetValue(p.kind, out var category))
                    continue;
                float sx = 0f, sz = 0f;
                int n = p.pts.Length / 2;
                for (int i = 0; i < n; i++)
                {
                    sx += p.pts[i * 2];
                    sz += p.pts[i * 2 + 1];
                }
                string id = new string(p.name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
                AddPlace(id, p.name.Trim(), category, new Vector3(sx / n, 0f, sz / n), 35f);
            }
            var catalog = EditorAssetUtil.LoadOrCreate<PlaceCatalog>($"{Out}/Places.asset");
            // Keep any card text already written for a place.
            var old = catalog.places != null ? catalog.places.ToDictionary(x => x.id, x => x) : new Dictionary<string, PlaceRecord>();
            foreach (var p in list)
                if (old.TryGetValue(p.id, out var o))
                {
                    p.cardText = o.cardText;
                    p.cardSource = o.cardSource;
                    p.reviewStatus = o.reviewStatus;
                }
            catalog.places = list.OrderBy(x => x.sector).ThenBy(x => x.name).ToArray();
            // Kimironko tour: the market, then the nearest unvisited Kimironko place each time (up to six stops).
            var stops = new List<PlaceRecord>();
            var left = catalog.places.Where(x => x.sector == "Kimironko").ToList();
            var at0 = left.FirstOrDefault(x => x.id == "kimironko_market");
            while (at0 != null && stops.Count < 6)
            {
                stops.Add(at0);
                left.Remove(at0);
                var from = at0.position;
                at0 = left.OrderBy(x => Flat(x.position, from)).FirstOrDefault();
            }
            catalog.tour = stops.Select(x => x.id).ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        static Transform Beacon(Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Milestone beacon";
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            var visual = go.transform;
            visual.localScale = new Vector3(3f, 60f, 3f);
            visual.localPosition = new Vector3(0f, 60f, 0f);
            var holder = new GameObject("Beacon").transform;
            holder.SetParent(parent, false);
            visual.SetParent(holder, false);
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{Out}/M_Beacon.mat");
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, $"{Out}/M_Beacon.mat");
            }
            mat.SetColor("_BaseColor", new Color(0.25f, 0.95f, 1f));
            EditorUtility.SetDirty(mat);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return holder;
        }

        // Speed cameras on three long roads. Traffic officers and foot patrols are real people now (CityServicesBuilder).
        static int PlacePolice(RoadNetworkTraffic vehicles, Terrain terrain, Vector3[] hubs)
        {
            var host = new GameObject("Police posts").transform;
            int count = 0;
            if (vehicles == null)
                return 0;
            var longest = vehicles.Paths.OrderByDescending(p => p.points.Length).Take(3).ToArray();
            foreach (var path in longest)
            {
                var mid = path.points[path.points.Length / 2];
                var cam = Stand(host, "Speed camera", OnGround(terrain, mid + new Vector3(path.halfWidth + 2f, 0f, 0f)), null);
                cam.AddComponent<PolicePost>().Configure(PolicePost.Kind.SpeedCamera, 60f);
                count++;
            }
            return count;
        }

        static GameObject Stand(Transform parent, string name, Vector3 at, Material vest)
        {
            var go = GameObject.CreatePrimitive(vest != null ? PrimitiveType.Capsule : PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            if (vest != null)
            {
                go.transform.position = at + Vector3.up * 0.9f;
                go.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
                go.GetComponent<Renderer>().sharedMaterial = vest;
            }
            else
            {
                go.transform.position = at + Vector3.up * 3f;
                go.transform.localScale = new Vector3(0.5f, 6f, 0.5f);
            }
            return go;
        }

        // ---------- content ----------

        static RoadRule[] Rules() => new[]
        {
            new RoadRule { id = RuleId.DriveOnRight, title = "Wrong side of the road", citation = $"{Law}, Art. 4",
                explanation = "Vehicles drive on the right-hand side of the road.", timePenalty = 20f, demeritPoints = 2 },
            new RoadRule { id = RuleId.ObeyOfficer, title = "Ignoring an officer", citation = $"{Law}, Art. 6(3)",
                explanation = "An officer's instructions take priority over road signs and signals.", timePenalty = 30f },
            new RoadRule { id = RuleId.RefuseStop, title = "Refusing to stop", citation = $"{Law}, Art. 39",
                explanation = "Refusing to stop when signalled by an officer is an offence: a fine of FRW 400,000 to 700,000 and 3 to 6 months' imprisonment.",
                timePenalty = 0f, fineFrw = 400000, endsRun = true, fromOrder = true },
            new RoadRule { id = RuleId.NoLicence, title = "Driving without a licence", citation = $"{Law}, Art. 40 and 12(1)(a)",
                explanation = "Driving without a licence is an offence (fine of FRW 100,000 to 200,000 and/or 15 to 30 days), and the vehicle is immobilised.",
                timePenalty = 120f, fineFrw = 100000, endsRun = true, fromOrder = true },
            new RoadRule { id = RuleId.RedSignal, title = "Ran a red signal", citation = $"{Law}, Art. 5(1)(g); details in the Minister's Order",
                explanation = "Road traffic signs and signals must be obeyed. The fine and points are set by the Minister's Order (not yet supplied).", timePenalty = 30f, demeritPoints = 3 },
            new RoadRule { id = RuleId.StopSign, title = "Did not stop at a STOP sign", citation = $"{Law}, Art. 5(1)(g); details in the Minister's Order",
                explanation = "At a STOP sign the vehicle must come to a complete stop at the line, then go only when the way is clear. The fine and points are set by the Minister's Order (not yet supplied).",
                timePenalty = 20f, demeritPoints = 2 },
            new RoadRule { id = RuleId.Speeding, title = "Speeding", citation = $"{Law}, Art. 5; limits in the Minister's Order",
                explanation = "Speed limits are set by the Minister's Order (not yet supplied); the game uses a placeholder town limit.", timePenalty = 25f, demeritPoints = 2 },
            new RoadRule { id = RuleId.Seatbelt, title = "No seatbelt", citation = "Minister's Order (not yet supplied)",
                explanation = "Placeholder rule until the Order's text is added.", timePenalty = 15f, demeritPoints = 1 },
            new RoadRule { id = RuleId.HelmetOnMoto, title = "No helmet on a moto", citation = "Minister's Order (not yet supplied)",
                explanation = "Placeholder rule until the Order's text is added.", timePenalty = 15f },
            new RoadRule { id = RuleId.AlcoholLimit, title = "Over the alcohol limit", citation = $"{Law}, Art. 9",
                explanation = "The maximum blood alcohol level for a driver is 0.80 grams per litre of blood.", timePenalty = 0f, endsRun = true, fromOrder = true },
        };

        static KnowledgeCard[] Cards() => new[]
        {
            new KnowledgeCard { id = "card_drive_right", theme = "road", title = "Drive on the right",
                textEn = "In Rwanda the driving side is the right-hand side of the road.", sources = new[] { "Law n° 014/2026 of 06/03/2026 governing road traffic, Art. 4 (Official Gazette, Special of 10/03/2026)" } },
            new KnowledgeCard { id = "card_officer", theme = "road", title = "Officers come first",
                textEn = "When an officer is directing traffic, their instructions take priority over road signs and signals.", sources = new[] { "Law n° 014/2026, Art. 6(3)" } },
            new KnowledgeCard { id = "card_refuse_stop", theme = "road", title = "Always stop when signalled",
                textEn = "Refusing to stop when an officer signals you is an offence: a fine of FRW 400,000 to 700,000 and 3 to 6 months' imprisonment.", sources = new[] { "Law n° 014/2026, Art. 39" } },
            new KnowledgeCard { id = "card_greetings", theme = "language", title = "Greetings for a visit",
                textEn = "Mwaramutse: good morning. Muraho: hello. Amakuru?: how are you? Murakoze: thank you. Icyayi: tea.",
                textRw = "Mwaramutse · Muraho · Amakuru? · Murakoze · Icyayi", sources = new[] { "TODO: native-speaker review" } },
            new KnowledgeCard { id = "card_market", theme = "place", title = "Kimironko Market",
                textEn = "Kimironko Market sits right next to Kimironko taxi park, so buses and motos arrive at its door.",
                sources = new[] { "OpenStreetMap: way 106506354 (market) and way 143205338 (taxi park), © OpenStreetMap contributors" } },
            new KnowledgeCard { id = "card_study", theme = "study", title = "Read in short blocks",
                textEn = "Reading in short, focused blocks with notes at the end of each one is easier to keep up during summative week.", sources = new[] { "TODO: add a study-skills source" } },
            new KnowledgeCard { id = "card_cell", theme = "place", title = "Kibagabaga is a cell",
                textEn = "Kibagabaga is a cell within Kimironko sector, Gasabo District.",
                sources = new[] { "OpenStreetMap: 'Kibagabaga Cell Office', way 206839949", "TODO: verify against Gasabo District's list" } },
        };

        // Written by the team: local, generic and non-political (CLAUDE.md section 4). Events are fictional.
        static string[] News() => new[]
        {
            "Kimironko: traders expect a busy market day. Allow extra time around the taxi park at midday.",
            "Students across Kigali enter summative week, and study spaces expect long hours.",
            "Rainy season reminder: afternoon showers are common, so carry an umbrella.",
            "Road safety: when an officer directs traffic, their instructions come before the lights.",
            "Kibagabaga: community football this weekend on the local pitch (fictional event).",
        };
    }
}
