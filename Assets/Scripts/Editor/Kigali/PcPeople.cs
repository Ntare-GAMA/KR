using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KR.Editor.Util;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace KR.Editor.Kigali
{
    // Spawns animated residents from the Quaternius characters (City Pack, CC0), fits the height and gives each person
    // one of three darker skin tones. With the Mixamo set imported, each person gets a Humanoid avatar and a
    // PersonAnimator with clips for their role (Characters/MixamoCatalog); otherwise the closest clip in the model loops.
    internal static class PcPeople
    {
        public enum Activity { Walk, Idle, Run, Dance, Work, Talk, Wave, Vendor, Shopper, Wait, Watch, Stroll, Cheer, Keeper, Serve }

        // Fallback without Mixamo: clip name keywords per activity, in order of preference ("...|Walk").
        static readonly Dictionary<Activity, string[]> Keywords = new Dictionary<Activity, string[]>
        {
            [Activity.Walk] = new[] { "Walk" },
            [Activity.Idle] = new[] { "Idle" },
            [Activity.Run] = new[] { "Run" },
            [Activity.Dance] = new[] { "Dance", "Yes", "Wave", "Jump" },
            [Activity.Work] = new[] { "Interact", "PickUp", "Pick", "Punch", "Attack" },
            [Activity.Talk] = new[] { "Yes", "No", "Idle" },
            [Activity.Wave] = new[] { "Wave", "Yes", "Idle" },
            [Activity.Vendor] = new[] { "Idle" },
            [Activity.Shopper] = new[] { "Walk" },
            [Activity.Wait] = new[] { "Idle" },
            [Activity.Watch] = new[] { "Idle" },
            [Activity.Stroll] = new[] { "Walk" },
            [Activity.Cheer] = new[] { "Yes", "Wave", "Idle" },
            [Activity.Keeper] = new[] { "Idle" },
            [Activity.Serve] = new[] { "Idle" },
        };
        static readonly Color[] Tones = { new Color(0.36f, 0.22f, 0.14f), new Color(0.45f, 0.29f, 0.18f), new Color(0.27f, 0.16f, 0.10f) };

        static readonly Dictionary<(GameObject, Activity), RuntimeAnimatorController> Controllers =
            new Dictionary<(GameObject, Activity), RuntimeAnimatorController>();
        static readonly Dictionary<(Material, int), Material> Toned = new Dictionary<(Material, int), Material>();
        static string _dir;
        static readonly List<Transform> Holders = new List<Transform>();

        public static int Spawned { get; private set; }

        public static void Begin(string peopleDir)
        {
            _dir = peopleDir;
            EditorAssetUtil.EnsureFolder(peopleDir);
            Controllers.Clear();
            Toned.Clear();
            Holders.Clear();
            Spawned = 0;
            Humanoid = 0;
            Characters.CharacterAnimation.Begin();
        }

        public static int Humanoid { get; private set; }

        // `brain` false: the caller drives the animator itself (the player).
        public static GameObject Spawn(GameObject model, Transform parent, Vector3 position, float yaw, Activity activity,
                                       System.Random rng, string label = null, bool brain = true)
        {
            var holder = new GameObject(label ?? $"{activity} ({model.name})").transform;
            holder.SetParent(parent, false);
            holder.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var body = EditorAssetUtil.InstantiateUnpacked(model, holder);
            body.name = model.name;
            EditorAssetUtil.StripRuntimeComponents(body);
            FitHeight(body, 1.55f + (float)rng.NextDouble() * 0.3f);
            var animator = body.GetComponentInChildren<Animator>();
            if (animator == null)
                animator = body.AddComponent<Animator>();
            var avatar = Characters.CharacterAnimation.Ready ? Characters.HumanoidRigs.AvatarFor(model) : null;
            if (avatar != null && animator.gameObject == body)
            {
                Characters.HumanoidRigs.FixHierarchy(body.transform);
                animator.avatar = avatar;
                if (brain)
                    Characters.CharacterAnimation.AttachPerson(holder.gameObject, animator, activity, Characters.HumanoidRigs.IsFemale(model), rng);
                Humanoid++;
            }
            else
                animator.runtimeAnimatorController = Controller(model, activity);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullCompletely;
            // Playback varies a little between people so crowds don't move in step; PersonAnimator plays the walk
            // at exactly the speed the person moves, so feet don't slide.
            animator.speed = 0.85f + (float)rng.NextDouble() * 0.3f;
            ApplySkinTone(body, rng.Next(Tones.Length));
            Spawned++;
            Holders.Add(holder);
            return holder.gameObject;
        }

        // People placed on the terrain where a pavement or road now covers it stand on that surface instead.
        // Only ever raises: people deliberately lifted (stages, steps) keep their height.
        public static void SnapToSurfaces(Terrain terrain)
        {
            Physics.SyncTransforms();
            foreach (var h in Holders)
            {
                if (h == null)
                    continue;
                var p = h.position;
                float y = PcRoadBuilder.SurfaceHeight(terrain, p);
                if (y > p.y + 0.01f)
                    h.position = new Vector3(p.x, y, p.z);
            }
        }

        public static RuntimeAnimatorController Controller(GameObject model, Activity activity)
        {
            if (Controllers.TryGetValue((model, activity), out var cached))
                return cached;
            string path = AssetDatabase.GetAssetPath(model);
            AnimationClip[] Clips() => AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            AnimationClip Choose(AnimationClip[] clips)
            {
                foreach (var kw in Keywords[activity])
                {
                    var hit = clips.FirstOrDefault(c => c.name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                                        c.name.IndexOf("Gun", StringComparison.OrdinalIgnoreCase) < 0);
                    if (hit != null)
                        return hit;
                }
                return clips.FirstOrDefault(c => c.name.Contains("Idle")) ?? clips.FirstOrDefault();
            }

            var clip = Choose(Clips());
            RuntimeAnimatorController result = null;
            if (clip != null && AssetImporter.GetAtPath(path) is ModelImporter importer)
            {
                var defs = importer.clipAnimations;
                if (defs == null || defs.Length == 0)
                    defs = importer.defaultClipAnimations;
                var def = defs.FirstOrDefault(c => c.name == clip.name);
                if (def != null && !def.loopTime)
                {
                    def.loopTime = true;
                    importer.clipAnimations = defs;
                    importer.SaveAndReimport();
                    clip = Clips().FirstOrDefault(c => c.name == clip.name) ?? clip;
                }
                string safe = string.Concat(model.name.Split(Path.GetInvalidFileNameChars()));
                string controllerPath = $"{_dir}/{activity}_{safe}.controller";
                AssetDatabase.DeleteAsset(controllerPath);
                result = AnimatorController.CreateAnimatorControllerAtPathWithClip(controllerPath, clip);
            }
            else
                Debug.LogWarning($"[PcPeople] {model.name}: no animation clip for {activity}; it will hold its pose.");
            Controllers[(model, activity)] = result;
            return result;
        }

        static void FitHeight(GameObject body, float height)
        {
            var t = body.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            var b = EditorAssetUtil.RendererBounds(body);
            if (b.size.y > 0.01f)
                t.localScale = Vector3.one * (height / b.size.y);
            b = EditorAssetUtil.RendererBounds(body);
            Vector3 origin = t.parent != null ? t.parent.position : Vector3.zero;
            t.position += new Vector3(origin.x - b.center.x, origin.y - b.min.y, origin.z - b.center.z);
        }

        // Skin materials (by material or texture name) get a darker tone; textured skins use the pack's own
        // DarkSkin textures when they sit next to the original.
        static void ApplySkinTone(GameObject body, int tone)
        {
            foreach (var r in body.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null)
                        continue;
                    var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                    bool skinTex = tex != null && tex.name.ToLowerInvariant().Contains("skin");
                    if (!skinTex && !m.name.ToLowerInvariant().Contains("skin"))
                        continue;
                    if (!Toned.TryGetValue((m, tone), out var toned))
                    {
                        toned = new Material(m);
                        Texture2D dark = null;
                        if (skinTex)
                        {
                            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(tex))?.Replace('\\', '/');
                            string[] names = { "DarkSkin", "DarkSkin1", "DarkSkin2" };
                            dark = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/{names[tone]}.png");
                        }
                        if (dark != null)
                        {
                            if (toned.HasProperty("_BaseMap")) toned.SetTexture("_BaseMap", dark);
                            toned.mainTexture = dark;
                        }
                        else if (toned.HasProperty("_BaseColor"))
                            toned.SetColor("_BaseColor", Tones[tone]);
                        string safe = string.Concat(m.name.Split(Path.GetInvalidFileNameChars()));
                        string matPath = $"{_dir}/{safe}_tone{tone}.mat";
                        // Update the existing asset in place: deleting and re-creating it gave it a new id, and every
                        // person placed by an earlier build step lost their material (drawn magenta).
                        var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                        if (existing != null)
                        {
                            EditorUtility.CopySerialized(toned, existing);
                            existing.name = Path.GetFileNameWithoutExtension(matPath);
                            EditorUtility.SetDirty(existing);
                            toned = existing;
                        }
                        else
                            AssetDatabase.CreateAsset(toned, matPath);
                        Toned[(m, tone)] = toned;
                    }
                    mats[i] = toned;
                    changed = true;
                }
                if (changed)
                    r.sharedMaterials = mats;
            }
        }
    }
}
