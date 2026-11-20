using System.Collections.Generic;
using System.IO;
using System.Linq;
using KR.Editor.GIS;
using KR.Editor.Setup;
using KR.Editor.Util;
using KR.Gameplay.CameraRig;
using KR.Gameplay.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Activity = KR.Editor.Kigali.PcPeople.Activity;
using Category = KR.Editor.Kigali.ScatterCollector.Category;

namespace KR.Editor.Kigali
{
    // PC-only cinematic showcase of real Kigali districts built from Copernicus terrain and OpenStreetMap data.
    // Photo-scanned ground and roads, every building footprint as a pack building, real landmarks, working traffic
    // signals with queuing traffic, parked cars, pedestrians and everyday life, a day/night cycle and a Story Mode
    // film. Static objects are GPU-instanced (InstancedScatter). Not part of the Android build.
    // Data: © OpenStreetMap contributors (ODbL); Copernicus DEM GLO-30; textures Poly Haven (CC0).
    public static class PcShowcaseBuilder
    {
        const string Out = "Assets/World/PC";
        const string SimplePoly = "Assets/SimplePoly City - Low Poly Assets/Prefab";
        const string ModularStreets = "Assets/ModularLowpolyStreetsFree";
        const string Ithappy = "Assets/ithappy/Cartoon_City_Free/Prefabs";
        const string FastMesh = "Assets/FastMesh/Prefabs/Road";
        const string HyperSport = "Assets/HYPER SPORT";
        const string AwbmCars = "Assets/Awbmecreations/Mobile Optimize-Free Low Poly Cars/Prefabs";
        const string AllSky = "Assets/AllSkyFree";
        const string CityPack = "Assets/City Pack.undefined-zip";
        const string RangeRover = "Assets/Range Rover by IvOfficial - 8zk4o6nALW";
        const string DetailLayer = "PcDetail";

        // Owner decision 2026-11-01: the PC scenes are a private demo. The Range Rover depicts a real trademarked
        // vehicle, so set this false before any public build, video or screenshot.
        const bool IncludePrivateDemoAssets = true;

        // If signal heads face away from the traffic they control, set this to 180.
        const float SignalYawOffset = 0f;

        const float DressRadius = 1600f;
        const float TrafficRadius = 2600f;
        // Meta Quest (standalone) budgets: the headset renders every frame twice at 72-90 fps.
        // Only the play area (PlayArea) is built, so these budgets cover the neighbourhood the player can reach.
        const int TreeTarget = 1500;
        const int MaxCars = 80;
        const int MaxWalkers = 70;
        // Everything past ViewDistance is clipped; the haze (squared exponential fog) hides the edge.
        const float ViewDistance = 950f;
        const float FogDensity = 0.0019f;

        static readonly string[] ExtraBuiltInFolders = { "Assets/SimplePoly City - Low Poly Assets", ModularStreets, "Assets/Food Pack Mixed", "Assets/RPG Food & Drinks Pack" };
        static readonly string[] ExcludedVehicleNames = { "Military", "Monster", "Air Plane", "Futuristic", "Wheel", "Spoiler" };

        static Vector3[] _hubs;
        static string[] _hubNames;

        // Building volumes, kept for City Life's collision shells and home selection.
        internal struct Solid
        {
            public Vector3 Centre;
            public float Yaw, W, D, Bottom, Top;
            public bool House;
        }

        static readonly List<Solid> Solids = new List<Solid>();

        internal sealed class Catalog
        {
            public GameObject[] Palms, Trees, Bushes, Houses, MidRise, Towers, RoofProps, Vehicles, Lamps, BusStops, Bins,
                Benches, Hydrants, SpeedSigns, StopSigns, DirectionSigns, TrafficCameras, Bollards, Manholes,
                Roadworks, CafeSets, StreetLightMain, TwoWheelers, StreetBoxes, WashingLines, BusStopSigns, Atms, Pedestrians,
                Boxes, TrashBags;
            public GameObject Stadium, SignalMain;
        }

        [MenuItem("Kigali Rush/Scene/Build Story scene (Kimironko + Kibagabaga)")]
        static void BuildStory() =>
            Build("kimironko_kibagabaga", "Assets/Scenes/PC Only - Story Kimironko Kibagabaga.unity", "Kimironko, Kibagabaga");

        public static void Build(string district, string scenePath, string title)
        {
            string src = Path.Combine(DistrictImporter.SourceDir, district);
            if (!File.Exists(Path.Combine(src, "terrain.json")))
            {
                Debug.LogError($"[PcShowcase] No data at {src}. Run Tools/gis/make_district_terrain.py first.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            // Hand edits to road markings, signs and names live in the scene; save them before it is replaced.
            if (File.Exists(scenePath))
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                EditableLayer.Keep();
            }

            try
            {
                Progress("Converting materials", 0.01f);
                MaterialPipelineConverter.ConvertFolders(AssetCatalog.BuiltInFolders.Concat(ExtraBuiltInFolders).ToArray());
                string outDir = $"{Out}/{district}";
                EditorAssetUtil.EnsureFolder($"{outDir}/Textures");
                int detailLayer = EditorAssetUtil.EnsureLayer(DetailLayer);

                var meta = JsonUtility.FromJson<DistrictImporter.TerrainMeta>(File.ReadAllText(Path.Combine(src, "terrain.json")));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject($"{title} (PC showcase)").transform;
                float size = meta.unity_terrain.width_m;
                var centre = new Vector3(size * 0.5f, 0f, meta.unity_terrain.length_m * 0.5f);
                bool hasHubs = meta.hubs != null && meta.hubs.Length > 0;
                _hubs = hasHubs ? meta.hubs.Select(h => new Vector3(h.x, 0f, h.z)).ToArray() : new[] { centre };
                _hubNames = hasHubs ? meta.hubs.Select(h => h.name).ToArray() : new[] { title };

                Progress("Terrain", 0.03f);
                var terrain = DistrictImporter.BuildTerrain(src, meta, outDir, root);
                // Quest: a little coarser than desktop (1) to cut terrain triangles; roads are lifted enough for this
                // close by, and the haze covers the distance. Instanced terrain draws in far fewer calls.
                terrain.heightmapPixelError = 3;
                terrain.drawInstanced = true;
                terrain.basemapDistance = 150f;
                var roads = PcSurround.ClipToMap(
                    JsonUtility.FromJson<DistrictImporter.RoadList>(File.ReadAllText(Path.Combine(src, "roads_unity.json"))).roads, size);
                var buildings = JsonUtility.FromJson<DistrictImporter.BuildingList>(File.ReadAllText(Path.Combine(src, "buildings_unity.json"))).buildings;
                string placesPath = Path.Combine(src, "places_unity.json");
                var places = File.Exists(placesPath)
                    ? JsonUtility.FromJson<PcCityLife.PlaceList>(File.ReadAllText(placesPath)).places
                    : new PcCityLife.Place[0];

                // Build only the two sectors, the ALU campus and the roads that join them.
                PlayArea.Init(_hubs, _hubNames, roads, size);
                int allBuildings = buildings.Length;
                roads = PlayArea.Clip(roads, 120f);
                buildings = PlayArea.Filter(buildings);
                places = PlayArea.Filter(places);
                Debug.Log($"[PcShowcase] Play area: {roads.Length} road runs, {buildings.Length} of {allBuildings} buildings, {places.Length} places.");

                Solids.Clear();
                var grid = BuildOccupancy(buildings, roads, size);
                var life = new PcCityLife.Result();
                PcCityLife.CollectExclusions(life, places, NearHub);
                // ALU campus: modelled from the real site and footprints instead of generic buildings.
                var aluSite = AluCampus.Find(places);
                if (aluSite != null)
                    AluCampus.Reserve(aluSite, life, grid);

                Progress("Roads, lanes, crossings", 0.06f);
                var junctions = FindJunctions(roads);
                var surfaces = new PcRoadBuilder.Surfaces
                {
                    Asphalt = PcMaterials.Photo(outDir, "asphalt_02", 6f, 0.25f),
                    Cobble = PcMaterials.Photo(outDir, "cobblestone_floor_08", 2.5f, 0.15f),
                    Pavers = PcMaterials.Photo(outDir, "interlocking_concrete_pavers", 2f, 0.15f),
                    Grass = PcMaterials.Photo(outDir, "leafy_grass", 4f, 0.05f),
                    Kerb = PcRoadBuilder.KerbMaterial(outDir),
                    White = EditorAssetUtil.LitMaterial(outDir, "Paint_White", new Color(0.9f, 0.9f, 0.88f), 0.35f),
                    Yellow = EditorAssetUtil.LitMaterial(outDir, "Paint_Yellow", new Color(0.92f, 0.72f, 0.16f), 0.35f),
                };
                PcRoadBuilder.Build(roads, terrain, root, outDir, junctions, surfaces, district);

                var cat = LoadCatalog();
                var rng = new System.Random(2026);
                var scatter = new ScatterCollector(size, size, 1000f);
                var lamps = new List<Vector3>();
                var busStops = new List<(Vector3, float)>();
                var footpaths = new List<(Vector3, float)>();
                PcPeople.Begin($"{outDir}/People");

                Progress("Buildings", 0.12f);
                var gardens = new List<(Vector3, float, float, float)>();
                int buildingCount = PlaceBuildings(buildings, terrain, cat, scatter, rng, life, grid, FoundationPrefab(outDir), gardens);

                Progress("Street life", 0.4f);
                _clearance = new RoadClearance(roads);
                DressRoads(roads, terrain, cat, scatter, rng, lamps, busStops, footpaths, grid);
                var signals = BuildSignals(junctions, terrain, cat, scatter, rng, root, outDir);
                PlaceStopSigns(junctions, terrain, cat, scatter, rng, root);

                Progress("Landmarks, markets, sport, nightlife, community", 0.55f);
                PcCityLife.Build(new PcCityLife.Context
                {
                    Terrain = terrain, Root = root, Cat = cat, Scatter = scatter, Rng = rng, OutDir = outDir, District = district,
                    Grid = grid, Places = places, Roads = roads, BusStops = busStops, Footpaths = footpaths, NearHub = NearHub,
                }, life);

                if (aluSite != null)
                {
                    Progress("African Leadership University campus", 0.67f);
                    AluCampus.Build(new AluCampus.Context
                    {
                        Terrain = terrain, Root = root, OutDir = outDir, Cat = cat, Scatter = scatter,
                        Rng = new System.Random(4021), Roads = roads,
                    }, aluSite, buildings);
                }

                Progress("Trees", 0.68f);
                ScatterTrees(terrain, cat, scatter, rng, size, grid);

                Progress("City map", 0.73f);
                CityMapBaker.Bake(roads, buildings, outDir, root);

                Progress("Ground: lawns, terraces, verges, flower beds", 0.78f);
                var masks = PcGreenery.Paint(terrain, buildings, places, life.Markets, gardens, roads, _hubs, outDir);
                Progress("Conifers", 0.8f);
                var trunks = PcGreenery.PlantConifers(terrain, grid, roads, places, masks, _hubs, rng);

                // Every building, tree, lamp, parked car and piece of street furniture as an object you can select
                // and edit (CityObjects), each with its own collider.
                Progress("City objects (editable)", 0.81f);
                var cityRoots = CityObjects.Build(scatter, cat, terrain, root, DetailLayer, out int cityObjects);
                InstancedScatter instanced = null;

                Progress("Traffic", 0.82f);
                var traffic = BuildTraffic(roads, terrain, root, cat, rng, outDir, junctions, signals, out int trackBus, out int trackCar);
                if (traffic != null)
                    EditorAssetUtil.SetLayerRecursively(traffic.gameObject, detailLayer);
                Progress("Pedestrians", 0.88f);
                var walkers = BuildPedestrians(roads, terrain, root, cat, rng);
                if (walkers != null)
                    EditorAssetUtil.SetLayerRecursively(walkers, detailLayer);
                PcPeople.SnapToSurfaces(terrain);

                Progress("Lights, day and night, story", 0.93f);
                SetupAtmosphere(terrain, centre, outDir, district, out var sun, out var cam, out var volume);
                cam.gameObject.AddComponent<LayerCullDistances>().Configure(DetailLayer, 220f);
                cam.gameObject.AddComponent<LayerCullDistances>().Configure(CityObjects.TreeLayer, 350f);
                cam.gameObject.AddComponent<LayerCullDistances>().Configure(CityObjects.HouseLayer, 600f);

                var lampHost = new GameObject("Street lamp lights (pooled)");
                var pool = lampHost.AddComponent<StreetLightPool>();
                pool.Configure(lamps.ToArray());
                if (traffic != null)
                    new GameObject("Headlights (pooled)").AddComponent<HeadlightPool>().Configure(traffic);

                var cycle = new GameObject("Day/Night Cycle").AddComponent<DayNightCycle>();
                cycle.Configure(sun, instanced, pool, traffic != null ? traffic.transform : null, SkyTimeline(), 7.5f);
                cycle.ConfigureCity(cityRoots);
                PcGreenery.AddWeather(terrain, cycle, outDir);

                Progress("Player", 0.96f);
                PlayerBuilder.Build(new PlayerBuilder.Context
                {
                    Terrain = terrain, Root = root, OutDir = outDir, Cat = cat, Rng = rng,
                    Hubs = _hubs, HubNames = _hubNames, Roads = roads, Solids = Solids, Scatter = scatter, TreeTrunks = trunks, ObjectColliders = true,
                    Camera = cam,
                });

                EditorSceneManager.SaveScene(scene, scenePath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[PcShowcase] Saved {scenePath}: {buildingCount} buildings, {scatter.Count} instanced objects, " +
                          $"{lamps.Count} lamp heads, {junctions.Count(j => j.Signalised)} signalised junctions, " +
                          $"{(traffic != null ? traffic.Count : 0)} vehicles, {PcPeople.Spawned} people ({PcPeople.Humanoid} on Mixamo animation), landmarks: {string.Join(", ", life.Anchors.Keys)}. " +
                          "WASD walk, E car, F5 weather.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static void Progress(string info, float t) => EditorUtility.DisplayProgressBar("PC Only", info, t);

        static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        internal static bool NearHub(Vector3 p, float radius) => _hubs.Any(h => FlatDistance(p, h) < radius);

        static float R(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        static GameObject Pick(GameObject[] items, System.Random rng) => items == null || items.Length == 0 ? null : items[rng.Next(items.Length)];

        static Vector3 Ground(Terrain terrain, Vector3 p, float lift = 0f)
        {
            p.y = terrain.SampleHeight(p) + lift;
            return p;
        }

        // Signal phase of an approach, relative to the junction's own geometry: the most opposite pair of approaches
        // (the through road) is phase 0, the rest phase 1. A world-axis split gave both roads green at once on
        // junctions turned about 45 degrees.
        static int Phase(PcRoadBuilder.Junction j, Vector3 d)
        {
            d.y = 0f;
            d.Normalize();
            var dirs = j.Directions;
            if (dirs.Count == 0)
                return 0;
            int a = 0, b = dirs.Count > 1 ? 1 : 0;
            float most = float.MaxValue;
            for (int i = 0; i < dirs.Count; i++)
                for (int k = i + 1; k < dirs.Count; k++)
                {
                    float dot = Vector3.Dot(dirs[i], dirs[k]);
                    if (dot < most)
                    {
                        most = dot;
                        a = i;
                        b = k;
                    }
                }
            // The approach this direction belongs to (d may point either way along the road).
            int nearest = 0;
            float best = -1f;
            for (int i = 0; i < dirs.Count; i++)
            {
                float dot = Mathf.Abs(Vector3.Dot(dirs[i], d));
                if (dot > best)
                {
                    best = dot;
                    nearest = i;
                }
            }
            if (nearest == a || nearest == b)
                return 0;
            // A fourth arm roughly in line with the through road joins it.
            return dirs.Count >= 4 && Mathf.Abs(Vector3.Dot(dirs[nearest], dirs[a])) > 0.7f ? 0 : 1;
        }

        // ---------- Catalog ----------

        static GameObject[] Prefabs(string folder, System.Func<string, bool> match)
        {
            if (!AssetDatabase.IsValidFolder(folder))
                return new GameObject[0];
            return AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                                .Where(p => p != null && match(p.name)).OrderBy(p => p.name).ToArray();
        }

        static GameObject[] Join(params GameObject[][] sets) => sets.SelectMany(s => s).Where(p => p != null).Distinct().ToArray();

        static GameObject[] Named(string folder, params string[] names) => Prefabs(folder, names.Contains);

        // City Pack ships raw FBX/OBJ models (no prefabs); model assets instantiate like prefabs.
        static GameObject[] Models(params string[] cityPackFolders) =>
            cityPackFolders.SelectMany(f => ModelsIn($"{CityPack}/{f}")).ToArray();

        static IEnumerable<GameObject> ModelsIn(string folder) =>
            !AssetDatabase.IsValidFolder(folder)
                ? Enumerable.Empty<GameObject>()
                : AssetDatabase.FindAssets("t:Model", new[] { folder })
                               .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                               .Where(m => m != null).OrderBy(m => m.name);

        internal static Catalog LoadCatalog()
        {
            var street = AssetCatalog.Load();
            string sp = SimplePoly;
            var c = new Catalog
            {
                Palms = street.Palms,
                Trees = Join(Named($"{sp}/Natures", "Natures_Big Tree", "Natures_Fir Tree"), Named(ModularStreets, "Tree1", "Tree_1_comp"), Models("Tree")),
                Bushes = Join(Prefabs($"{sp}/Natures", n => n.StartsWith("Natures_Bush_") || n.StartsWith("Natures_Pot Bush")), street.Bushes,
                              Models("Planter & Bushes", "Flower Pot")),
                Houses = Prefabs($"{sp}/Buildings", n => n.StartsWith("Building_House_")),
                MidRise = Join(Prefabs($"{sp}/Buildings", n => n.StartsWith("Building_Residential_")), street.Buildings, new[] { street.UrbanBuilding },
                               Models("Big Building", "Brown Building", "Building Green", "Building Red", "Building Red Corner")),
                Towers = Join(Prefabs($"{sp}/Buildings", n => n.StartsWith("Building Sky_")),
                              Prefabs(Ithappy, n => n.StartsWith("Eco_Building_") && !n.Contains("NightLight"))),
                RoofProps = Join(Named($"{sp}/Props", "Props_Roof Antenna", "Props_Roof Solar Panel", "Props_Roof prop air", "Props_Roof_prop"),
                                 Models("Air conditioner", "Roof Exit")),
                Vehicles = Join(
                    // Separate-wheel versions, so wheels can spin.
                    Prefabs($"{sp}/Vehicles/Vehicle with Separated Wheels", n => n.StartsWith("Vehicle_")),
                    Prefabs(AwbmCars, n => !ExcludedVehicleNames.Any(x => n.Contains(x))),
                    Prefabs($"{Ithappy}/Cars", n => !ExcludedVehicleNames.Any(x => n.Contains(x))),
                    Prefabs(HyperSport, n => n.StartsWith("R") || n.StartsWith("Street Racer")),
                    Models("Bus", "Van", "Pickup Truck", "SUV", "Car", "Sports Car", "Sports Car-Gzj704DXdr", "Police Car"),
                    IncludePrivateDemoAssets ? ModelsIn(RangeRover).ToArray() : new GameObject[0]),
                Lamps = Join(new[] { street.StreetLight }, Named(ModularStreets, "Pole_light_comp"),
                             Named($"{sp}/Props", "Props_Street Light"), Named(FastMesh, "LampPost-1", "LampPost-2")),
                StreetLightMain = new[] { street.StreetLight },
                BusStops = Join(Named($"{sp}/Props", "Props_Bus Stop"), new[] { street.BusStop }, Models("Bus Stop")),
                Bins = Join(Named($"{sp}/Props", "Props_Dustbin"), Named(FastMesh, "TrashCan"), Named(ModularStreets, "Trash_can_1"),
                            Prefabs(Ithappy, n => n.StartsWith("Trash_Can_")), Models("Trash Can", "Dumpster")),
                Benches = Join(Prefabs($"{sp}/Props", n => n.StartsWith("Props_Bench_")), Named(ModularStreets, "Bench_1"), Models("Bench")),
                Hydrants = Join(Named($"{sp}/Props", "Props_Hydrant"), Named(ModularStreets, "Hydrant"), Models("Fire hydrant")),
                SpeedSigns = Join(Named($"{sp}/Props", "Props_Traffic Sign_speed limit"), Named(FastMesh, "RoadSigns-1", "RoadSigns-2", "RoadSigns-3", "StreetSign-1")),
                StopSigns = Join(Named($"{sp}/Props", "Props_Traffic Sign_stop"), Models("Stop sign")),
                DirectionSigns = Named(FastMesh, "DirectionalSign"),
                TrafficCameras = Named(FastMesh, "TrafficCameras"),
                Bollards = Named(FastMesh, "RoadBollard-1", "RoadBollard-2", "RoadBollard-3", "ParkingBollard"),
                Manholes = Join(Named(FastMesh, "ManholeCover"), Named(ModularStreets, "Sewer_hatch", "Sewer_grill"), Models("Manhole Cover")),
                Roadworks = Join(Named(FastMesh, "TrafficCones", "WarningTriangles", "RoadBarrier-1", "RoadBarrier-2"),
                                 Named($"{sp}/Props", "Props_Traffic cone", "Props_Traffic Control Barrier Fence"),
                                 Named(ModularStreets, "Traffic_cone"), Models("Cone", "Fence Piece")),
                CafeSets = Named(ModularStreets, "Cafe_table_1", "Cafe_chair_1"),
                TwoWheelers = Models("Motorcycle", "Bicycle"),
                StreetBoxes = Models("Power Box"),
                WashingLines = Models("Washing Line"),
                BusStopSigns = Models("Bus stop sign"),
                Atms = Models("ATM"),
                // Women: the City Pack women (Mixamo-rigged). Men: the Floreswa low-poly men (Rigify, mapped to Humanoid).
                Pedestrians = Join(Models("Animated Woman", "Animated Woman-9kF7eTDbhO"), Prefabs("Assets/Floreswa/Prefabs", n => n.StartsWith("male"))),
                Boxes = Models("Box"),
                TrashBags = Models("trah bag grey"),
                Stadium = Named($"{sp}/Buildings", "Building_Stadium").FirstOrDefault(),
                // One consistent signal model across the city, as in a real network.
                SignalMain = Named($"{sp}/Props", "Props_Traffic Signal_big").FirstOrDefault()
                             ?? Named(ModularStreets, "Traffic_light_EU_comp").FirstOrDefault()
                             ?? Models("Traffic Light").FirstOrDefault(),
            };
            // Colourless car bodies get paint; houses are modest, clean bungalows (CleanCity).
            c.Vehicles = CleanCity.PaintVehicles(c.Vehicles, $"{Out}/Vehicles");
            c.Houses = CleanCity.ModestHouses($"{Out}/Houses");
            Debug.Log("[PcShowcase] Catalog: " + string.Join(", ", typeof(Catalog).GetFields()
                .Where(f => f.FieldType == typeof(GameObject[]))
                .Select(f => $"{f.Name} {((GameObject[])f.GetValue(c)).Length}")) +
                $", stadium {(c.Stadium ? c.Stadium.name : "none")}, signal {(c.SignalMain ? c.SignalMain.name : "none")}");
            return c;
        }

        // ---------- Occupancy and junctions ----------

        static PcCityLife.Occupancy BuildOccupancy(DistrictImporter.Building[] buildings, DistrictImporter.Road[] roads, float size)
        {
            var grid = new PcCityLife.Occupancy(size, 4f);
            foreach (var b in buildings)
            {
                float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
                for (int i = 0; i + 1 < b.pts.Length; i += 2)
                {
                    x0 = Mathf.Min(x0, b.pts[i]); x1 = Mathf.Max(x1, b.pts[i]);
                    z0 = Mathf.Min(z0, b.pts[i + 1]); z1 = Mathf.Max(z1, b.pts[i + 1]);
                }
                grid.Mark(x0 - 1.5f, z0 - 1.5f, x1 + 1.5f, z1 + 1.5f);
            }
            foreach (var road in roads)
            {
                if (road.pts == null || !DistrictImporter.RoadWidths.TryGetValue(road.highway, out float width))
                    continue;
                float r = width * 0.5f + (PcRoadBuilder.WithFootpaths.Contains(road.highway) ? 3.5f : 1.5f);
                foreach (var p in PcRoadBuilder.ResampleLine(road.pts))
                    grid.Mark(p, r);
            }
            return grid;
        }

        // Shared OSM nodes where three or more segments meet. Signalised when two main roads meet near a hub.
        static List<PcRoadBuilder.Junction> FindJunctions(DistrictImporter.Road[] roads)
        {
            var nodes = new Dictionary<long, PcRoadBuilder.Junction>();
            foreach (var road in roads)
            {
                if (road.pts == null || road.pts.Length < 6 || !DistrictImporter.RoadWidths.TryGetValue(road.highway, out float width))
                    continue;
                bool major = PcRoadBuilder.Major.Contains(road.highway);
                int n = road.pts.Length / 3;
                for (int i = 0; i < n; i++)
                {
                    var p = new Vector3(road.pts[i * 3], 0f, road.pts[i * 3 + 2]);
                    long key = Key(p);
                    if (!nodes.TryGetValue(key, out var j))
                        nodes[key] = j = new PcRoadBuilder.Junction { Position = p };
                    void AddDir(int other)
                    {
                        var d = new Vector3(road.pts[other * 3], 0f, road.pts[other * 3 + 2]) - p;
                        if (d.sqrMagnitude < 0.01f)
                            return;
                        j.Directions.Add(d.normalized);
                        j.DirectionIsMajor.Add(major);
                    }
                    if (i > 0) AddDir(i - 1);
                    if (i < n - 1) AddDir(i + 1);
                    j.Width = Mathf.Max(j.Width, major ? width : 0f);
                }
            }
            var result = new List<PcRoadBuilder.Junction>();
            foreach (var j in nodes.Values)
            {
                if (j.Directions.Count < 3 || j.Width <= 0f)
                    continue;
                bool clustered = result.Any(o => o.Signalised && FlatDistance(o.Position, j.Position) < 40f);
                j.Signalised = !clustered && NearHub(j.Position, DressRadius) && j.DirectionIsMajor.Count(m => m) >= 2;
                result.Add(j);
            }
            return result;
        }

        static long Key(Vector3 p) => ((long)Mathf.RoundToInt(p.x * 10f) << 32) ^ (uint)Mathf.RoundToInt(p.z * 10f);

        // ---------- Buildings ----------

        // Every real footprint becomes a pack building fitted to its oriented box, except inside landmarks.
        static int PlaceBuildings(DistrictImporter.Building[] buildings, Terrain terrain, Catalog cat, ScatterCollector scatter,
                                  System.Random rng, PcCityLife.Result life, PcCityLife.Occupancy grid, GameObject foundation,
                                  List<(Vector3, float, float, float)> gardens)
        {
            int placed = 0;
            for (int k = 0; k < buildings.Length; k++)
            {
                if (k % 2000 == 0)
                    Progress($"Buildings {k}/{buildings.Length}", 0.12f + 0.28f * k / buildings.Length);
                var b = buildings[k];
                if (b.pts.Length < 6)
                    continue;
                float area = PcCityLife.Area(b.pts);
                if (area < 20f || PcCityLife.Excluded(life, PcCityLife.Centroid(b.pts)))
                    continue;

                GameObject prefab;
                Category category;
                float h;
                if (area < 250f && cat.Houses.Length > 0)
                {
                    prefab = Pick(cat.Houses, rng);
                    category = Category.House;
                    h = Mathf.Clamp(b.h, 3.8f, 7f);
                }
                else
                {
                    bool tower = area > 1500f && cat.Towers.Length > 0 && rng.NextDouble() < 0.15;
                    prefab = tower ? Pick(cat.Towers, rng) : Pick(cat.MidRise, rng);
                    category = Category.Large;
                    // OSM heights are mostly missing; large footprints are rarely single-storey, so estimate.
                    h = tower ? R(rng, 25f, 45f) : Mathf.Max(b.h, R(rng, 8f, 20f));
                }
                if (prefab == null || !PcCityLife.OrientedBox(b.pts, terrain, out var centre, out float yaw, out float w, out float d, out _))
                    continue;
                // The oriented box overshoots irregular footprints; shrinking it keeps neighbours from intersecting.
                w *= 0.94f;
                d *= 0.94f;
                // Sit on the highest ground under the plot, never buried; a foundation fills the slope below,
                // like Kigali's terraced plots.
                GroundRange(terrain, centre, yaw, w, d, out float lo, out float hi);
                scatter.Add(prefab, scatter.FitBox(prefab, centre, yaw, w, d, h, hi, 0.05f), category);
                Solids.Add(new Solid { Centre = centre, Yaw = yaw, W = w, D = d, Bottom = lo - 0.5f, Top = hi + h, House = category == Category.House });
                if (hi - lo > 0.2f && foundation != null)
                    scatter.Add(foundation, scatter.FitBox(foundation, centre, yaw, w * 1.02f, d * 1.02f, hi - lo + 0.45f, lo, 0.4f), category);
                placed++;

                var rot = Quaternion.Euler(0f, yaw, 0f);
                if (category == Category.House && NearHub(centre, DressRadius) && rng.NextDouble() < 0.7)
                    Garden(terrain, cat, scatter, rng, grid, centre, rot, w, d, gardens);
                if (category == Category.Large && cat.RoofProps.Length > 0)
                    for (int r = 0, count = rng.Next(1, 4); r < count; r++)
                    {
                        var p = centre + rot * new Vector3(R(rng, -w, w) * 0.3f, 0f, R(rng, -d, d) * 0.3f);
                        p.y = hi + h - 0.1f;
                        var prop = Pick(cat.RoofProps, rng);
                        scatter.Add(prop, scatter.Fit(prop, p, yaw + 90f * rng.Next(4), R(rng, 1.2f, 2.5f), 4f), Category.Prop);
                    }
                if (category == Category.Large && cat.CafeSets.Length > 0 && rng.NextDouble() < 0.12 && NearHub(centre, DressRadius))
                {
                    var front = centre + rot * new Vector3(0f, 0f, -(d * 0.5f + 2.5f));
                    for (int s = 0; s < 3; s++)
                    {
                        var set = cat.CafeSets[s % cat.CafeSets.Length];
                        var p = Ground(terrain, front + rot * new Vector3((s - 1) * 1.4f, 0f, 0f));
                        if (!Claim(grid, p, 0.8f))
                            continue;
                        scatter.Add(set, scatter.Fit(set, p, R(rng, 0f, 360f), set.name.Contains("table") ? 0.75f : 0.9f, 1.2f), Category.Prop);
                    }
                    var atm = Pick(cat.Atms, rng);
                    var atmPos = Ground(terrain, centre + rot * new Vector3(w * 0.5f + 1.9f, 0f, -d * 0.3f));
                    if (atm != null && rng.NextDouble() < 0.4 && Claim(grid, atmPos, 0.6f))
                        scatter.Add(atm, scatter.Fit(atm, atmPos, yaw + 90f, 2.1f, 1.5f), Category.Prop);
                    if (cat.TwoWheelers.Length > 1)
                        for (int bikeIndex = 0; bikeIndex < 2; bikeIndex++)
                        {
                            var p = Ground(terrain, front + rot * new Vector3(3.5f + bikeIndex * 0.8f, 0f, 0.5f));
                            if (Claim(grid, p, 0.5f))
                                scatter.Add(cat.TwoWheelers[1], scatter.Fit(cat.TwoWheelers[1], p, yaw + 90f, 1f, 1.9f), Category.Prop);
                        }
                }
                // Laundry drying in house yards: everyday Kigali.
                if (category == Category.House && cat.WashingLines.Length > 0 && rng.NextDouble() < 0.15 && NearHub(centre, DressRadius))
                {
                    var line = Pick(cat.WashingLines, rng);
                    var p = Ground(terrain, centre + rot * new Vector3(0f, 0f, d * 0.5f + 3.2f));
                    if (Claim(grid, p, 1.2f))
                        scatter.Add(line, scatter.Fit(line, p, yaw + 90f, 1.8f, 5f), Category.Prop);
                }
            }
            return placed;
        }

        // Lowest and highest ground under an oriented plot (centre, four corners, four edge midpoints).
        static void GroundRange(Terrain terrain, Vector3 centre, float yaw, float w, float d, out float lo, out float hi)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            lo = float.MaxValue;
            hi = float.MinValue;
            for (int sx = -1; sx <= 1; sx++)
                for (int sz = -1; sz <= 1; sz++)
                {
                    float y = terrain.SampleHeight(centre + rot * new Vector3(sx * w * 0.5f, 0f, sz * d * 0.5f));
                    lo = Mathf.Min(lo, y);
                    hi = Mathf.Max(hi, y);
                }
        }

        // Reserves a free spot in the occupancy grid; false if anything already stands there.
        static bool Claim(PcCityLife.Occupancy grid, Vector3 p, float radius)
        {
            if (!grid.Free(p, radius))
                return false;
            grid.Mark(p, radius);
            return true;
        }

        // A small front garden: shrubs and flowers around the house where the ground is free, plus a lawn.
        static void Garden(Terrain terrain, Catalog cat, ScatterCollector scatter, System.Random rng, PcCityLife.Occupancy grid,
                           Vector3 centre, Quaternion rot, float w, float d, List<(Vector3, float, float, float)> gardens)
        {
            gardens.Add((centre, rot.eulerAngles.y, w + 6f, d + 6f));
            for (int i = 0, n = rng.Next(2, 5); i < n; i++)
            {
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                var local = rng.NextDouble() < 0.5
                    ? new Vector3(side * (w * 0.5f + R(rng, 1.9f, 2.8f)), 0f, R(rng, -d, d) * 0.4f)
                    : new Vector3(R(rng, -w, w) * 0.4f, 0f, side * (d * 0.5f + R(rng, 1.9f, 2.8f)));
                var p = Ground(terrain, centre + rot * local);
                var plant = Pick(cat.Bushes, rng);
                if (plant != null && Claim(grid, p, 0.6f))
                    scatter.Add(plant, scatter.Fit(plant, p, R(rng, 0f, 360f), R(rng, 0.6f, 1.4f), 1.6f), Category.Prop);
            }
        }

        // A plain stone-coloured block, scaled per plot to fill the slope under a building.
        static GameObject FoundationPrefab(string outDir)
        {
            string path = $"{outDir}/Foundation.prefab";
            var mat = EditorAssetUtil.LitMaterial(outDir, "PC_Foundation", new Color(0.58f, 0.55f, 0.5f), 0.1f);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = "Foundation";
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ---------- Street life along roads ----------

        static void DressRoads(DistrictImporter.Road[] roads, Terrain terrain, Catalog cat, ScatterCollector scatter, System.Random rng,
                               List<Vector3> lamps, List<(Vector3, float)> busStops, List<(Vector3, float)> footpathSpots,
                               PcCityLife.Occupancy grid)
        {
            for (int r = 0; r < roads.Length; r++)
            {
                var road = roads[r];
                if (r % 100 == 0)
                    Progress($"Street life {r}/{roads.Length}", 0.4f + 0.12f * r / roads.Length);
                if (road.pts == null || road.pts.Length < 6 ||
                    !DistrictImporter.RoadWidths.TryGetValue(road.highway, out float width))
                    continue;
                var line = PcRoadBuilder.ResampleLine(road.pts);
                if (line.Count < 2 || !line.Any(p => NearHub(p, DressRadius)))
                    continue;

                bool major = PcRoadBuilder.Major.Contains(road.highway);
                bool footpaths = PcRoadBuilder.WithFootpaths.Contains(road.highway);
                bool oneway = road.oneway == "yes";
                float half = width * 0.5f, lift = PcRoadBuilder.SurfaceLift(road.highway);
                float kerbTop = lift + 0.12f;
                var lampPrefab = road.highway.StartsWith("trunk") || road.highway.StartsWith("primary")
                    ? Pick(cat.StreetLightMain, rng) ?? Pick(cat.Lamps, rng)
                    : Pick(cat.Lamps, rng);

                var next = new Dictionary<string, float>
                {
                    ["palm"] = R(rng, 0f, 16f), ["lamp"] = R(rng, 0f, 32f), ["stop"] = R(rng, 100f, 450f),
                    ["sign"] = R(rng, 60f, 300f), ["dir"] = R(rng, 300f, 900f), ["cam"] = R(rng, 500f, 1500f),
                    ["bin"] = R(rng, 20f, 90f), ["hydrant"] = R(rng, 40f, 150f), ["manhole"] = R(rng, 10f, 90f),
                    ["bush"] = R(rng, 0f, 25f), ["works"] = R(rng, 800f, 3000f), ["box"] = R(rng, 50f, 300f),
                    ["parked"] = R(rng, 5f, 25f), ["chat"] = R(rng, 60f, 200f),
                };
                bool lampLeft = false;
                var reserved = new List<Vector3>();
                float walked = 0f;
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    Vector3 a = line[i], b = line[i + 1];
                    float len = Vector3.Distance(a, b);
                    if (len < 0.01f)
                        continue;
                    Vector3 dir = (b - a) / len;
                    var right = new Vector3(dir.z, 0f, -dir.x);
                    float roadYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                    bool near = NearHub(a, DressRadius);

                    // Next event of this kind inside the current segment; events away from the hubs are skipped.
                    bool Due(string key, float spacingMin, float spacingMax, out Vector3 at)
                    {
                        while (next[key] <= walked + len)
                        {
                            at = a + dir * (next[key] - walked);
                            next[key] += R(rng, spacingMin, spacingMax);
                            if (near)
                                return true;
                        }
                        at = default;
                        return false;
                    }
                    // Beside this road, but never on any carriageway (another street meeting or bending close by).
                    Vector3 Side(Vector3 p, float offset, float extraLift)
                    {
                        var q = p + right * offset;
                        _clearance?.Push(ref q, right * (offset < 0f ? -1f : 1f), 0.35f, 3f);
                        return Ground(terrain, q, extraLift);
                    }

                    // Street lamps every ~32 m; on two-way main roads they alternate sides.
                    while (Due("lamp", footpaths ? 30f : 50f, footpaths ? 34f : 60f, out var p))
                        if (lampPrefab != null)
                        {
                            float side = major && !oneway && (lampLeft = !lampLeft) ? -1f : 1f;
                            // The pole stands at the kerb edge and the arm reaches over the road, whatever the model's pivot.
                            var m = PoleFit(scatter, lampPrefab, Side(p, side * (half + (footpaths ? 0.6f : 0.8f)), footpaths ? kerbTop : 0f),
                                            -right * side, 9f, 12f);
                            int lamp = scatter.Add(lampPrefab, m, Category.Lamp);
                            AddLampHeads(lampPrefab, m, scatter, lamps, lamp);
                        }
                    // No manhole covers or road works on the carriageway: Kigali's roads are kept clean, and props
                    // laid on the asphalt showed as patches.
                    while (Due("bin", 60f, 140f, out var p))
                    {
                        var bin = Pick(cat.Bins, rng);
                        if (bin != null)
                            scatter.Add(bin, scatter.Fit(bin, Side(p, half + (footpaths ? 2.65f : 1.2f), footpaths ? lift + 0.1f : 0f), roadYaw, 1f, 1.2f), Category.Prop);
                    }
                    while (Due("hydrant", 120f, 220f, out var p))
                    {
                        var hy = Pick(cat.Hydrants, rng);
                        if (hy != null)
                            scatter.Add(hy, scatter.Fit(hy, Side(p, half + (footpaths ? 2.7f : 1.3f), footpaths ? lift + 0.1f : 0f), roadYaw, 0.8f, 0.6f), Category.Prop);
                    }
                    while (Due("box", 200f, 420f, out var p))
                    {
                        var box = Pick(cat.StreetBoxes, rng);
                        if (box != null)
                            scatter.Add(box, scatter.Fit(box, Side(p, half + (footpaths ? 2.75f : 1.5f), footpaths ? lift + 0.1f : 0f), roadYaw - 90f, 1.4f, 1.2f), Category.Prop);
                    }
                    while (Due("chat", 120f, 260f, out var p))
                        if (footpaths)
                            footpathSpots.Add((Side(p, half + 1.6f, lift + 0.1f), roadYaw));

                    if (major)
                    {
                        // Footpath zones, measured from the carriageway edge: kerb strip (lamps, signs) to 1.0 m,
                        // walking strip 0.9-1.9 m, furniture strip 2.4-2.8 m, verge (shelters, stands, palms) beyond.
                        while (Due("stop", 350f, 600f, out var p))
                        {
                            var stop = Pick(cat.BusStops, rng);
                            if (stop == null)
                                continue;
                            scatter.Add(stop, scatter.Fit(stop, Side(p, half + 4.1f, 0.05f), roadYaw, 2.7f, 6f), Category.Prop);
                            var bench = Pick(cat.Benches, rng);
                            if (bench != null)
                                scatter.Add(bench, scatter.Fit(bench, Side(p + dir * 5f, half + 4.0f, 0.05f), roadYaw - 90f, 0.9f, 2f), Category.Prop);
                            var bollard = Pick(cat.Bollards, rng);
                            if (bollard != null)
                                foreach (float along in new[] { -4f, 9f })
                                    scatter.Add(bollard, scatter.Fit(bollard, Side(p + dir * along, half + 0.6f, kerbTop), roadYaw, 0.9f, 0.5f), Category.Prop);
                            var stopSign = Pick(cat.BusStopSigns, rng);
                            if (stopSign != null)
                                scatter.Add(stopSign, scatter.Fit(stopSign, Side(p - dir * 3f, half + 0.7f, kerbTop), roadYaw + 180f, 2.6f, 1f), Category.Prop);
                            MotoStand(cat, scatter, rng, terrain, p + dir * 14f, dir, right, half + 4.0f, 0.05f, roadYaw);
                            reserved.Add(p);
                            reserved.Add(p + dir * 8f);
                            reserved.Add(p + dir * 17f);
                            // People wait on the walking strip, facing the road (it lies to their left of travel = -right).
                            busStops.Add((Side(p + dir * 1.5f, half + 1.4f, lift + 0.1f), Mathf.Atan2(-right.x, -right.z) * Mathf.Rad2Deg));
                        }
                        while (Due("palm", 15f, 18f, out var p))
                            foreach (float side in new[] { 1f, -1f })
                            {
                                if (reserved.Any(q => FlatDistance(q, p) < 7f))
                                    continue;
                                float offset = side > 0f ? half + 3.4f : -(half + (oneway ? 1.3f : 3.4f));
                                var palm = Pick(cat.Palms, rng);
                                scatter.Add(palm, scatter.Fit(palm, Side(p, offset, 0.1f), R(rng, 0f, 360f), R(rng, 8f, 13f), 7f), Category.Tree);
                                var bush = Pick(cat.Bushes, rng);
                                if (bush != null && rng.NextDouble() < 0.7)
                                    scatter.Add(bush, scatter.Fit(bush, Side(p + dir * 8f, offset, 0.1f), R(rng, 0f, 360f), R(rng, 0.8f, 1.5f), 2.5f), Category.Prop);
                            }
                        while (Due("sign", 280f, 520f, out var p))
                        {
                            var sign = Pick(cat.SpeedSigns, rng);
                            if (sign != null)
                                scatter.Add(sign, scatter.Fit(sign, Side(p, half + 2.9f, lift + 0.1f), roadYaw + 180f, 2.8f, 1.5f), Category.Prop);
                        }
                        while (Due("dir", 700f, 1200f, out var p))
                        {
                            var sign = Pick(cat.DirectionSigns, rng);
                            if (sign != null)
                                scatter.Add(sign, scatter.Fit(sign, Side(p, half + 3.5f, 0.1f), roadYaw + 180f, 4f, 6f), Category.Prop);
                        }
                        while (Due("cam", 900f, 1800f, out var p))
                        {
                            // Kigali uses roadside speed cameras widely.
                            var camPrefab = Pick(cat.TrafficCameras, rng);
                            if (camPrefab != null)
                                scatter.Add(camPrefab, scatter.Fit(camPrefab, Side(p, half + 1.2f, kerbTop), roadYaw + 180f, 4.5f, 3f), Category.Prop);
                        }
                    }
                    else
                    {
                        while (Due("bush", 18f, 35f, out var p))
                        {
                            var bush = Pick(cat.Bushes, rng);
                            if (bush != null)
                                scatter.Add(bush, scatter.Fit(bush, Side(p, (rng.NextDouble() < 0.5 ? -1f : 1f) * (half + 1.6f), 0f), R(rng, 0f, 360f), R(rng, 0.7f, 1.4f), 2.2f), Category.Prop);
                        }
                        // Parked cars along the kerb of side streets.
                        while (Due("parked", 9f, 22f, out var p))
                        {
                            var car = Pick(cat.Vehicles, rng);
                            if (car == null || rng.NextDouble() > 0.55)
                                continue;
                            float side = rng.NextDouble() < 0.5 ? 1f : -1f;
                            var spot = p + right * side * (half + 1.1f);
                            _clearance?.Push(ref spot, right * side, 1.0f, 2f);
                            float carLength = car.name.Contains("Bus") ? 10f : 4.4f, carWidth = car.name.Contains("Bus") ? 2.5f : 1.9f;
                            float carYaw = roadYaw + (rng.NextDouble() < 0.5 ? 0f : 180f);
                            // Sit on all four corners: tilt with the ground, and skip spots too steep to park on
                            // (an untilted car on a slope had one side in the air).
                            if (!ParkedOnGround(terrain, spot, carYaw, carLength, carWidth, lift, out var pos, out var tilt))
                                continue;
                            var fit = scatter.FitLength(car, pos, carYaw, carLength);
                            scatter.Add(car, Matrix4x4.TRS(pos, tilt, Vector3.one) * Matrix4x4.TRS(-pos, Quaternion.identity, Vector3.one) * fit, Category.Tree);
                            grid.Mark(pos, 2f);
                        }
                    }
                    walked += len;
                }
            }
        }

        static RoadClearance _clearance;

        // Ground height at the four corners of a parked vehicle: its centre at their average, and the tilt that
        // matches front-back and side-to-side slope. False where the ground is too uneven to park.
        static bool ParkedOnGround(Terrain terrain, Vector3 centre, float yaw, float length, float width, float lift,
                                   out Vector3 pos, out Quaternion tilt)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 f = rot * Vector3.forward * (length * 0.45f), r = rot * Vector3.right * (width * 0.5f);
            float H(Vector3 q) => terrain.SampleHeight(q) + terrain.transform.position.y;
            float fl = H(centre + f - r), fr = H(centre + f + r), bl = H(centre - f - r), br = H(centre - f + r);
            float lo = Mathf.Min(Mathf.Min(fl, fr), Mathf.Min(bl, br)), hi = Mathf.Max(Mathf.Max(fl, fr), Mathf.Max(bl, br));
            pos = new Vector3(centre.x, (fl + fr + bl + br) * 0.25f + lift, centre.z);
            tilt = Quaternion.identity;
            if (hi - lo > Mathf.Max(0.6f, length * 0.12f))
                return false;
            float pitch = Mathf.Atan2((fl + fr - bl - br) * 0.5f, length * 0.9f) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2((fr + br - fl - bl) * 0.5f, width) * Mathf.Rad2Deg;
            // Pitch about the car's right axis (nose up = negative X rotation), roll about its forward axis.
            tilt = Quaternion.AngleAxis(-pitch, rot * Vector3.right) * Quaternion.AngleAxis(roll, rot * Vector3.forward);
            return true;
        }
        static readonly Dictionary<GameObject, (Vector2 pole, Vector2 arm)> PoleCache = new Dictionary<GameObject, (Vector2, Vector2)>();

        // Where a lamp model's pole meets the ground and which way its arm points (prefab space, xz), from its vertices:
        // the pole is the lowest part, the arm the highest.
        internal static (Vector2 pole, Vector2 arm) PoleGeometry(GameObject prefab)
        {
            if (PoleCache.TryGetValue(prefab, out var cached))
                return cached;
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            temp.transform.localScale = Vector3.one;
            var verts = new List<Vector3>();
            foreach (var mf in temp.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && mf.sharedMesh.isReadable)
                    foreach (var v in mf.sharedMesh.vertices)
                        verts.Add(mf.transform.TransformPoint(v));
            // Meshes without CPU access: fall back to the middle of the model (the old behaviour).
            var rs = temp.GetComponentsInChildren<Renderer>();
            var centre = rs.Length > 0 ? rs.Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; }).center : Vector3.zero;
            Object.DestroyImmediate(temp);
            var result = (new Vector2(centre.x, centre.z), Vector2.zero);
            if (verts.Count > 0)
            {
                float lo = verts.Min(v => v.y), hi = verts.Max(v => v.y), h = Mathf.Max(0.01f, hi - lo);
                var bottom = verts.Where(v => v.y < lo + h * 0.1f).ToList();
                var top = verts.Where(v => v.y > hi - h * 0.15f).ToList();
                var pole = new Vector2(bottom.Average(v => v.x), bottom.Average(v => v.z));
                var head = new Vector2(top.Average(v => v.x), top.Average(v => v.z));
                result = (pole, head - pole);
            }
            PoleCache[prefab] = result;
            return result;
        }

        // Uniform scale to a height (capped by footprint), the pole on basePos, the arm turned towards the road.
        static Matrix4x4 PoleFit(ScatterCollector scatter, GameObject prefab, Vector3 basePos, Vector3 towardsRoad, float height, float maxFootprint)
        {
            var b = scatter.PrefabBounds(prefab);
            float scale = b.size.y > 0.01f ? height / b.size.y : 1f;
            float footprint = Mathf.Max(b.size.x, b.size.z) * scale;
            if (footprint > maxFootprint)
                scale *= maxFootprint / footprint;
            var (pole, arm) = PoleGeometry(prefab);
            float want = Mathf.Atan2(towardsRoad.x, towardsRoad.z) * Mathf.Rad2Deg;
            // A post-top lamp (no real arm) keeps facing along the road's side.
            float yaw = arm.magnitude * scale > 0.4f ? want - Mathf.Atan2(arm.x, arm.y) * Mathf.Rad2Deg : want;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 p = basePos - rot * (new Vector3(pole.x, 0f, pole.y) * scale);
            p.y = basePos.y - b.min.y * scale;
            return Matrix4x4.TRS(p, rot, Vector3.one * scale);
        }

        // Lamp heads sit at the top of the pole, at both arm ends for wide (double-arm) lamps.
        // Each head is also a LampHead marker attached to its lamp, so the light follows the lamp when it is moved.
        static void AddLampHeads(GameObject prefab, Matrix4x4 m, ScatterCollector scatter, List<Vector3> lamps, int placed = -1)
        {
            var b = scatter.PrefabBounds(prefab);
            float top = b.max.y - 0.4f;
            void Head(Vector3 local)
            {
                var p = m.MultiplyPoint3x4(local);
                lamps.Add(p);
                if (placed < 0)
                    return;
                var marker = new GameObject("Lamp head", typeof(LampHead));
                marker.transform.position = p;
                scatter.Attach(placed, marker.transform);
            }
            if (b.size.x > 1.5f || b.size.z > 1.5f)
            {
                bool alongX = b.size.x >= b.size.z;
                Head(alongX ? new Vector3(b.min.x + 0.3f, top, b.center.z) : new Vector3(b.center.x, top, b.min.z + 0.3f));
                Head(alongX ? new Vector3(b.max.x - 0.3f, top, b.center.z) : new Vector3(b.center.x, top, b.max.z - 0.3f));
            }
            else
                Head(new Vector3(b.center.x, top, b.center.z));
        }

        // Parked motorcycles (moto-taxi stand) with the odd bicycle, nose to the kerb.
        static void MotoStand(Catalog cat, ScatterCollector scatter, System.Random rng, Terrain terrain, Vector3 start,
                              Vector3 dir, Vector3 right, float offset, float lift, float roadYaw)
        {
            if (cat.TwoWheelers.Length == 0)
                return;
            for (int k = 0, n = rng.Next(3, 7); k < n; k++)
            {
                var bike = cat.TwoWheelers[cat.TwoWheelers.Length == 1 || rng.NextDouble() < 0.85 ? 0 : 1];
                var p = Ground(terrain, start + dir * (k * 1.1f) + right * offset, lift);
                scatter.Add(bike, scatter.Fit(bike, p, roadYaw + 90f + R(rng, -12f, 12f), 1.15f, 2.3f), Category.Prop);
            }
        }

        // ---------- Traffic signals ----------

        // One signal per approach at the stop line on the driver's right, facing oncoming traffic. Each has a
        // three-lamp head (red over amber over green) in a dark housing, so it reads like a real signal from the
        // driver's seat and shows nothing from behind. Phase 0 is the junction's through road, phase 1 the cross road.
        static TrafficSignalSystem BuildSignals(List<PcRoadBuilder.Junction> junctions, Terrain terrain, Catalog cat,
                                                ScatterCollector scatter, System.Random rng, Transform root, string outDir)
        {
            var host = new GameObject("Traffic signals (live phases)");
            host.transform.SetParent(root, false);
            var system = host.AddComponent<TrafficSignalSystem>();
            var housingMat = EditorAssetUtil.LitMaterial(outDir, "PC_SignalHousing", new Color(0.05f, 0.05f, 0.05f), 0.3f);
            var lampMats = new[]
            {
                PcMaterials.Emissive(outDir, "PC_SignalRed", new Color(1f, 0.08f, 0.05f), new Color(1f, 0.06f, 0.04f) * 4f, 0.8f),
                PcMaterials.Emissive(outDir, "PC_SignalAmber", new Color(1f, 0.6f, 0.05f), new Color(1f, 0.6f, 0.05f) * 4f, 0.8f),
                PcMaterials.Emissive(outDir, "PC_SignalGreen", new Color(0.1f, 1f, 0.35f), new Color(0.1f, 1f, 0.35f) * 4f, 0.8f),
            };
            var offMat = EditorAssetUtil.LitMaterial(outDir, "PC_SignalOff", new Color(0.12f, 0.12f, 0.11f), 0.85f);
            var offsets = new List<float>();
            var lamps = new List<TrafficSignalSystem.Lamp>();
            foreach (var j in junctions)
            {
                if (!j.Signalised)
                    continue;
                j.SignalIndex = offsets.Count;
                offsets.Add(R(rng, 0f, 46f));
                for (int i = 0; i < j.Directions.Count && i < 4; i++)
                {
                    var d = j.Directions[i];
                    var approachRight = new Vector3(-d.z, 0f, d.x);   // right of a driver heading into the junction
                    var pos = Ground(terrain, j.Position + d * (PcRoadBuilder.StopLineAt + 0.6f) + approachRight * (j.Width * 0.5f + 0.9f), 0.42f);
                    float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + SignalYawOffset;
                    int pole = cat.SignalMain != null ? scatter.Add(cat.SignalMain, scatter.Fit(cat.SignalMain, pos, yaw, 4.6f, 3f), Category.Lamp) : -1;

                    // The head faces +d, towards the drivers approaching along -d.
                    var head = new GameObject($"Signal head J{j.SignalIndex}").transform;
                    head.SetParent(host.transform, false);
                    head.SetPositionAndRotation(pos + Vector3.up * 4.4f + d * 0.35f, Quaternion.LookRotation(-d));
                    var housing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.DestroyImmediate(housing.GetComponent<Collider>());
                    housing.name = "Housing";
                    housing.transform.SetParent(head, false);
                    housing.transform.localScale = new Vector3(0.36f, 1.0f, 0.22f);
                    var hr = housing.GetComponent<MeshRenderer>();
                    hr.sharedMaterial = housingMat;
                    hr.shadowCastingMode = ShadowCastingMode.Off;
                    var lenses = new Renderer[3];
                    for (int a = 0; a < 3; a++)
                    {
                        var lens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        Object.DestroyImmediate(lens.GetComponent<Collider>());
                        lens.name = a == 0 ? "Red" : a == 1 ? "Amber" : "Green";
                        lens.transform.SetParent(head, false);
                        // Local -z faces the drivers; the back half of each lens sits inside the housing.
                        lens.transform.localPosition = new Vector3(0f, 0.3f - a * 0.3f, -0.11f);
                        lens.transform.localScale = new Vector3(0.22f, 0.22f, 0.12f);
                        var lr = lens.GetComponent<MeshRenderer>();
                        lr.sharedMaterial = offMat;
                        lr.shadowCastingMode = ShadowCastingMode.Off;
                        lr.receiveShadows = false;
                        lenses[a] = lr;
                    }
                    lamps.Add(new TrafficSignalSystem.Lamp { head = head.gameObject, red = lenses[0], amber = lenses[1], green = lenses[2],
                                                             junction = j.SignalIndex, phase = Phase(j, d) });
                    // The head rides on its pole (moving the pole by hand moves the head); the system finds it at start.
                    var sh = head.gameObject.AddComponent<SignalHead>();
                    sh.red = lenses[0];
                    sh.amber = lenses[1];
                    sh.green = lenses[2];
                    sh.junction = j.SignalIndex;
                    sh.phase = Phase(j, d);
                    scatter.Attach(pole, head);
                }
            }
            system.Configure(offsets.ToArray(), lamps.ToArray(), lampMats, offMat);
            return system;
        }

        // Stop signs where a minor road meets a main road without signals. Each records its stop position so the
        // road-rule monitor can check that the driver came to a full stop, and the driving coach can warn ahead.
        static void PlaceStopSigns(List<PcRoadBuilder.Junction> junctions, Terrain terrain, Catalog cat, ScatterCollector scatter,
                                   System.Random rng, Transform root)
        {
            if (cat.StopSigns.Length == 0)
                return;
            var points = new List<Vector3>();
            var dirs = new List<Vector3>();
            var widths = new List<float>();
            foreach (var j in junctions)
            {
                if (j.Signalised || !NearHub(j.Position, DressRadius))
                    continue;
                for (int i = 0; i < j.Directions.Count; i++)
                {
                    if (j.DirectionIsMajor[i])
                        continue;
                    var d = j.Directions[i];
                    var approachRight = new Vector3(-d.z, 0f, d.x);
                    var stop = Pick(cat.StopSigns, rng);
                    var p = Ground(terrain, j.Position + d * 7f + approachRight * (j.Width * 0.5f + 0.8f));
                    scatter.Add(stop, scatter.Fit(stop, p, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 2.6f, 1.2f), Category.Prop);
                    points.Add(Ground(terrain, j.Position + d * 7f));
                    dirs.Add(-d);
                    widths.Add(j.Width);
                }
            }
            var host = new GameObject("Stop signs (rule data)");
            host.transform.SetParent(root, false);
            host.AddComponent<StopSigns>().Configure(points.ToArray(), dirs.ToArray(), widths.ToArray());
        }

        // ---------- Trees ----------

        static void ScatterTrees(Terrain terrain, Catalog cat, ScatterCollector scatter, System.Random rng, float size, PcCityLife.Occupancy grid)
        {
            int placed = 0;
            for (int attempt = 0; attempt < TreeTarget * 15 && placed < TreeTarget; attempt++)
            {
                var p = new Vector3(R(rng, 0f, size), 0f, R(rng, 0f, size));
                if (!PlayArea.Contains(p))
                    continue;
                if (grid.Blocked(p))
                    continue;
                grid.Mark(p, 2f);
                p = Ground(terrain, p);

                double roll = rng.NextDouble();
                GameObject prefab;
                float h, fp;
                Category category = Category.Tree;
                if (roll < 0.3) { prefab = Pick(cat.Bushes, rng); h = R(rng, 0.8f, 1.8f); fp = 3f; category = Category.Prop; }
                else if (roll < 0.5) { prefab = Pick(cat.Palms, rng); h = R(rng, 8f, 14f); fp = 7f; }
                else { prefab = Pick(cat.Trees, rng) ?? Pick(cat.Palms, rng); h = R(rng, 6f, 15f); fp = 10f; }
                if (prefab == null)
                    continue;
                scatter.Add(prefab, scatter.Fit(prefab, p, R(rng, 0f, 360f), h, fp), category);
                placed++;
            }
        }

        // ---------- Traffic ----------

        static RoadNetworkTraffic BuildTraffic(DistrictImporter.Road[] roads, Terrain terrain, Transform root, Catalog cat,
                                               System.Random rng, string outDir, List<PcRoadBuilder.Junction> junctions,
                                               TrafficSignalSystem signals, out int trackBus, out int trackCar)
        {
            trackBus = trackCar = -1;
            if (cat.Vehicles.Length == 0)
                return null;
            var signalAt = junctions.Where(j => j.Signalised).ToDictionary(j => Key(j.Position), j => j);

            var paths = new List<RoadNetworkTraffic.RoadPath>();
            var lengths = new List<float>();
            var widths = new List<float>();
            var majors = new List<bool>();
            var stops = new List<RoadNetworkTraffic.Stop>();
            foreach (var road in roads)
            {
                if (road.pts == null || road.pts.Length < 6 || !PcRoadBuilder.WithFootpaths.Contains(road.highway) ||
                    !DistrictImporter.RoadWidths.TryGetValue(road.highway, out float width))
                    continue;
                var line = PcRoadBuilder.ResampleLine(road.pts);
                if (!line.Any(p => NearHub(p, TrafficRadius)))
                    continue;
                float lift = PcRoadBuilder.SurfaceLift(road.highway) + 0.02f;
                var pts = line.Select(p => Ground(terrain, p, lift)).ToArray();
                float len = 0f;
                var cum = new float[pts.Length];
                for (int i = 1; i < pts.Length; i++)
                    cum[i] = len += Vector3.Distance(pts[i - 1], pts[i]);
                if (len < 60f)
                    continue;
                int pathIndex = paths.Count;
                for (int i = 0; i < line.Count; i++)
                    if (signalAt.TryGetValue(Key(line[i]), out var sj))
                    {
                        var d = line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)];
                        stops.Add(new RoadNetworkTraffic.Stop { path = pathIndex, s = cum[i], junction = sj.SignalIndex, phase = Phase(sj, d) });
                    }
                paths.Add(new RoadNetworkTraffic.RoadPath { points = pts, oneway = road.oneway == "yes", halfWidth = width * 0.5f });
                lengths.Add(len);
                widths.Add(width);
                majors.Add(PcRoadBuilder.Major.Contains(road.highway));
            }
            if (paths.Count == 0)
                return null;

            var go = new GameObject("Traffic (in lane, stops at red, queues)");
            go.transform.SetParent(root, false);
            var headMat = EditorAssetUtil.LitMaterial(outDir, "PC_Headlight", new Color(1f, 0.97f, 0.85f), 0.9f);
            var tailMat = EditorAssetUtil.LitMaterial(outDir, "PC_Taillight", new Color(0.7f, 0.05f, 0.03f), 0.8f);
            var lightStore = new PcCityLife.MeshStore($"{outDir}/VehicleLights.asset");
            var lightMeshes = new Dictionary<GameObject, Mesh>();

            var cars = new List<RoadNetworkTraffic.Car>();
            float spacing = Mathf.Max(45f, lengths.Sum() / MaxCars);
            for (int p = 0; p < paths.Count && cars.Count < MaxCars; p++)
            {
                int count = Mathf.Max(1, Mathf.FloorToInt(lengths[p] / spacing));
                float width = widths[p];
                bool oneway = paths[p].oneway;
                int lanesPerDir = PcRoadBuilder.Lanes(width, oneway);
                for (int c = 0; c < count && cars.Count < MaxCars; c++)
                {
                    // Every vehicle model appears before any repeats.
                    var prefab = cat.Vehicles[cars.Count % cat.Vehicles.Length];
                    var holder = new GameObject($"Vehicle {cars.Count} ({prefab.name})").transform;
                    holder.SetParent(go.transform, false);
                    var model = EditorAssetUtil.InstantiateUnpacked(prefab, holder);
                    EditorAssetUtil.StripRuntimeComponents(model);
                    string n = prefab.name;
                    bool heavy = n.Contains("Bus") || n.Contains("Truck") || n.Contains("Container");
                    float length = n.Contains("Bus") ? 11f : n.Contains("Container") || n.Contains("Truck_") ? 9f :
                                   n.Contains("Van") || n.Contains("Ambulance") ? 5.2f : 4.5f;
                    AssetCatalog.FitVehicle(model, length);
                    var wheels = WheelPivots(model.transform, holder, out float wheelRadius);

                    bool ownLights = model.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials)
                                          .Any(m => m != null && m.name.Contains("lights"));
                    if (!ownLights)
                    {
                        if (!lightMeshes.TryGetValue(prefab, out var mesh))
                            lightMeshes[prefab] = mesh = lightStore.Add(VehicleLightMesh(EditorAssetUtil.RendererBounds(model)), $"Lights_{n}");
                        var lightsGo = new GameObject("Lights");
                        lightsGo.transform.SetParent(holder, false);
                        lightsGo.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var mr = lightsGo.AddComponent<MeshRenderer>();
                        mr.sharedMaterials = new[] { headMat, tailMat };
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                    }

                    // Heavy vehicles keep to the kerb-side lane.
                    int lane = heavy ? lanesPerDir - 1 : rng.Next(lanesPerDir);
                    float offset = oneway
                        ? (lane - (lanesPerDir - 1) * 0.5f) * (width - 1f) / lanesPerDir
                        : width * 0.5f * (lane + 0.5f) / lanesPerDir;
                    holder.position = paths[p].points[0];
                    // Story Mode follows a bus and a car that start on main roads near the hubs.
                    if (n.Contains("Bus") && trackBus < 0 && majors[p] && NearHub(paths[p].points[0], 1200f))
                        trackBus = cars.Count;
                    if (!heavy && trackCar < 0 && majors[p] && NearHub(paths[p].points[0], 900f))
                        trackCar = cars.Count;
                    cars.Add(new RoadNetworkTraffic.Car
                    {
                        body = holder, path = p,
                        distance = lengths[p] * (c + (float)rng.NextDouble() * 0.6f) / count,
                        reverse = !oneway && rng.NextDouble() < 0.5,
                        speed = heavy ? R(rng, 7f, 10f) : R(rng, 9f, 15f),
                        offset = offset,
                        model = model.transform,
                        wheels = wheels,
                        wheelRadius = wheelRadius,
                    });
                }
            }
            var traffic = go.AddComponent<RoadNetworkTraffic>();
            traffic.Configure(paths.ToArray(), cars.ToArray(), false, true, stops.ToArray(), signals);
            traffic.ConfigureAvoidance(7.5f, 16f, 1.9f);
            if (trackBus < 0) trackBus = cars.FindIndex(c => c.body.name.Contains("Bus"));
            return traffic;
        }

        // Puts each wheel under a pivot at its own centre, aligned with the vehicle, so it can spin about the
        // vehicle's right axis whatever the model's pivot is. Only the top-most "wheel" transforms are used.
        internal static Transform[] WheelPivots(Transform model, Transform holder, out float radius)
        {
            radius = 0.35f;
            var found = model.GetComponentsInChildren<Transform>()
                             .Where(t => t != model && t.name.IndexOf("wheel", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                                         t.GetComponentInChildren<Renderer>() != null)
                             .ToList();
            found = found.Where(t => !found.Any(o => o != t && t.IsChildOf(o))).ToList();
            var pivots = new List<Transform>();
            foreach (var wheel in found)
            {
                var b = EditorAssetUtil.RendererBounds(wheel.gameObject);
                var pivot = new GameObject($"{wheel.name} (spin)").transform;
                pivot.SetParent(wheel.parent, false);
                pivot.SetPositionAndRotation(b.center, holder.rotation);
                wheel.SetParent(pivot, true);
                pivots.Add(pivot);
                radius = Mathf.Max(0.15f, b.extents.y);
            }
            return pivots.ToArray();
        }

        // Two headlights (submesh 0) and two tail lights (submesh 1) at the corners of the vehicle bounds.
        static Mesh VehicleLightMesh(Bounds b)
        {
            var builder = new BoxMeshBuilder(2);
            float y = b.min.y + b.size.y * 0.42f, x = Mathf.Max(0.3f, b.extents.x - 0.35f);
            var size = new Vector3(0.32f, 0.14f, 0.06f);
            foreach (float side in new[] { -1f, 1f })
            {
                builder.Box(0, new Vector3(b.center.x + side * x, y, b.max.z + 0.02f), size);
                builder.Box(1, new Vector3(b.center.x + side * x, y, b.min.z - 0.02f), size);
            }
            return builder.ToMesh("VehicleLights");
        }

        // ---------- Pedestrians ----------

        static GameObject BuildPedestrians(DistrictImporter.Road[] roads, Terrain terrain, Transform root, Catalog cat, System.Random rng)
        {
            if (cat.Pedestrians.Length == 0)
                return null;
            var paths = new List<RoadNetworkTraffic.RoadPath>();
            var lengths = new List<float>();
            var widths = new List<float>();
            foreach (var road in roads)
            {
                if (road.pts == null || road.pts.Length < 6 || !PcRoadBuilder.WithFootpaths.Contains(road.highway) ||
                    !DistrictImporter.RoadWidths.TryGetValue(road.highway, out float width))
                    continue;
                var line = PcRoadBuilder.ResampleLine(road.pts);
                if (!line.Any(p => NearHub(p, DressRadius)))
                    continue;
                float lift = PcRoadBuilder.SurfaceLift(road.highway) + 0.1f;
                var pts = line.Select(p => Ground(terrain, p, lift)).ToArray();
                float len = 0f;
                for (int i = 1; i < pts.Length; i++)
                    len += Vector3.Distance(pts[i - 1], pts[i]);
                if (len < 40f)
                    continue;
                paths.Add(new RoadNetworkTraffic.RoadPath { points = pts, oneway = false });
                lengths.Add(len);
                widths.Add(width);
            }
            if (paths.Count == 0)
                return null;

            var go = new GameObject("Pedestrians (walking the footpaths)");
            go.transform.SetParent(root, false);
            var people = new List<RoadNetworkTraffic.Car>();
            float spacing = Mathf.Max(50f, lengths.Sum() / MaxWalkers);
            for (int p = 0; p < paths.Count && people.Count < MaxWalkers; p++)
            {
                int count = Mathf.Max(1, Mathf.FloorToInt(lengths[p] / spacing));
                for (int c = 0; c < count && people.Count < MaxWalkers; c++)
                {
                    var model = cat.Pedestrians[people.Count % cat.Pedestrians.Length];
                    var person = PcPeople.Spawn(model, go.transform, paths[p].points[0], 0f, Activity.Walk, rng, $"Walker {people.Count} ({model.name})");
                    people.Add(new RoadNetworkTraffic.Car
                    {
                        body = person.transform, path = p,
                        distance = lengths[p] * (c + (float)rng.NextDouble() * 0.8f) / count,
                        reverse = rng.NextDouble() < 0.5,
                        speed = R(rng, 1.1f, 1.6f),
                        // The walking strip of the footpath (furniture sits further out).
                        offset = widths[p] * 0.5f + R(rng, 0.95f, 1.85f),
                        animator = person.GetComponentInChildren<Animator>(),
                    });
                }
            }
            var walkers = go.AddComponent<RoadNetworkTraffic>();
            walkers.Configure(paths.ToArray(), people.ToArray(), keepUpright: true, queue: false);
            walkers.ConfigureAvoidance(1.3f, 3.5f, 0.7f);
            return go;
        }

        // ---------- Atmosphere ----------

        // AllSky panoramas through the day (Equirect versions expose _Exposure for fading). Midday keeps the
        // procedural sky so the sun disc matches the light direction; Kigali afternoons are often overcast.
        static DayNightCycle.SkyKey[] SkyTimeline()
        {
            DayNightCycle.SkyKey Key(float from, string material, float exposure)
            {
                var sky = material == null ? null : AssetDatabase.LoadAssetAtPath<Material>($"{AllSky}/{material}");
                if (material != null && sky == null)
                    Debug.LogWarning($"[PcShowcase] AllSky material missing: {material}");
                return new DayNightCycle.SkyKey { fromHour = from, sky = sky, exposure = exposure };
            }
            return new[]
            {
                Key(5.3f, "Epic_GloriousPink/Epic_GloriousPink Equirect.mat", 1.0f),
                Key(7.0f, null, 1f),
                Key(14.5f, "Overcast Low/AllSky_Overcast4_Low Equirect.mat", 1.1f),
                Key(17.2f, "Cold Sunset/Cold Sunset Equirect.mat", 1.0f),
                Key(18.4f, "Epic_BlueSunset/Epic_BlueSunset Equirect.mat", 0.9f),
                Key(19.0f, "Deep Dusk/Deep Dusk Equirect.mat", 0.8f),
                Key(19.8f, "Cold Night/Cold Night Equirect.mat", 0.7f),
                Key(1.0f, "Night MoonBurst/AllSky_Night_MoonBurst Equirect.mat", 0.7f),
            };
        }

        static void SetupAtmosphere(Terrain terrain, Vector3 centre, string outDir, string district, out Light sun, out Camera cam, out Volume volume)
        {
            sun = new GameObject("Sun / Moon").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            sun.color = new Color(1f, 0.94f, 0.84f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;

            var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/World/Kigali/M_Sky_KigaliMorning.mat");
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                sky.SetFloat("_AtmosphereThickness", 0.85f);
                sky.SetFloat("_Exposure", 1.25f);
                AssetDatabase.CreateAsset(sky, $"{outDir}/M_Sky.mat");
            }
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.60f, 0.70f, 0.86f);
            RenderSettings.ambientEquatorColor = new Color(0.56f, 0.58f, 0.53f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.25f, 0.19f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;   // clear close by, thick at the clip distance
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.fogColor = new Color(0.72f, 0.79f, 0.86f);

            string profilePath = $"{outDir}/PP_{district}.asset";
            AssetDatabase.DeleteAsset(profilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
            profile.Add<Tonemapping>(true).mode.value = TonemappingMode.ACES;
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.value = 0.95f;
            bloom.intensity.value = 0.55f;
            bloom.scatter.value = 0.72f;
            var grade = profile.Add<ColorAdjustments>(true);
            grade.postExposure.value = 0.2f;
            grade.contrast.value = 16f;
            grade.saturation.value = 12f;
            profile.Add<WhiteBalance>(true).temperature.value = 6f;
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.value = 0.24f;
            vignette.smoothness.value = 0.45f;
            var grain = profile.Add<FilmGrain>(true);
            grain.intensity.value = 0.12f;
            var dof = profile.Add<DepthOfField>(true);
            dof.mode.value = DepthOfFieldMode.Bokeh;
            dof.focusDistance.value = 40f;
            dof.aperture.value = 4f;
            dof.focalLength.value = 60f;
            dof.active = false;
            foreach (var component in profile.components)
                AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
            volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            // Quest: no post-processing pass (colour comes from lighting and fog); the headset applies MSAA instead.
            cam.farClipPlane = ViewDistance;
            cam.clearFlags = CameraClearFlags.Skybox;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            camGo.AddComponent<AudioListener>();
            var target = Ground(terrain, centre);
            camGo.transform.position = target + new Vector3(-450f, 320f, -650f) * (terrain.terrainData.size.x / 6000f);
            camGo.transform.LookAt(target);
        }
    }
}
