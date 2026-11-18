using System.Linq;
using KR.Editor.Util;
using KR.Gameplay.Player;
using KR.Gameplay.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Activity = KR.Editor.Kigali.PcPeople.Activity;

namespace KR.Editor.Characters
{
    // Builds the animator controllers that play the Mixamo set: one shared base for background people (each person
    // overrides its clips at runtime through PersonAnimator) and the player's controller (locomotion, driving,
    // one state per City Life activity, talking, waving, idle breaks, searching and celebrating).
    internal static class CharacterAnimation
    {
        const string Dir = "Assets/Art/Animations/Controllers";

        static RuntimeAnimatorController _npcBase;
        static AnimationClip[] _slots;

        public static bool Ready => MixamoCatalog.Available;

        public static void Begin()
        {
            _npcBase = null;
            _slots = null;
            MixamoCatalog.ClearCache();
            HumanoidRigs.ClearCache();
            if (!Ready)
                Debug.LogWarning("[Characters] Mixamo set not imported: people use the City Pack's own clips. " +
                                 "Run Kigali Rush > Characters > Import Mixamo Animations, then rebuild.");
        }

        // ---------- Background people ----------

        public static void AttachPerson(GameObject holder, Animator animator, Activity role, bool female, System.Random rng)
        {
            EnsureNpcBase();
            if (!MixamoCatalog.Roles.TryGetValue(role, out var r))
                r = MixamoCatalog.Roles[Activity.Idle];
            var idle = MixamoCatalog.Pick(r.Idle, female, rng);
            var move = MixamoCatalog.Pick(r.Move, female, rng) ?? idle;
            var talk = MixamoCatalog.Pick(r.Talk, female, rng) ?? idle;
            var greet = MixamoCatalog.Pick(r.Greet, female, rng) ?? idle;
            var actions = r.Actions.Take(3).Select(a => MixamoCatalog.Pick(a, female, rng)).Where(c => c != null).ToList();
            var clips = new[] { idle, move, talk, greet,
                                actions.ElementAtOrDefault(0) ?? idle, actions.ElementAtOrDefault(1) ?? idle, actions.ElementAtOrDefault(2) ?? idle };
            // Root speed of the walk/run so playback matches how fast the person actually moves (no foot sliding).
            float clipSpeed = move != null ? Mathf.Clamp(move.averageSpeed.magnitude, 0.6f, 6f) : 1.4f;
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;   // PersonAnimator assigns the shared override at runtime
            holder.AddComponent<PersonAnimator>().Configure(animator, _npcBase, _slots, clips, actions.Count, clipSpeed, r.ActionEvery);
        }

        static void EnsureNpcBase()
        {
            if (_npcBase != null)
                return;
            EditorAssetUtil.EnsureFolder(Dir);
            _slots = PersonAnimator.Slots.Select(slot =>
            {
                string path = $"{Dir}/Slot_{slot}.anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    clip = new AnimationClip { name = $"Slot_{slot}" };
                    AssetDatabase.CreateAsset(clip, path);
                }
                return clip;
            }).ToArray();

            string controllerPath = $"{Dir}/Person_Base.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var c = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            c.AddParameter("Speed", AnimatorControllerParameterType.Float);
            c.AddParameter("MoveScale", AnimatorControllerParameterType.Float);
            c.AddParameter("Talking", AnimatorControllerParameterType.Bool);
            var ps = c.parameters;
            ps[1].defaultFloat = 1f;
            c.parameters = ps;

            var sm = c.layers[0].stateMachine;
            AnimatorState State(string name, int slot)
            {
                var s = sm.AddState(name);
                s.motion = _slots[slot];
                s.iKOnFeet = true;
                return s;
            }
            var idle = State("Idle", 0);
            sm.defaultState = idle;
            var move = State("Move", 1);
            move.speedParameterActive = true;
            move.speedParameter = "MoveScale";
            var talk = State("Talk", 2);
            var greet = State("Greet", 3);
            var actions = new[] { State("Action0", 4), State("Action1", 5), State("Action2", 6) };

            Go(idle, move, 0.25f).AddCondition(AnimatorConditionMode.Greater, 0.15f, "Speed");
            Go(move, idle, 0.3f).AddCondition(AnimatorConditionMode.Less, 0.08f, "Speed");
            Go(idle, talk, 0.3f).AddCondition(AnimatorConditionMode.If, 0f, "Talking");
            Go(talk, idle, 0.3f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Talking");
            foreach (var once in actions.Append(greet))
            {
                Done(once, idle);
                Go(once, move, 0.2f).AddCondition(AnimatorConditionMode.Greater, 0.4f, "Speed");
            }
            _npcBase = c;
        }

        // ---------- The player ----------

        public static RuntimeAnimatorController PlayerController(string dir, PlayerTuning settings)
        {
            EditorAssetUtil.EnsureFolder(dir);
            string path = $"{dir}/Player_Mixamo.controller";
            AssetDatabase.DeleteAsset(path);
            var c = AnimatorController.CreateAnimatorControllerAtPath(path);
            c.AddParameter("Speed", AnimatorControllerParameterType.Float);
            c.AddParameter("Seated", AnimatorControllerParameterType.Bool);
            var locomotionState = c.CreateBlendTreeInController("Locomotion", out var tree, 0);
            locomotionState.iKOnFeet = true;
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            var idle = MixamoCatalog.PlayerClip("Idle");
            var walk = MixamoCatalog.PlayerClip("Walk");
            var run = MixamoCatalog.PlayerClip("Run");
            if (idle != null) tree.AddChild(idle, 0f);
            if (walk != null) tree.AddChild(walk, settings != null ? settings.walkSpeed : 1.9f);
            if (run != null) tree.AddChild(run, settings != null ? settings.runSpeed : 5.2f);

            var sm = c.layers[0].stateMachine;
            sm.defaultState = locomotionState;
            int states = 0;
            foreach (var key in MixamoCatalog.Player.Keys.Where(k => k != "Idle" && k != "Walk" && k != "Run"))
            {
                var clip = MixamoCatalog.PlayerClip(key);
                if (clip == null)
                    continue;
                var s = sm.AddState(key);
                s.motion = clip;
                s.iKOnFeet = key != "Exhausted" && key != "Seated";
                states++;
                if (key == "Seated")
                {
                    Go(locomotionState, s, 0.15f).AddCondition(AnimatorConditionMode.If, 0f, "Seated");
                    Go(s, locomotionState, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Seated");
                }
                else if (MixamoCatalog.PlayerOneShots.Contains(key))
                    Done(s, locomotionState);
            }
            Debug.Log($"[Characters] Player controller: locomotion + {states} Mixamo states ({path}).");
            // IK pass: first person puts the character's hands on the steering wheel and the tracked controllers.
            var layers = c.layers;
            layers[0].iKPass = true;
            c.layers = layers;
            EditorUtility.SetDirty(c);
            return c;
        }

        // ---------- Helpers ----------

        static AnimatorStateTransition Go(AnimatorState from, AnimatorState to, float fade)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = fade;
            return t;
        }

        // Back to `to` as the clip ends.
        static void Done(AnimatorState from, AnimatorState to)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = 0.92f;
            t.duration = 0.25f;
        }
    }
}
