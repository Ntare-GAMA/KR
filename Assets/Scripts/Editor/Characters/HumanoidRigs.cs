using System.Collections.Generic;
using System.IO;
using System.Linq;
using KR.Editor.Util;
using UnityEditor;
using UnityEngine;

namespace KR.Editor.Characters
{
    // Humanoid avatars for the City Pack (Quaternius) characters so Mixamo clips can drive them.
    // Most of those rigs are built for Blender IK: the feet hang off the root and the thighs off "Body" instead of the
    // hips. Unity's Humanoid needs foot under shin and thigh under hips, so FixHierarchy re-parents those bones
    // (world pose kept, so skinning is unchanged) on every spawned copy, and the avatar is built from a copy fixed
    // the same way, with arms and legs straightened into the T-pose Humanoid expects. Source FBX files are untouched.
    internal static class HumanoidRigs
    {
        const string AvatarDir = "Assets/Art/Characters/Avatars";

        // Human bone -> bone names in the City Pack rigs (Quaternius first, then the Mixamo-style "Animated Woman"),
        // then Blender's Rigify "metarig" (the Floreswa low-poly people).
        static readonly (string human, string[] names, bool required)[] Map =
        {
            ("Hips", new[] { "Hips", "spine" }, true),
            ("Spine", new[] { "Abdomen", "Spine", "spine.001" }, true),
            ("Chest", new[] { "Torso", "Spine1", "spine.002" }, false),
            ("UpperChest", new[] { "Chest", "Spine2", "spine.003" }, false),
            ("Neck", new[] { "Neck", "spine.004" }, false),
            ("Head", new[] { "Head", "spine.006", "spine.005" }, true),
            ("LeftShoulder", new[] { "Shoulder.L", "LeftShoulder", "shoulder.L" }, false),
            ("LeftUpperArm", new[] { "UpperArm.L", "LeftArm", "upper_arm.L" }, true),
            ("LeftLowerArm", new[] { "LowerArm.L", "LeftForeArm", "forearm.L" }, true),
            ("LeftHand", new[] { "Wrist.L", "Palm.L", "LeftHand", "hand.L" }, true),
            ("RightShoulder", new[] { "Shoulder.R", "RightShoulder", "shoulder.R" }, false),
            ("RightUpperArm", new[] { "UpperArm.R", "RightArm", "upper_arm.R" }, true),
            ("RightLowerArm", new[] { "LowerArm.R", "RightForeArm", "forearm.R" }, true),
            ("RightHand", new[] { "Wrist.R", "Palm.R", "RightHand", "hand.R" }, true),
            ("LeftUpperLeg", new[] { "UpperLeg.L", "LeftUpLeg", "thigh.L" }, true),
            ("LeftLowerLeg", new[] { "LowerLeg.L", "LeftLeg", "shin.L" }, true),
            ("LeftFoot", new[] { "Foot.L", "LeftFoot", "foot.L" }, true),
            ("LeftToes", new[] { "LeftToeBase", "toe.L" }, false),
            ("RightUpperLeg", new[] { "UpperLeg.R", "RightUpLeg", "thigh.R" }, true),
            ("RightLowerLeg", new[] { "LowerLeg.R", "RightLeg", "shin.R" }, true),
            ("RightFoot", new[] { "Foot.R", "RightFoot", "foot.R" }, true),
            ("RightToes", new[] { "RightToeBase", "toe.R" }, false),
        };

        static readonly Dictionary<GameObject, Avatar> Cache = new Dictionary<GameObject, Avatar>();
        static readonly HashSet<GameObject> Failed = new HashSet<GameObject>();

        public static void ClearCache()
        {
            Cache.Clear();
            Failed.Clear();
        }

        public static bool IsFemale(GameObject model) =>
            AssetDatabase.GetAssetPath(model).IndexOf("Woman", System.StringComparison.OrdinalIgnoreCase) >= 0;

        public static void FixHierarchy(Transform root)
        {
            foreach (var s in new[] { "L", "R" })
            {
                Reparent(root, $"Foot.{s}", $"LowerLeg.{s}");
                Reparent(root, $"UpperLeg.{s}", "Hips");
            }
        }

        static void Reparent(Transform root, string child, string parent)
        {
            var c = Find(root, child);
            var p = Find(root, parent);
            if (c != null && p != null && c.parent != p && !p.IsChildOf(c))
                c.SetParent(p, true);
        }

        static Transform Find(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        // Humanoid avatar for this model, built once per build and saved under Assets/Art/Characters/Avatars.
        // Null when the rig cannot be mapped (the person then keeps the model's own clips).
        public static Avatar AvatarFor(GameObject model)
        {
            if (model == null || Failed.Contains(model))
                return null;
            if (Cache.TryGetValue(model, out var cached) && cached != null)
                return cached;

            var temp = Object.Instantiate(model);
            temp.name = model.name;
            try
            {
                temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                temp.transform.localScale = Vector3.one;
                FixHierarchy(temp.transform);

                var bones = new Dictionary<string, Transform>();
                var human = new List<HumanBone>();
                foreach (var (humanName, names, required) in Map)
                {
                    var t = names.Select(n => Find(temp.transform, n)).FirstOrDefault(x => x != null);
                    if (t == null)
                    {
                        if (required)
                        {
                            Debug.LogWarning($"[Characters] {model.name}: no bone for {humanName}; it keeps its own animations.");
                            Failed.Add(model);
                            return null;
                        }
                        continue;
                    }
                    bones[humanName] = t;
                    human.Add(new HumanBone { humanName = humanName, boneName = t.name, limit = new HumanLimit { useDefaultValues = true } });
                }
                EnforceTPose(bones);

                var seen = new HashSet<string>();
                var skeleton = temp.GetComponentsInChildren<Transform>(true).Where(t => seen.Add(t.name))
                    .Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale })
                    .ToArray();
                var description = new HumanDescription
                {
                    human = human.ToArray(), skeleton = skeleton,
                    upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                    armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
                };
                var avatar = AvatarBuilder.BuildHumanAvatar(temp, description);
                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                {
                    Debug.LogWarning($"[Characters] {model.name}: humanoid avatar is not valid; it keeps its own animations.");
                    Failed.Add(model);
                    return null;
                }
                EditorAssetUtil.EnsureFolder(AvatarDir);
                string safe = string.Concat(Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(model)).Split(Path.GetInvalidFileNameChars()));
                string folder = string.Concat(Path.GetFileName(Path.GetDirectoryName(AssetDatabase.GetAssetPath(model)) ?? "").Split(Path.GetInvalidFileNameChars()));
                string path = $"{AvatarDir}/{folder}_{safe}_Humanoid.asset";
                AssetDatabase.DeleteAsset(path);
                avatar.name = $"{model.name} Humanoid";
                AssetDatabase.CreateAsset(avatar, path);
                Cache[model] = avatar;
                return avatar;
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        // Straight arms out to the sides and straight legs down: the reference pose Humanoid retargets from.
        static void EnforceTPose(Dictionary<string, Transform> b)
        {
            Vector3 right = b["RightUpperArm"].position - b["LeftUpperArm"].position;
            right.y = 0f;
            right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
            Aim(b["LeftUpperArm"], b["LeftLowerArm"], -right);
            Aim(b["LeftLowerArm"], b["LeftHand"], -right);
            Aim(b["RightUpperArm"], b["RightLowerArm"], right);
            Aim(b["RightLowerArm"], b["RightHand"], right);
            Aim(b["LeftUpperLeg"], b["LeftLowerLeg"], Vector3.down);
            Aim(b["LeftLowerLeg"], b["LeftFoot"], Vector3.down);
            Aim(b["RightUpperLeg"], b["RightLowerLeg"], Vector3.down);
            Aim(b["RightLowerLeg"], b["RightFoot"], Vector3.down);
        }

        static void Aim(Transform bone, Transform child, Vector3 direction)
        {
            var current = child.position - bone.position;
            if (current.sqrMagnitude < 1e-8f)
                return;
            bone.rotation = Quaternion.FromToRotation(current, direction) * bone.rotation;
        }
    }
}
