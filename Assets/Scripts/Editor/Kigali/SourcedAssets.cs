using System.Collections.Generic;
using System.IO;
using System.Linq;
using KR.Editor.Util;
using UnityEditor;
using UnityEngine;

namespace KR.Editor.Kigali
{
    // The files in Assets/Sourced Assets: furniture (Poly Haven style FBX + PBR texture folders) and sounds.
    //   - Import settings sized for the Quest: textures at most 2048 (1024 on Android, ASTC), normal maps marked,
    //     roughness/metal maps linear; sounds mono and compressed (long loops stream).
    //   - Furniture(name): a prefab per model with URP Lit materials built from its textures
    //     (Furniture/Prefabs/<name>.prefab), made on first use.
    //   - Sound(key): the clip for a role (engine, horn, door, footsteps...), or null if that file is missing.
    internal sealed class SourcedAssets : AssetPostprocessor
    {
        public const string Root = "Assets/Sourced Assets";
        const string FurnitureDir = Root + "/Furniture", PrefabDir = FurnitureDir + "/Prefabs", MatDir = FurnitureDir + "/Materials";

        // ---------- import settings ----------

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(FurnitureDir))
                return;
            var ti = (TextureImporter)assetImporter;
            string n = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            if (n.Contains("_nor_"))
                ti.textureType = TextureImporterType.NormalMap;
            else if (n.Contains("_rough") || n.Contains("_metal"))
                ti.sRGBTexture = false;
            ti.maxTextureSize = 2048;
            ti.mipmapEnabled = true;
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 1024;
            android.format = TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(android);
        }

        void OnPreprocessAudio()
        {
            // The car radio: 52 minutes, so it streams from disk (decompressed it would take ~550 MB of memory).
            if (assetPath.StartsWith("Assets/Resources/Audio/Radio/"))
            {
                var ri = (AudioImporter)assetImporter;
                ri.forceToMono = false;
                ri.loadInBackground = true;
                var rs = ri.defaultSampleSettings;
                rs.loadType = AudioClipLoadType.Streaming;
                rs.compressionFormat = AudioCompressionFormat.Vorbis;
                rs.quality = 0.45f;
                rs.preloadAudioData = false;
                ri.defaultSampleSettings = rs;
                return;
            }
            if (!assetPath.StartsWith(Root) && !assetPath.StartsWith("Assets/Resources/Story/VO"))
                return;
            var ai = (AudioImporter)assetImporter;
            ai.forceToMono = true;
            var s = ai.defaultSampleSettings;
            bool longClip = assetPath.Contains("cars-on-the-road") || assetPath.Contains("nature-ambience") || assetPath.Contains("hum-of-a-city")
                            || assetPath.Contains("murmur") || assetPath.Contains("cafe-ambience") || assetPath.Contains("terrace-wind") || assetPath.Contains("gravel-on-asphalt");
            s.loadType = longClip ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            ai.defaultSampleSettings = s;
        }

        // ---------- sounds ----------

        static readonly Dictionary<string, string> Sounds = new Dictionary<string, string>
        {
            ["engine_start"] = "Derived/engine_start.wav",
            ["engine_idle"] = "Derived/engine_idle_loop.wav",
            ["engine_rev"] = "Derived/engine_rev_loop.wav",
            ["engine_run"] = "dragon-studio-car-engine-372477.mp3",
            ["throttle"] = "tanweraman-car-throttle-static-337873.mp3",
            ["horn"] = "freesound_community-car-horn-6408.mp3",
            ["door_open"] = "dragon-studio-open-door-stock-sfx-454246.mp3",
            ["door_close"] = "dragon-studio-close-door-382723.mp3",
            ["door_slam"] = "dragon-studio-door-slam-478362.mp3",
            ["crash"] = "freesound_community-car-accident-with-squeal-and-crash-6054.mp3",
            ["skid"] = "freesound_community-screech-103213.mp3",
            ["footsteps"] = "freesound_community-concrete-footsteps-1-6265.mp3",
            ["sliding_door"] = "Derived/sliding_door.wav",
            ["bus_idle"] = "lesiakower-diesel-engine-idle-bus-engine-idling-408780.mp3",
            ["moto"] = "tanweraman-motorcycle-engine-rev-2-337870.mp3",
            ["traffic"] = "audiopapkin-cars-on-the-road-297888.mp3",
            ["traffic_2"] = "audiopapkin-cars-on-the-road-2-302877.mp3",
            ["indicator"] = "freesound_community-car-left-right-indicator-tick-80303.mp3",
        };

        // The SoundBank: each name with its takes (file names or Derived/<prefix>_* cuts) and a volume.
        static readonly (string key, string[] files, float volume)[] Bank =
        {
            ("seatbelt", new[] { "freesound_community-seatbelt-102265.mp3" }, 0.9f),
            ("handbrake", new[] { "freesound_community-car-hand-brake-45513.mp3" }, 0.7f),
            ("crash", new[] { "dragon-studio-car-crash-sound-effect-376874.mp3", "freesound_community-car-accident-with-squeal-and-crash-6054.mp3" }, 1f),
            ("glass", new[] { "dragon-studio-glass-breaking-504033.mp3" }, 0.9f),
            ("car_door", new[] { "Derived/car_door_both.wav" }, 0.9f),
            ("road_roll", new[] { "Derived/road_roll_loop.wav" }, 1f),
            ("passby", new[] { "spinopel-passing-car-393073.mp3" }, 0.8f),
            ("whoosh", new[] { "freesound_community-light-and-heavy-woosh-47892.mp3" }, 0.7f),
            ("moto_far", new[] { "spinopel-motorcycle-rev-in-the-distance-364893.mp3" }, 0.8f),
            ("siren", new[] { "dragon-studio-police-siren-397963.mp3" }, 0.8f),
            ("police_whistle", new[] { "horror_house_music-police-whistle-fx-1-566726.mp3" }, 0.8f),
            ("city_hum", new[] { "umbreonline-continuous-hum-of-a-city-from-a-distance-including-traffic-noise-338064.mp3" }, 1f),
            ("nature", new[] { "Derived/nature_loop.wav" }, 1f),
            ("wind", new[] { "soundque-house-terrace-wind-ambience-soundque-field-recording-445579.mp3" }, 1f),
            ("steps_gravel", new[] { "Derived/steps_gravel_loop.wav" }, 1f),
            ("steps_pavement", new[] { "Derived/steps_pavement_loop.wav", "tommylynn-walking-on-wet-pavement-374001.mp3" }, 1f),
            ("steps_wet", new[] { "Derived/steps_wet_loop.wav" }, 1f),
            ("murmur", new[] { "freesound_community-indoor-adult-murmur-small-groupwav-14573.mp3", "freesound_community-murmur-27730.mp3" }, 1f),
            ("cafe", new[] { "km007-cafe-ambience-9263.mp3" }, 1f),
            ("kitchen", new[] { "freesound_community-kitchen-cutlery-81287.mp3" }, 1f),
            ("door_restaurant", new[] { "dragon-studio-opening-door-450444.mp3" }, 0.8f),
            ("chair", new[] { "Derived/chair_*" }, 0.8f),
            ("plate_down", new[] { "Derived/plate_down_*", "Derived/plate_ceramic_*" }, 0.9f),
            ("pour", new[] { "freesound_community-tea-pouring-86194.mp3", "freesound_community-pouring-coffee-6743.mp3", "Derived/tea_cup_*" }, 0.8f),
            ("bottle_open", new[] { "freesound_community-openning-beer-bottle-96615.mp3" }, 0.9f),
            ("bite", new[] { "Derived/bite_*" }, 0.8f),
            ("cutlery", new[] { "Derived/cutlery_plate_*" }, 0.8f),
            ("sip", new[] { "Derived/sip_*", "Derived/sip_ahh_*" }, 0.8f),
        };

        // Not used: universfield creepy creaking door (a horror sound; nothing in the city creaks like that).

        public static KR.Gameplay.World.SoundBank.Entry[] BankEntries()
        {
            var list = new List<KR.Gameplay.World.SoundBank.Entry>();
            foreach (var (key, files, volume) in Bank)
            {
                var clips = new List<AudioClip>();
                foreach (var f in files)
                {
                    if (f.EndsWith("*"))
                    {
                        string dir = $"{Root}/{Path.GetDirectoryName(f).Replace('\\', '/')}", prefix = Path.GetFileName(f).TrimEnd('*');
                        clips.AddRange(AssetDatabase.FindAssets("t:AudioClip", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath)
                            .Where(p => Path.GetFileName(p).StartsWith(prefix)).OrderBy(p => p).Select(AssetDatabase.LoadAssetAtPath<AudioClip>));
                    }
                    else
                    {
                        var c = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}/{f}");
                        if (c != null) clips.Add(c);
                        else Debug.LogWarning($"[Sourced] Sound file missing for '{key}': {f}");
                    }
                }
                clips.RemoveAll(c => c == null);
                list.Add(new KR.Gameplay.World.SoundBank.Entry { key = key, clips = clips.ToArray(), volume = volume });
            }
            Debug.Log($"[Sourced] SoundBank: {list.Count} sounds, {list.Sum(e => e.clips.Length)} takes; empty: " +
                      string.Join(", ", list.Where(e => e.clips.Length == 0).Select(e => e.key)));
            return list.ToArray();
        }

        public static AudioClip Sound(string key)
        {
            if (!Sounds.TryGetValue(key, out var file))
                return null;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}/{file}");
            if (clip == null)
                Debug.LogWarning($"[Sourced] Sound '{key}' missing: {Root}/{file}");
            return clip;
        }

        // ---------- furniture ----------

        static readonly Dictionary<string, GameObject> Made = new Dictionary<string, GameObject>();

        // "painted_wooden_table" finds the folder painted_wooden_table_2k (any resolution suffix).
        public static GameObject Furniture(string name)
        {
            if (Made.TryGetValue(name, out var done) && done != null)
                return done;
            if (!AssetDatabase.IsValidFolder(FurnitureDir))
                return null;
            string folder = AssetDatabase.GetSubFolders(FurnitureDir).FirstOrDefault(f => Path.GetFileName(f).StartsWith(name + "_"));
            string fbx = folder == null ? null : AssetDatabase.FindAssets("t:Model", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            var model = fbx == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (model == null)
            {
                Debug.LogWarning($"[Sourced] No furniture model '{name}' in {FurnitureDir}.");
                return null;
            }
            EditorAssetUtil.EnsureFolder(PrefabDir);
            EditorAssetUtil.EnsureFolder(MatDir);
            var groups = TextureGroups(folder);
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(model);
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var r in temp.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string slot = (mats[i] != null ? mats[i].name : r.name).ToLowerInvariant();
                    var key = groups.Keys.OrderByDescending(k => Score(k, slot)).FirstOrDefault();
                    if (key != null)
                        mats[i] = MaterialFor(key, groups[key]);
                }
                r.sharedMaterials = mats;
            }
            var b = EditorAssetUtil.RendererBounds(temp);
            // Furniture taller than 2 m was exported on its end (Z-up): stand it up, the right way round
            // (most of a table's or chair's geometry is in its upper half: top, seat, benches).
            if (b.size.y > 2f)
            {
                var holder = new GameObject(name);
                temp.transform.SetParent(holder.transform, false);
                float best = float.MinValue;
                Quaternion pick = Quaternion.identity;
                foreach (var q in new[] { Quaternion.Euler(-90f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f) })
                {
                    temp.transform.localRotation = q;
                    var rb = EditorAssetUtil.RendererBounds(holder);
                    float score = UpperShare(holder, rb) - (rb.size.y > 2f ? 1f : 0f);
                    if (score > best)
                    {
                        best = score;
                        pick = q;
                    }
                }
                temp.transform.localRotation = pick;
                b = EditorAssetUtil.RendererBounds(holder);
                temp.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
                b = EditorAssetUtil.RendererBounds(holder);
                Debug.Log($"[Sourced] {name} was on its end; stood up ({pick.eulerAngles.x:0} deg about X).");
                temp = holder;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(temp, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(temp);
            Debug.Log($"[Sourced] {name}: {groups.Count} materials, size {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m.");
            return Made[name] = prefab;
        }

        // Share of the model's vertices in the upper half of its bounds.
        static float UpperShare(GameObject go, Bounds b)
        {
            int up = 0, all = 0;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null)
                    continue;
                var m = mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    all++;
                    if (m.MultiplyPoint3x4(v).y > b.center.y)
                        up++;
                }
            }
            return all > 0 ? (float)up / all : 0f;
        }

        // Textures grouped by material: "outdoor_table_chair_set_01_chair" -> {diff, nor, rough, metal}.
        static Dictionary<string, Dictionary<string, Texture2D>> TextureGroups(string folder)
        {
            var groups = new Dictionary<string, Dictionary<string, Texture2D>>();
            foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath))
            {
                string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                foreach (var kind in new[] { "_diff_", "_nor_gl_", "_rough_", "_metal_" })
                {
                    int at = n.IndexOf(kind);
                    if (at <= 0)
                        continue;
                    string key = n.Substring(0, at);
                    if (!groups.TryGetValue(key, out var g))
                        groups[key] = g = new Dictionary<string, Texture2D>();
                    g[kind.Trim('_')] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    break;
                }
            }
            return groups;
        }

        // How well a texture group's name matches a material slot (longest shared word wins).
        static int Score(string key, string slot)
        {
            if (slot.Contains(key) || key.Contains(slot))
                return 100 + key.Length;
            var words = key.Split('_');
            return words.Count(w => w.Length > 2 && slot.Contains(w));
        }

        static Material MaterialFor(string key, Dictionary<string, Texture2D> tex)
        {
            string path = $"{MatDir}/{key}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = key };
                AssetDatabase.CreateAsset(mat, path);
            }
            if (tex.TryGetValue("diff", out var diff))
            {
                mat.SetTexture("_BaseMap", diff);
                mat.SetColor("_BaseColor", Color.white);
            }
            if (tex.TryGetValue("nor_gl", out var nor))
            {
                mat.SetTexture("_BumpMap", nor);
                mat.EnableKeyword("_NORMALMAP");
            }
            // Metal map in R; these maps have no alpha, so smoothness is the constant below.
            if (tex.TryGetValue("metal", out var metal))
            {
                mat.SetTexture("_MetallicGlossMap", metal);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            mat.SetFloat("_Smoothness", key.Contains("pillow") || key.Contains("lounge") ? 0.15f : 0.32f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // The table part of a model (its renderers with "table" in the name), or the whole model.
        public static Bounds TableBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>().Where(r => r.name.ToLowerInvariant().Contains("table")).ToArray();
            if (rs.Length == 0)
                return EditorAssetUtil.RendererBounds(go);
            var b = rs[0].bounds;
            foreach (var r in rs)
                b.Encapsulate(r.bounds);
            return b;
        }
    }
}
