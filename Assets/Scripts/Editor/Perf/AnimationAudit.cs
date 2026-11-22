using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KR.Gameplay.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KR.Editor.Perf
{
    // Finds people in the story scene who would move without a walking animation (owner, 2026-11-22: "characters
    // that have no animations in terms of movement, remove anything like that"): a person-like object that is moved
    // by a script (traffic walkers, wanderers, street walkers, crossing walkers) or has a PersonAnimator, but whose
    // Animator is missing, has no controller, has no valid avatar, or has no clip in the Move slot.
    // Report: Logs/animation_audit.txt. Remove: deletes them, saves the scene and keeps the editable layers.
    public static class AnimationAudit
    {
        public static void Report() => Run(false);
        public static void Remove() => Run(true);

        // The people's walking and talking clips are humanoid, but some body models were imported as Generic with
        // no avatar, so they could not play them (frozen, or sliding along). Re-imports those models as Humanoid
        // and gives each body in the scene its model's new avatar. Then Remove() deletes only what still fails.
        public static void FixRigs()
        {
            var scene = EditorSceneManager.OpenScene(PerfReport.StoryScene, OpenSceneMode.Single);
            var animators = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Animator>(true))
                                 .Where(a => a.avatar == null || !a.avatar.isValid).ToList();
            var models = new Dictionary<string, Avatar>();
            int fixedModels = 0, assigned = 0;
            foreach (var a in animators)
            {
                // The bodies are not linked to their model prefab; the skinned mesh tells which model file it is.
                var skin = a.GetComponentInChildren<SkinnedMeshRenderer>(true);
                string path = skin != null && skin.sharedMesh != null ? AssetDatabase.GetAssetPath(skin.sharedMesh) : null;
                if (string.IsNullOrEmpty(path) || !(AssetImporter.GetAtPath(path) is ModelImporter mi))
                    continue;
                if (!models.ContainsKey(path))
                {
                    if (mi.animationType != ModelImporterAnimationType.Human)
                    {
                        mi.animationType = ModelImporterAnimationType.Human;
                        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                        mi.SaveAndReimport();
                        fixedModels++;
                    }
                    var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                    models[path] = avatar != null && avatar.isValid && avatar.isHuman ? avatar : null;
                    Debug.Log($"[AnimationAudit] {path}: humanoid avatar {(models[path] != null ? "OK" : "FAILED (rig not recognised)")}");
                }
                if (models[path] != null)
                {
                    a.avatar = models[path];
                    assigned++;
                }
            }
            // Bodies with a PersonAnimator but no controller borrow the setup (controller, slot placeholders and
            // clips: idle, walk, talk...) of a person in the city that does animate.
            var people = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PersonAnimator>(true)).ToList();
            PersonAnimator donor = null;
            foreach (var pa in people)
            {
                var so = new SerializedObject(pa);
                var anim = pa.GetComponentInChildren<Animator>(true);
                if (so.FindProperty("baseController").objectReferenceValue != null && anim != null && anim.avatar != null &&
                    anim.avatar.isValid && so.FindProperty("clips").arraySize >= 2 &&
                    so.FindProperty("clips").GetArrayElementAtIndex(1).objectReferenceValue != null)
                {
                    donor = pa;
                    break;
                }
            }
            int adopted = 0;
            if (donor == null)
                Debug.LogWarning("[AnimationAudit] No person in the city has a working animation setup to share.");
            else
            {
                var from = new SerializedObject(donor);
                foreach (var pa in people)
                {
                    var so = new SerializedObject(pa);
                    var anim = pa.GetComponentInChildren<Animator>(true);
                    if (so.FindProperty("baseController").objectReferenceValue != null || anim == null || anim.avatar == null || !anim.avatar.isValid)
                        continue;
                    foreach (var prop in new[] { "baseController", "placeholders", "clips", "actionCount", "moveClipSpeed" })
                        so.CopyFromSerializedProperty(from.FindProperty(prop));
                    so.FindProperty("animator").objectReferenceValue = anim;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    anim.runtimeAnimatorController = (RuntimeAnimatorController)from.FindProperty("baseController").objectReferenceValue;
                    anim.applyRootMotion = false;
                    adopted++;
                }
                Debug.Log($"[AnimationAudit] Animation setup copied from {PathOf(donor.transform)} to {adopted} people.");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            KR.Editor.Kigali.EditableLayer.Keep();
            Debug.Log($"[AnimationAudit] {fixedModels} models re-imported as Humanoid; {assigned} bodies given a working avatar; {adopted} given animations.");
        }

        [MenuItem("Kigali Rush/Characters/Remove people without walking animations")]
        static void RemoveMenu() => Run(true);

        static void Run(bool remove)
        {
            var scene = EditorSceneManager.OpenScene(PerfReport.StoryScene, OpenSceneMode.Single);
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
            var log = new StringBuilder();
            var bad = new List<(GameObject go, string why)>();
            var seen = new HashSet<GameObject>();

            // 1) Everyone with a PersonAnimator.
            foreach (var pa in all.Select(t => t.GetComponent<PersonAnimator>()).Where(p => p != null))
            {
                string why = Problem(pa.gameObject, pa);
                if (why != null && seen.Add(pa.gameObject))
                    bad.Add((pa.gameObject, why));
            }
            // 2) Bodies moved by scripts that have no PersonAnimator at all.
            foreach (var t in all)
            {
                if (seen.Contains(t.gameObject))
                    continue;
                bool moved = t.GetComponent<Wanderer>() != null || t.GetComponent<StreetWalker>() != null;
                if (!moved)
                    continue;
                if (t.GetComponentInChildren<PersonAnimator>(true) != null)
                    continue;
                var anim = t.GetComponentInChildren<Animator>(true);
                string why = anim == null ? "moved by a script, no Animator"
                           : anim.runtimeAnimatorController == null ? "moved by a script, Animator has no controller"
                           : anim.avatar == null || !anim.avatar.isValid ? "moved by a script, no valid avatar" : null;
                if (why != null && seen.Add(t.gameObject))
                    bad.Add((t.gameObject, why));
            }
            // 3) Walkers in the traffic system (pedestrian bodies).
            foreach (var traffic in all.Select(t => t.GetComponent<RoadNetworkTraffic>()).Where(r => r != null && r.IsWalkers))
                for (int i = 0; i < traffic.transform.childCount; i++)
                {
                    var body = traffic.transform.GetChild(i).gameObject;
                    if (seen.Contains(body))
                        continue;
                    var anim = body.GetComponentInChildren<Animator>(true);
                    if (anim == null || anim.runtimeAnimatorController == null || anim.avatar == null || !anim.avatar.isValid)
                        if (seen.Add(body))
                            bad.Add((body, "traffic walker without a working Animator"));
                }

            foreach (var g in bad.GroupBy(b => b.why))
            {
                log.AppendLine($"== {g.Key}: {g.Count()}");
                foreach (var (go, _) in g)
                    log.AppendLine("   " + PathOf(go.transform));
            }
            log.AppendLine($"TOTAL {bad.Count} people without a walking animation");
            if (remove && bad.Count > 0)
            {
                foreach (var (go, _) in bad)
                {
                    if (go == null)
                        continue;
                    // Never the story's own characters (missions need them); they are listed to fix by hand.
                    if (PathOf(go.transform).StartsWith("Story Mode/Characters"))
                    {
                        log.AppendLine("KEPT (story character): " + PathOf(go.transform));
                        continue;
                    }
                    Object.DestroyImmediate(go);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                KR.Editor.Kigali.EditableLayer.Keep();
                log.AppendLine("Removed, scene saved.");
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/animation_audit.txt", log.ToString());
            Debug.Log($"[AnimationAudit] {bad.Count} people without a walking animation{(remove ? " removed" : "")}. Logs/animation_audit.txt");
        }

        static string Problem(GameObject go, PersonAnimator pa)
        {
            var anim = go.GetComponentInChildren<Animator>(true);
            if (anim == null)
                return "PersonAnimator but no Animator";
            if (anim.avatar == null || !anim.avatar.isValid)
                return "no valid avatar (the body cannot animate)";
            var so = new SerializedObject(pa);
            var clips = so.FindProperty("clips");
            var baseController = so.FindProperty("baseController");
            if (baseController != null && baseController.objectReferenceValue == null && anim.runtimeAnimatorController == null)
                return "no animation controller";
            // Slot 1 is "Move" (PersonAnimator.Slots).
            if (clips != null && clips.isArray && (clips.arraySize < 2 || clips.GetArrayElementAtIndex(1).objectReferenceValue == null) &&
                (clips.arraySize == 0 || clips.GetArrayElementAtIndex(0).objectReferenceValue == null))
                return "no walking clip";
            return null;
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    }
}
