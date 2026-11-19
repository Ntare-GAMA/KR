using System.Collections.Generic;
using System.Linq;
using KR.Editor.Util;
using KR.Gameplay.Player;
using KR.Gameplay.Story;
using KR.Gameplay.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Activity = KR.Editor.Kigali.PcPeople.Activity;

namespace KR.Editor.Kigali
{
    // The dining room every cafe and restaurant opens into (Restaurant): a warm, modest room with wooden floor and
    // plastered walls, six tables with chairs (Furniture Mega Pack), a counter (its kitchen cabinets), a board with
    // the restaurant's name and menu, the waitress, the dishes she serves (RPG Food & Drinks Pack, Food Pack Mixed,
    // plus the generated cup, plate and bottle) and soft background music (Casual & Relaxing Game Music, "Happy").
    // The room is built once, below the city where nothing else can be seen (y = -60).
    internal static class RestaurantInterior
    {
        const string Out = "Assets/World/Story/Restaurant";
        static readonly Vector3 Origin = new Vector3(3500f, -60f, 3500f);
        const float W = 14f, D = 10f, H = 3.4f;

        public static Restaurant Build(Transform parent, PlayerRig rig, ConversationRunner runner, StoryDirector director,
                                       GameObject[] women, System.Random rng, (GameObject cup, GameObject plate, GameObject bottle) ware)
        {
            EditorAssetUtil.EnsureFolder(Out);
            var room = new GameObject("Restaurant dining room (every cafe and restaurant opens here)").transform;
            room.SetParent(parent, false);
            room.position = Origin;

            var floor = EditorAssetUtil.LitMaterial(Out, "Floor_Wood", new Color(0.42f, 0.27f, 0.16f), 0.35f);
            var wall = EditorAssetUtil.LitMaterial(Out, "Wall_Plaster", new Color(0.94f, 0.89f, 0.79f), 0.1f);
            var band = EditorAssetUtil.LitMaterial(Out, "Wall_Band", new Color(0.36f, 0.22f, 0.14f), 0.3f);
            var ceiling = EditorAssetUtil.LitMaterial(Out, "Ceiling", new Color(0.97f, 0.96f, 0.93f), 0.05f);
            var window = EditorAssetUtil.LitMaterial(Out, "Window_Daylight", new Color(0.86f, 0.93f, 1f), 0.9f);
            window.EnableKeyword("_EMISSION");
            window.SetColor("_EmissionColor", new Color(0.75f, 0.85f, 1f) * 1.4f);
            var door = EditorAssetUtil.LitMaterial(Out, "Door_Wood", new Color(0.28f, 0.17f, 0.1f), 0.4f);
            var counterTop = EditorAssetUtil.LitMaterial(Out, "Counter_Top", new Color(0.2f, 0.16f, 0.13f), 0.6f);

            // Shell: floor, ceiling, four walls with a dark band at the bottom, daylight windows on the sides.
            Box(room, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(W, 0.2f, D), floor, true);
            Box(room, "Ceiling", new Vector3(0f, H + 0.1f, 0f), new Vector3(W, 0.2f, D), ceiling, true);
            foreach (var (name, pos, size) in new[]
            {
                ("Back wall", new Vector3(0f, H / 2f, D / 2f + 0.1f), new Vector3(W, H, 0.2f)),
                ("Left wall", new Vector3(-W / 2f - 0.1f, H / 2f, 0f), new Vector3(0.2f, H, D)),
                ("Right wall", new Vector3(W / 2f + 0.1f, H / 2f, 0f), new Vector3(0.2f, H, D)),
                ("Front wall left", new Vector3(-W / 4f - 0.6f, H / 2f, -D / 2f - 0.1f), new Vector3(W / 2f - 1.2f, H, 0.2f)),
                ("Front wall right", new Vector3(W / 4f + 0.6f, H / 2f, -D / 2f - 0.1f), new Vector3(W / 2f - 1.2f, H, 0.2f)),
                ("Front wall over door", new Vector3(0f, H - 0.6f, -D / 2f - 0.1f), new Vector3(2.4f, 1.2f, 0.2f)),
            })
            {
                Box(room, name, pos, size, wall, true);
                var bandSize = new Vector3(size.x > 0.3f ? size.x : 0.22f, 1f, size.z > 0.3f ? size.z : 0.22f);
                if (!name.Contains("over door"))
                    Box(room, name + " band", new Vector3(pos.x, 0.5f, pos.z), bandSize, band, false);
            }
            foreach (float side in new[] { -1f, 1f })
                for (int k = -1; k <= 1; k++)
                    Box(room, "Window", new Vector3(side * (W / 2f - 0.005f), 1.7f, k * 3f), new Vector3(0.02f, 1.1f, 1.5f), window, false);
            var exit = Box(room, "Door (way out)", new Vector3(0f, 1.1f, -D / 2f - 0.05f), new Vector3(1.9f, 2.2f, 0.1f), door, false).transform;
            exit.localRotation = Quaternion.identity;

            // Warm lights over the tables and the counter.
            foreach (var p in new[] { new Vector3(-3.5f, H - 0.3f, -1.5f), new Vector3(3.5f, H - 0.3f, -1.5f), new Vector3(-3.5f, H - 0.3f, 2f),
                                      new Vector3(3.5f, H - 0.3f, 2f), new Vector3(0f, H - 0.3f, 4f) })
            {
                var l = new GameObject("Ceiling light").AddComponent<Light>();
                l.transform.SetParent(room, false);
                l.transform.localPosition = p;
                l.type = LightType.Point;
                l.range = 7f;
                l.intensity = 2.2f;
                l.color = new Color(1f, 0.86f, 0.68f);
                l.shadows = LightShadows.None;
            }

            // Counter across the back (kitchen cabinets with a dark top) and the board above it.
            var cabinets = Prefabs("Assets/Furniture Mega Pack/Prefabs/Kitchen", n => n.StartsWith("CabinetA0")).ToArray();
            for (int i = 0; i < 6; i++)
            {
                var pos = new Vector3(-2.75f + i * 1.1f, 0f, D / 2f - 0.55f);
                if (cabinets.Length > 0)
                    Place(room, cabinets[i % cabinets.Length], pos, 180f, 0.9f, 1.15f);
                else
                    Box(room, "Counter", pos + Vector3.up * 0.45f, new Vector3(1.1f, 0.9f, 0.6f), band, true);
            }
            Box(room, "Counter top", new Vector3(0f, 0.93f, D / 2f - 0.55f), new Vector3(6.8f, 0.05f, 0.7f), counterTop, true);
            var board = Board(room, new Vector3(0f, 2.4f, D / 2f - 0.02f), out var title);

            // Six tables in two rows either side of a centre aisle; the chair on the aisle side is yours.
            // Tables: the sourced wooden tables (Poly Haven) when present, else dining tables from the Mega Pack.
            // (painted_wooden_table is 1.14 m high, a serving table, so it stands by the right wall instead.)
            var tables = new[] { SourcedAssets.Furniture("wooden_table_02") }.Where(p => p != null).ToArray();
            var serving = SourcedAssets.Furniture("painted_wooden_table");
            if (serving != null)
                Place(room, serving, new Vector3(W / 2f - 0.65f, 0f, 0f), 90f, Size(serving).size.y, 3f);
            if (tables.Length == 0)
                tables = Prefabs("Assets/Furniture Mega Pack/Prefabs/Tables", _ => true)
                    .Where(p => Fits(p, 0.65f, 0.85f, 0.7f, 1.8f)).ToArray();
            var chairs = Prefabs("Assets/Furniture Mega Pack/Prefabs/Chairs", _ => true)
                .Where(p => Fits(p, 0.75f, 1.15f, 0.35f, 0.75f)).ToArray();
            var seats = new List<DiningSeat>();
            foreach (float side in new[] { -1f, 1f })
                foreach (float z in new[] { -2.6f, 0f, 2.4f })
                {
                    var t = new Vector3(side * 3.6f, 0f, z);
                    var table = tables.Length > 0 ? Place(room, tables[rng.Next(tables.Length)], t, 0f, 0.76f, 1.2f)
                                                  : Box(room, "Table", t + Vector3.up * 0.38f, new Vector3(1f, 0.76f, 0.8f), band, true);
                    float top = EditorAssetUtil.RendererBounds(table).max.y - Origin.y;
                    var towardsAisle = new Vector3(-side, 0f, 0f);
                    var chairAt = t + towardsAisle * 0.85f;
                    var oppositeAt = t - towardsAisle * 0.85f;
                    if (chairs.Length > 0)
                    {
                        var chair = chairs[rng.Next(chairs.Length)];
                        Place(room, chair, chairAt, Yaw(-towardsAisle), 0.95f, 0.65f);
                        Place(room, chair, oppositeAt, Yaw(towardsAisle), 0.95f, 0.65f);
                    }
                    seats.Add(new DiningSeat
                    {
                        seat = Point(room, "Your seat", chairAt, Yaw(-towardsAisle)),
                        plate = Point(room, "Plate", new Vector3(t.x + towardsAisle.x * 0.25f, top + 0.005f, t.z - 0.12f), 0f),
                        drink = Point(room, "Drink", new Vector3(t.x + towardsAisle.x * 0.25f, top + 0.005f, t.z + 0.28f), 0f),
                        waiter = Point(room, "Waitress stands here", t + towardsAisle * 1.6f + Vector3.forward * 0.7f, Yaw(-towardsAisle)),
                    });
                }

            // A lounge corner by the left windows: two armchairs and a stool between them as a side table.
            var arm = SourcedAssets.Furniture("modern_arm_chair_01");
            var lounge = SourcedAssets.Furniture("mid_century_lounge_chair");
            var stool = SourcedAssets.Furniture("folding_wooden_stool");
            // At their modelled size (real-world metres).
            if (arm != null)
                Place(room, arm, new Vector3(-W / 2f + 0.9f, 0f, -1.2f), 90f, Size(arm).size.y, 3f);
            if (lounge != null)
                Place(room, lounge, new Vector3(-W / 2f + 0.95f, 0f, 1.3f), 90f, Size(lounge).size.y, 3f);
            if (stool != null)
                Place(room, stool, new Vector3(-W / 2f + 0.8f, 0f, 0f), 0f, Size(stool).size.y, 3f);

            // Plants in the corners.
            var plant = AssetDatabase.FindAssets("Flower Pot t:Model").Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(g => g != null);
            if (plant != null)
                foreach (var p in new[] { new Vector3(-W / 2f + 0.5f, 0f, -D / 2f + 0.5f), new Vector3(W / 2f - 0.5f, 0f, -D / 2f + 0.5f),
                                          new Vector3(-W / 2f + 0.5f, 0f, D / 2f - 0.5f), new Vector3(W / 2f - 0.5f, 0f, D / 2f - 0.5f) })
                    Place(room, plant, p, 0f, 1.1f, 0.8f);

            // The dishes she serves, kept hidden in the room until ordered.
            var dishes = new GameObject("Dishes").transform;
            dishes.SetParent(room, false);
            var china = EditorAssetUtil.LitMaterial("Assets/World/Story/Tableware", "Ware_China", new Color(0.96f, 0.96f, 0.94f), 0.75f);
            GameObject Dish(string name, string foodPath, float size)
            {
                var root = new GameObject(name);
                root.transform.SetParent(dishes, false);
                var plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(plate.GetComponent<Collider>());
                plate.name = "Plate";
                plate.transform.SetParent(root.transform, false);
                plate.transform.localScale = new Vector3(0.27f, 0.008f, 0.27f);
                plate.transform.localPosition = new Vector3(0f, 0.008f, 0f);
                plate.GetComponent<Renderer>().sharedMaterial = china;
                var food = AssetDatabase.LoadAssetAtPath<GameObject>(foodPath);
                if (food != null)
                    Fit(food, root.transform, new Vector3(0f, 0.016f, 0f), size);
                return root;
            }
            GameObject Item(string name, GameObject prefab, float size)
            {
                var root = new GameObject(name);
                root.transform.SetParent(dishes, false);
                if (prefab != null)
                    Fit(prefab, root.transform, Vector3.zero, size, height: true);
                return root;
            }
            const string Rpg = "Assets/RPG Food & Drinks Pack/Assets/Prefabs";
            var dishKeys = new[] { "brochette", "fish", "stew", "plate" };
            var dishItems = new[]
            {
                Dish("Goat brochettes with chips", $"{Rpg}/Ribs.prefab", 0.2f),
                Dish("Grilled tilapia", $"{Rpg}/Fish.prefab", 0.22f),
                Dish("Beef stew with rice", $"{Rpg}/Stew.prefab", 0.18f),
                Item("Buffet plate", ware.plate, 0.26f),
            };
            var drinkKeys = new[] { "tea", "juice", "water" };
            var drinkItems = new[]
            {
                Item("African tea", ware.cup, 0.1f),
                Item("Passion juice", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Food Pack Mixed/Prefabs/JuicePack.prefab"), 0.16f),
                Item("Water", ware.bottle, 0.24f),
            };

            // The waitress, at the counter.
            var home = Point(room, "Waitress home (counter)", new Vector3(1.2f, 0f, D / 2f - 1.4f), 180f);
            var model = women.Length > 0 ? women[rng.Next(women.Length)] : null;
            Npc waitress = null;
            if (model != null)
            {
                var w = PcPeople.Spawn(model, room, home.position, 180f, Activity.Serve, rng, "Waitress", brain: true);
                waitress = w.AddComponent<Npc>();
                waitress.Configure("waitress", "Waitress", null, w.GetComponent<PersonAnimator>(), null, 0.05f);
            }

            // Music while you are inside.
            var music = room.gameObject.AddComponent<AudioSource>();
            music.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Casual & Relaxing Game Music/Happy.wav");
            music.loop = true;
            music.volume = 0.18f;
            music.spatialBlend = 0f;
            music.playOnAwake = false;

            var entry = Point(room, "Entry (inside the door)", new Vector3(0f, 0.05f, -D / 2f + 1.2f), 0f);
            var exitPoint = Point(room, "Exit", new Vector3(0f, 0f, -D / 2f + 0.4f), 180f);
            var aisle = Point(room, "Aisle", Vector3.zero, 0f);
            var restaurant = room.gameObject.AddComponent<Restaurant>();
            restaurant.Configure(rig, runner, director, waitress, entry, exitPoint, home, aisle, title, seats.ToArray(),
                                 dishKeys, dishItems, drinkKeys, drinkItems, music);
            return restaurant;
        }

        static IEnumerable<GameObject> Prefabs(string folder, System.Func<string, bool> match) =>
            AssetDatabase.IsValidFolder(folder)
                ? AssetDatabase.FindAssets("t:Prefab", new[] { folder }).Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                               .Where(p => p != null && match(p.name)).OrderBy(p => p.name)
                : Enumerable.Empty<GameObject>();

        static readonly Dictionary<GameObject, Bounds> Sizes = new Dictionary<GameObject, Bounds>();

        static Bounds Size(GameObject prefab)
        {
            if (Sizes.TryGetValue(prefab, out var b))
                return b;
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            b = EditorAssetUtil.RendererBounds(temp);
            Object.DestroyImmediate(temp);
            return Sizes[prefab] = b;
        }

        // A dining-size table or chair (height and footprint in metres, as modelled).
        static bool Fits(GameObject prefab, float hMin, float hMax, float fMin, float fMax)
        {
            var b = Size(prefab);
            float f = Mathf.Max(b.size.x, b.size.z);
            return b.size.y >= hMin && b.size.y <= hMax && f >= fMin && f <= fMax;
        }

        // A prefab standing on the floor at a room position, scaled to a height (capped by footprint).
        static GameObject Place(Transform room, GameObject prefab, Vector3 local, float yaw, float height, float maxFootprint)
        {
            var b = Size(prefab);
            float s = b.size.y > 0.01f ? height / b.size.y : 1f;
            float f = Mathf.Max(b.size.x, b.size.z) * s;
            if (f > maxFootprint)
                s *= maxFootprint / f;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(room, false);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localRotation = rot;
            go.transform.localScale = Vector3.one * s;
            go.transform.localPosition = local - rot * new Vector3(b.center.x, 0f, b.center.z) * s - Vector3.up * b.min.y * s;
            EditorAssetUtil.StripRuntimeComponents(go);
            var box = go.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = b.size;
            return go;
        }

        // A model under parent at offset, scaled so its larger side (or its height) is `size`, standing on the offset.
        static void Fit(GameObject prefab, Transform parent, Vector3 offset, float size, bool height = false)
        {
            var b = Size(prefab);
            float extent = height ? b.size.y : Mathf.Max(b.size.x, b.size.z);
            float s = extent > 0.0001f ? size / extent : 1f;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * s;
            go.transform.localPosition = offset - new Vector3(b.center.x, b.min.y, b.center.z) * s;
            EditorAssetUtil.StripRuntimeComponents(go);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static GameObject Box(Transform room, string name, Vector3 local, Vector3 size, Material mat, bool collide)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(room, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collide)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static Transform Point(Transform room, string name, Vector3 local, float yaw)
        {
            var t = new GameObject(name).transform;
            t.SetParent(room, false);
            t.localPosition = local;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return t;
        }

        static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        // The name board over the counter, with the menu underneath; the name is filled in when you come in.
        static GameObject Board(Transform room, Vector3 local, out Text title)
        {
            var go = new GameObject("Name and menu board", typeof(RectTransform));
            go.transform.SetParent(room, false);
            go.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(420f, 140f);
            go.transform.localScale = Vector3.one * 0.01f;
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.identity;   // faces into the room (-z)
            var bg = new GameObject("Back", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
            bg.transform.SetParent(go.transform, false);
            bg.color = new Color(0.12f, 0.09f, 0.07f);
            Stretch(bg.rectTransform, 0f, 0f, 1f, 1f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            title = Label(go.transform, font, "Restaurant", 36, FontStyle.Bold, new Color(0.98f, 0.86f, 0.55f), 0.55f, 1f);
            Label(go.transform, font, "Brochettes · Grilled tilapia · Beef stew · Buffet\nAfrican tea · Passion juice · Water", 17, FontStyle.Normal,
                  new Color(0.95f, 0.93f, 0.88f), 0f, 0.55f);
            return go;
        }

        static Text Label(Transform parent, Font font, string text, int size, FontStyle style, Color color, float y0, float y1)
        {
            var t = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Text>();
            t.transform.SetParent(parent, false);
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 10;
            t.resizeTextMaxSize = size;
            Stretch(t.rectTransform, 0f, y0, 1f, y1);
            return t;
        }

        static void Stretch(RectTransform r, float x0, float y0, float x1, float y1)
        {
            r.anchorMin = new Vector2(x0, y0);
            r.anchorMax = new Vector2(x1, y1);
            r.offsetMin = new Vector2(10f, 6f);
            r.offsetMax = new Vector2(-10f, -6f);
        }
    }
}
