using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KR.Editor.Kigali;
using UnityEditor;
using UnityEngine;
using Activity = KR.Editor.Kigali.PcPeople.Activity;

namespace KR.Editor.Characters
{
    // Which Mixamo clips each person in Kigali uses, by what they are doing. Names are the downloaded file names
    // (SourceArt/Mixamo Animations/Animations/<name>.fbx); only these are copied into the project.
    // Several options per slot: each person picks one, so crowds do not move alike. Clips starting "Female"
    // go to women, "Male" to men; the rest to anyone.
    // Left out on purpose: weapons, fighting, hostage and injury clips. Nothing in City Life calls for them, and
    // in a game set in Kigali they do not belong among civilians.
    internal static class MixamoCatalog
    {
        public const string Folder = "Assets/Art/Animations/Mixamo";

        public static string SourceFolder =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "SourceArt", "Mixamo Animations", "Animations"));

        public sealed class Role
        {
            public string[] Idle, Move, Talk, Greet;
            public string[][] Actions = new string[0][];   // up to three slots, each a list of options
            public Vector2 ActionEvery = new Vector2(7f, 16f);
        }

        // ---------- Shared pools ----------

        static readonly string[] Standing =
        {
            "Standing Idle", "Breathing Idle", "Weight Shift Idle", "Standard Idle", "Neutral Idle", "Male Standing Idle 01",
            "Male Standing Idle 02", "Female Standing Idle", "Female Idle", "Happy Idle Variation 1", "Happy Idle Variation 2",
            "Standing Short Idle", "Standing Idle Looking Around",
        };
        static readonly string[] Waiting =
        {
            "Standing Around Bored Idle", "Texting On A Smart Phone While Standing", "Standing Texting On Phone",
            "Male Standing While Talking On A Cell Phone", "Female Standing Talking On Phone", "Legs Crossed, Arms Crossed",
            "Weight Shift Idle", "Breathing Idle", "Female Standing Idle",
        };
        static readonly string[] Glances =
        {
            "Looking Around", "Looking Off Into The Distance", "Hands On Hips Looking Over Shoulder", "Idle Stand Looking Around",
            "Standing Idle Looking Behind", "Looking Left", "Looking Right",
        };
        static readonly string[] Fidgets =
        {
            "Searching Front And Back Pockets", "Stretching Arms By Pushing The Elbows", "Stretching Neck Rolling Side To Side",
            "Thinking While Standing", "Big Yawn While Standing",
        };
        static readonly string[] Conversation =
        {
            "General Conversation", "Talking Finding Something Funny", "Asking A Question", "Asking A Question With One Hand",
            "Asking A Question With Two Hands", "Happy Forward Hand Gesture", "Two Handed Forward Gesture",
            "Thoughtfully Nodding Head Yes",
        };
        static readonly string[] Reactions =
        {
            "Laughing Standing", "Agreeing Yes", "Nodding Head Yes", "Acknowledging Gesture", "Shaking Head No Thoughtfully",
            "Reaching Out Gesture",
        };
        static readonly string[] Greetings =
        {
            "Waving", "Greeting While Standing", "Aj Waving Gesture", "Emotional Waving Forward", "Quick Informal Bow",
        };
        static readonly string[] Walks =
        {
            "Walking", "Standard Walk", "Walking Forward", "Male Walk Forward 01", "Male Walk Forward 02", "Male Walk Forward 03",
            "Male Standard Walk", "Male Walking While Texting On A Smartphone", "Male Walk With Briefcase", "Male Hurried Walk",
            "Walking With A Swagger", "Female Walk", "Female Normal Walk", "Feminine Walk Forward", "Female Walk Forward",
            "Female Walking And Texting On Phone", "Female Walk With Briefcase", "Female Walk Forward - Legs Crossing",
        };
        static readonly string[] Dances =
        {
            "Afoxe Samba Reggae Dance", "Female _African Rainbow_ Dance", "Hip Hop Dancing Side To Side", "House Dance Variation Two",
            "House Dance Variation 3", "House Dance Variation Four", "Salsa Dancing Side To Side", "Female Salsa Dancing",
            "Samba Funky Pocoto Variation 1", "Samba Gandy Variation 1", "Samba Dance Olodum Variation Two", "Booty Step Hip Hop Dance",
            "Step Hip Hop Dance", "Snake Hip Hop Dance", "Wave Hip Hop Dance Variation One", "Locking Hip Hop Dance Variation Two",
            "Basic Northern Soul Step", "Boogaloo", "Basic Rock Beat", "Doing The Shuffling Dance",
            "Hip Hop Just Listening Dancing Variation", "Timid Dancing", "Silly Dancing The Twist", "Hip Hop Dancing Shimmy",
            "Hip Hop Dancing With Arms", "Dancing The Running Man", "Salsa Dancing Up And Back",
            "Female Hip Hop 'Rib Pops' Dancing", "Female Hip Hop 'Raise The Roof' Dancing", "Female Hip Hop 'Slide Step' Dancing",
            "Female Rumba Dancing -  Loop", "Female Samba Pagode Variation Five- Loop",
        };
        static readonly string[] Showpieces =
        {
            "Bboy Uprock Indian Step", "Breakdance Uprock", "Male Cheering Head Banging", "Salsa Dancing Twirl And Clap",
            "Hip Hop Dancing Really Twirl",
        };

        static string[] Join(params string[][] sets) => sets.SelectMany(s => s).Distinct().ToArray();

        // ---------- Roles ----------

        public static readonly Dictionary<Activity, Role> Roles = new Dictionary<Activity, Role>
        {
            // Pedestrians on the footpaths.
            // A waiter or waitress: carries a tray as they walk, bows to greet, points to show you your table.
            [Activity.Serve] = new Role
            {
                Idle = new[] { "Breathing Idle", "Standing Idle", "Female Standing Idle", "Weight Shift Idle" },
                Move = new[] { "Holding An Object While Walking" },
                Talk = Conversation,
                Greet = new[] { "Quick Informal Bow", "Greeting While Standing" },
                Actions = new[] { new[] { "Aj Pointing Gesture" }, new[] { "Quick Informal Bow" }, new[] { "Acknowledging Gesture", "Agreeing Yes" } },
                ActionEvery = new Vector2(30f, 60f),
            },
            [Activity.Walk] = new Role
            {
                Idle = Standing, Move = Join(Walks, new[] { "Male Walking With Shopping Bag", "Holding An Object While Walking" }),
                Talk = Conversation, Greet = Greetings, Actions = new[] { Glances, Fidgets },
            },
            [Activity.Idle] = new Role
            {
                Idle = Standing, Move = Walks, Talk = Conversation, Greet = Greetings, Actions = new[] { Glances, Fidgets, Greetings },
                ActionEvery = new Vector2(8f, 18f),
            },
            [Activity.Talk] = new Role
            {
                Idle = Conversation, Move = Walks, Talk = Conversation, Greet = Greetings, Actions = new[] { Reactions, Reactions, Glances },
                ActionEvery = new Vector2(4f, 9f),
            },
            [Activity.Wave] = new Role
            {
                Idle = Standing, Move = Walks, Talk = Conversation, Greet = Greetings, Actions = new[] { Greetings, Reactions },
                ActionEvery = new Vector2(5f, 10f),
            },
            // Bus stops: phones, boredom, looking down the road for the bus.
            [Activity.Wait] = new Role
            {
                Idle = Waiting, Move = Walks, Talk = Conversation, Greet = Greetings,
                Actions = new[] { new[] { "Looking With Hand Shading Eyes", "Looking At Something And Shading Eyes", "Looking Off Into The Distance" },
                                  Glances, Fidgets },
                ActionEvery = new Vector2(6f, 14f),
            },
            // News screens.
            [Activity.Watch] = new Role
            {
                Idle = new[] { "Standing Idle", "Weight Shift Idle", "Legs Crossed, Arms Crossed", "Breathing Idle", "Female Standing Idle" },
                Move = Walks, Talk = Conversation, Greet = Greetings,
                Actions = new[] { new[] { "Thoughtfully Nodding Head Yes", "Nodding Head Yes" }, new[] { "Shaking Head No Thoughtfully" },
                                  new[] { "Laughing Standing", "Aj Pointing Gesture", "Talking Finding Something Funny" } },
                ActionEvery = new Vector2(6f, 14f),
            },
            // Market stall keepers: count, beckon, show produce.
            [Activity.Vendor] = new Role
            {
                Idle = new[] { "Standing Idle", "Breathing Idle", "Weight Shift Idle", "Legs Crossed, Arms Crossed", "Female Standing Idle" },
                Move = Walks, Talk = Conversation, Greet = new[] { "Greeting While Standing", "Waving" },
                Actions = new[] { new[] { "Counting To Five On One Hand", "Pick Up Object And Examine" },
                                  new[] { "Greeting While Standing", "Happy Forward Hand Gesture", "Asking A Question With One Hand" },
                                  new[] { "Acknowledging Gesture", "Agreeing Yes" } },
                ActionEvery = new Vector2(5f, 12f),
            },
            [Activity.Shopper] = new Role
            {
                Idle = Standing,
                Move = new[] { "Male Walking With Shopping Bag", "Holding An Object While Walking", "Walking", "Standard Walk", "Female Walk",
                               "Female Normal Walk", "Feminine Walk Forward" },
                Talk = Conversation, Greet = Greetings,
                Actions = new[] { new[] { "Pick Up Object And Examine", "Picking Up An Object" }, new[] { "Searching Front And Back Pockets", "Counting To Five On One Hand" },
                                  Glances },
                ActionEvery = new Vector2(1.5f, 4f),   // they stop at a stall to look, so act soon after stopping
            },
            [Activity.Stroll] = new Role
            {
                Idle = Standing,
                Move = new[] { "Male Happy Walk", "Happy Walking Forward", "Walking", "Female Walk", "Feminine Walk Forward", "Slow Old Man Shuffle Walk" },
                Talk = Conversation, Greet = Greetings,
                Actions = new[] { new[] { "Looking Off Into The Distance", "Looking With Hand Shading Eyes" },
                                  new[] { "Stretching Arms By Pushing The Elbows", "Big Yawn While Standing" }, Glances },
                ActionEvery = new Vector2(2f, 6f),
            },
            // Football: jog about, the first action is the pass the ball triggers.
            [Activity.Run] = new Role
            {
                Idle = new[] { "Offensive Soccer Idle", "Male Fitness Idle With Breathing", "Breathing Idle" },
                Move = new[] { "Soccer Jog Forward", "Jogging", "Medium Speed Running", "Slow Run Forward", "Forward Jog Dribbling Soccer Ball" },
                Talk = Conversation, Greet = Greetings,
                Actions = new[] { new[] { "Soccer Pass From Idle Pose", "Kicking Up A Soccer Ball" },
                                  new[] { "Soccer Header In Place", "Soccer Idle Chest Receive" }, new[] { "Wiping Sweat Idle" } },
                ActionEvery = new Vector2(3f, 8f),
            },
            [Activity.Keeper] = new Role
            {
                Idle = new[] { "Goalkeeper Idle Without Ball" }, Move = new[] { "Soccer Jog Forward" }, Talk = Conversation, Greet = Greetings,
                Actions = new[] { new[] { "Goalkeeper Left Sidestep", "Goalkeeper Right Sidestep" },
                                  new[] { "Goalkeeper Left Diving Save", "Goalkeeper Right Diving Save", "Goalkeeper Jump Catching Ball High Height" },
                                  new[] { "Goalkeeper Directing The Defensive Players" } },
                ActionEvery = new Vector2(3f, 7f),
            },
            [Activity.Cheer] = new Role
            {
                Idle = new[] { "Happy Idle Variation 1", "Happy Idle Variation 2", "Standing Idle", "Weight Shift Idle" },
                Move = Walks, Talk = Conversation, Greet = Greetings,
                Actions = new[] { new[] { "High Enthusiasm Fist Pump", "Restrained Enthusiasm Standing Fist Pump", "Less Enthusiastic Fist Pump" },
                                  new[] { "Super Excited", "Celebrating After A Win" },
                                  new[] { "Shaking Head No Thoughtfully", "Disappointed _Awe-Shucks_" } },
                ActionEvery = new Vector2(4f, 9f),
            },
            // Club night: a dance loop, now and then a different move or a showpiece.
            [Activity.Dance] = new Role
            {
                Idle = Dances, Move = Walks, Talk = Conversation, Greet = Greetings,
                Actions = new[] { Dances, Dances, Showpieces }, ActionEvery = new Vector2(6f, 14f),
            },
            // Umuganda: pulling weeds, planting, picking up litter.
            [Activity.Work] = new Role
            {
                Idle = new[] { "Standing Pulling Up A Plant", "Kneeling Pulling Up A Plant", "Kneeling Planting A Plant", "Standing Planting A Tree",
                               "Kneeling Digging And Planting Seeds", "Picking Up An Object" },
                Move = Walks, Talk = Conversation, Greet = Greetings,
                Actions = new[] { new[] { "Wiping Sweat Idle" }, new[] { "Stretching Arms By Pushing The Elbows" }, new[] { "Lifting A Heavy Object With Both Hands" } },
                ActionEvery = new Vector2(10f, 20f),
            },
        };

        // ---------- The player (Male_Casual) ----------

        // State name -> clip options. "Act_*" are played by Story Mode while you dwell at a milestone (tea, reading, market).
        public static readonly Dictionary<string, string[]> Player = new Dictionary<string, string[]>
        {
            ["Idle"] = new[] { "Breathing Idle", "Standing Idle" },
            ["Walk"] = new[] { "Walking", "Standard Walk" },
            ["Run"] = new[] { "Standard Running", "Running Forward" },
            ["Seated"] = new[] { "Male Driving A Car" },
            ["Act_Eat"] = new[] { "Male Drinking" },
            ["Act_School"] = new[] { "Thinking While Standing" },
            ["Act_Market"] = new[] { "Pick Up Object And Examine" },
            ["Talk"] = new[] { "General Conversation" },
            ["Wave"] = new[] { "Waving" },
            ["IdleBreak0"] = new[] { "Standing Idle Looking Around" },
            ["IdleBreak1"] = new[] { "Weight Shift Idle" },
            ["IdleBreak2"] = new[] { "Stretching Neck Rolling Side To Side" },
            ["IdleBreak3"] = new[] { "Searching Front And Back Pockets" },
            ["IdleTired"] = new[] { "Big Yawn While Standing" },
        };

        // Player states that play once and return to walking.
        public static readonly HashSet<string> PlayerOneShots = new HashSet<string>
            { "Wave", "IdleBreak0", "IdleBreak1", "IdleBreak2", "IdleBreak3", "IdleTired" };

        // ---------- Import rules ----------

        static HashSet<string> _looped, _locomotion;

        static void BuildRules()
        {
            if (_looped != null)
                return;
            _looped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _locomotion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Roles.Values)
            {
                foreach (var n in r.Idle.Concat(r.Move).Concat(r.Talk))
                    _looped.Add(n);
                foreach (var n in r.Move)
                    _locomotion.Add(n);
            }
            foreach (var kv in Player.Where(kv => !PlayerOneShots.Contains(kv.Key)))
                foreach (var n in kv.Value)
                    _looped.Add(n);
            foreach (var n in Player["Walk"].Concat(Player["Run"]))
                _locomotion.Add(n);
        }

        public static bool IsLooped(string clip) { BuildRules(); return _looped.Contains(clip); }

        // Locomotion keeps its forward travel as root motion (discarded in game, so it plays in place);
        // everything else has the root baked into the pose so people stay on their spot.
        public static bool IsLocomotion(string clip) { BuildRules(); return _locomotion.Contains(clip); }

        public static IEnumerable<string> AllClips() =>
            Roles.Values.SelectMany(r => r.Idle.Concat(r.Move).Concat(r.Talk).Concat(r.Greet).Concat(r.Actions.SelectMany(a => a)))
                 .Concat(Player.Values.SelectMany(v => v))
                 .Distinct(StringComparer.OrdinalIgnoreCase);

        // ---------- Loading ----------

        static readonly Dictionary<string, AnimationClip> Loaded = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache() => Loaded.Clear();

        public static bool Available => AssetDatabase.IsValidFolder(Folder) && Clip(Player["Idle"][0]) != null;

        public static AnimationClip Clip(string name)
        {
            if (Loaded.TryGetValue(name, out var clip) && clip != null)
                return clip;
            clip = AssetDatabase.LoadAllAssetsAtPath($"{Folder}/{name}.fbx").OfType<AnimationClip>()
                                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip != null)
                Loaded[name] = clip;
            return clip;
        }

        // One option, honouring the person's gender where a clip is gendered; null when none is imported.
        public static AnimationClip Pick(string[] options, bool female, System.Random rng)
        {
            if (options == null || options.Length == 0)
                return null;
            bool Fits(string n) => female
                ? !n.StartsWith("Male", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("Man ", StringComparison.OrdinalIgnoreCase)
                : n.IndexOf("Female", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("Feminine", StringComparison.OrdinalIgnoreCase) < 0;
            var pool = options.Where(Fits).Select(Clip).Where(c => c != null).ToList();
            if (pool.Count == 0)
                pool = options.Select(Clip).Where(c => c != null).ToList();
            return pool.Count == 0 ? null : pool[rng.Next(pool.Count)];
        }

        public static AnimationClip PlayerClip(string state) =>
            Player.TryGetValue(state, out var options) ? options.Select(Clip).FirstOrDefault(c => c != null) : null;
    }
}
