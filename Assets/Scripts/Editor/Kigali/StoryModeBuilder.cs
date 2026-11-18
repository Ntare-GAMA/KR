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

            Add("mon_1_class", 0, "Make the 9:00 class", "Your first class at ALU starts at 9:00 and you are still at home in Kibagabaga. Morning traffic builds up fast; walking, a moto or the bus all work, but the clock is already running.",
                "ALU campus", campusGate, 45f, 0f, null, null, "card_drive_right", 7.5f);
            milestones[0].startAtPoint = true;
            milestones[0].startPoint = home;
            EditorUtility.SetDirty(milestones[0]);
            Add("mon_2_read", 0, "Read for your summative", "Find a quiet spot on campus and read the chapter your group needs before lunch.",
                "ALU campus", campus, 70f, 45f, "Reading...", "Act_School", "card_study", 9f);
            Add("mon_3_tea", 0, "Tea with Mama Ineza", "Your friend's mother lives in Kibagabaga and has asked you for tea. Arriving late is rude, and so is leaving before the tea is finished.",
                "Kibagabaga", mama, 20f, 40f, "Drinking tea with Mama Ineza...", "Act_Eat", "card_greetings", 15f,
                "Mama Ineza", "Mwaramutse! Come in, sit down, the icyayi is ready.");
            Add("tue_1_market", 1, "Kimironko Market run", "Your group project needs fabric and materials. The market is busiest around midday and closes in the evening.",
                "Kimironko Market", market, 40f, 25f, "Buying materials...", "Act_Market", "card_market", 10f);
            Add("tue_2_group", 1, "Group meeting on campus", "Meet your group on campus to put the summative together.",
                "ALU campus", campus, 70f, 30f, "Working with your group...", "Act_School", "card_refuse_stop", 14f);
            Add("wed_1_submit", 2, "Submit before the deadline", "The summative closes this afternoon. Get to campus and submit it with time to spare.",
                "ALU campus", campus, 70f, 15f, "Submitting...", "Act_School", "card_officer", 13f);

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

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[StoryMode] Added Rush Week to {ScenePath}: {milestones.Count} milestones, {week.rules.Length} rules, {cards.Length} cards, " +
                      $"{posts} police posts. Home {home}, campus {campusGate}, market {market}, Mama Ineza {mama}.");
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

        // Traffic officers at the signalised junction nearest each hub; speed cameras on three long roads.
        // Visuals are stand-ins (hi-vis capsules) until Mixamo officers with hand-signal clips replace them.
        static int PlacePolice(RoadNetworkTraffic vehicles, Terrain terrain, Vector3[] hubs)
        {
            var host = new GameObject("Police posts").transform;
            var vest = AssetDatabase.LoadAssetAtPath<Material>($"{Out}/M_HiVis.mat");
            if (vest == null)
            {
                vest = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(vest, $"{Out}/M_HiVis.mat");
            }
            vest.SetColor("_BaseColor", new Color(0.85f, 0.95f, 0.1f));
            vest.EnableKeyword("_EMISSION");
            vest.SetColor("_EmissionColor", new Color(0.35f, 0.4f, 0.02f));
            EditorUtility.SetDirty(vest);
            int count = 0;
            if (vehicles != null)
            {
                foreach (var hub in hubs)
                {
                    Vector3 best = Vector3.zero;
                    float bestD = float.MaxValue;
                    for (int i = 0; i < vehicles.StopCount; i++)
                        if (vehicles.StopLines(i, out var a, out _, out var b, out _, out _, out _))
                        {
                            Vector3 j = (a + b) * 0.5f;
                            float d = Flat(j, hub);
                            if (d < bestD)
                            {
                                bestD = d;
                                best = j;
                            }
                        }
                    if (bestD > 900f)
                        continue;
                    var officer = Stand(host, "Traffic officer", OnGround(terrain, best + new Vector3(6f, 0f, 6f)), vest);
                    officer.AddComponent<PolicePost>().Configure(PolicePost.Kind.Officer, 80f);
                    count++;
                }
                var longest = vehicles.Paths.OrderByDescending(p => p.points.Length).Take(3).ToArray();
                foreach (var path in longest)
                {
                    var mid = path.points[path.points.Length / 2];
                    var cam = Stand(host, "Speed camera", OnGround(terrain, mid + new Vector3(path.halfWidth + 2f, 0f, 0f)), null);
                    cam.AddComponent<PolicePost>().Configure(PolicePost.Kind.SpeedCamera, 60f);
                    count++;
                }
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
