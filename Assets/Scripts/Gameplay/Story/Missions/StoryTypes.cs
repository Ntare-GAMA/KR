using System;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // Road rules come from Law n° 014/2026 of 06/03/2026 governing road traffic (Official Gazette, Special of
    // 10/03/2026). The law delegates speed limits, signs, the list of faults, fines and demerit points to an
    // Order of the Minister (Articles 5(2), 25(3), 33(2)). Until that Order is supplied, those numbers are
    // game placeholders and the rule says so (fromOrder = false).
    public enum RuleId
    {
        DriveOnRight, ObeyOfficer, RefuseStop, NoLicence, RedSignal, Speeding, HelmetOnMoto, Seatbelt, AlcoholLimit,
        StopSign,   // appended: the values above are serialised by number
        HitObject, HitVehicle, KilledPerson,   // collisions; appended likewise
    }

    [Serializable]
    public sealed class RoadRule
    {
        public RuleId id;
        public string title;
        [Tooltip("Article in the law, e.g. 'Law 014/2026, Art. 4'.")] public string citation;
        [TextArea] public string explanation;
        [Tooltip("Seconds added to the milestone clock when this rule is broken.")] public float timePenalty = 20f;
        public int fineFrw;
        public int demeritPoints;
        [Tooltip("Breaking this rule ends the milestone (e.g. refusing to stop, Art. 39).")] public bool endsRun;
        [Tooltip("True only when the fine and points come from an official text (the law or the Minister's Order); false = game placeholder.")] public bool fromOrder;
    }

    // Same review fields as the quiz records (project brief, section 9): only a human reviewer approves a card.
    [Serializable]
    public sealed class KnowledgeCard
    {
        public string id;
        public string theme;
        public string title;
        [TextArea] public string textEn;
        [TextArea] public string textRw;
        [TextArea] public string textFr;
        public string[] sources;
        public string reviewStatus = "draft";
    }

    public enum Travel { Walk, Moto, Bus, Car }

    public enum MissionKind { Arrive, ParkInBay, Talk, Photo }

    // A place you can discover. Card text is written and reviewed by a person (reviewStatus stays "draft" until then).
    [Serializable]
    public sealed class PlaceRecord
    {
        public string id;
        public string name;
        public string category;
        public string sector;          // Kimironko, Kibagabaga or ALU campus
        public Vector3 position;
        public float radius = 35f;
        public int xp = 50;
        [TextArea] public string cardText;
        public string cardSource;
        public string reviewStatus = "draft";
    }

    // ---------- conversations and quiz (Resources/Story/story_script.json) ----------

    [Serializable]
    public sealed class ScriptFile
    {
        public int version = 1;
        public Conversation[] conversations = new Conversation[0];
        public QuizQuestion[] quiz = new QuizQuestion[0];
    }

    [Serializable]
    public sealed class Conversation
    {
        public string id;
        public string npc;             // NPC id
        public string start;           // first node id
        public DialogueNode[] nodes = new DialogueNode[0];
    }

    [Serializable]
    public sealed class DialogueNode
    {
        public string id;
        public string speaker;         // "npc" or "you"
        public string text;
        public string next;            // next node when there are no choices; empty = end
        public string setFlag;         // optional flag recorded in the save
        public string[] quiz = new string[0];   // optional quiz question ids asked after this line
        public DialogueChoice[] choices = new DialogueChoice[0];
    }

    [Serializable]
    public sealed class DialogueChoice
    {
        public string text;
        public string next;
        public string setFlag;
    }

    // Same review rule as every quiz in the project: draft until a human reviewer approves it.
    [Serializable]
    public sealed class QuizQuestion
    {
        public string id;
        public string text;
        public string[] options = new string[0];
        public int correct;
        public string explanation;
        public string source;
        public string reviewStatus = "draft";
    }

    // ---------- progression ----------

    public static class Badges
    {
        public static readonly (string id, string title, string how)[] All =
        {
            ("first_discovery", "First find", "Discover your first place."),
            ("kimironko", "Kimironko explorer", "Discover every place in Kimironko."),
            ("kibagabaga", "Kibagabaga explorer", "Discover every place in Kibagabaga."),
            ("full_licence", "Full licence", "Drive 3 km in a row without breaking a rule."),
            ("perfect_park", "Parked like a pro", "Pass the parking test."),
            ("road_scholar", "Road scholar", "Answer every road-rule question correctly."),
            ("first_photo", "Photographer", "Take your first photo."),
            ("tour", "Tour guide", "Finish the Kimironko tour."),
            ("clean_week", "Clean week", "Finish Rush Week without a fine."),
        };

        // XP needed for each level (level 1 starts at 0).
        public static readonly int[] Levels = { 0, 150, 400, 800, 1400, 2200, 3200 };

        public static int LevelFor(int xp)
        {
            int level = 1;
            for (int i = 1; i < Levels.Length; i++)
                if (xp >= Levels[i])
                    level = i + 1;
            return level;
        }
    }

    [Serializable]
    public sealed class PhotoRecord
    {
        public string file;
        public string placeId;
        public string missionId;
        public string takenUtc;
    }

    [Serializable]
    public sealed class ComfortSave
    {
        public int turn;               // 0 snap 30, 1 snap 45, 2 smooth
        public int vignette = 2;       // 0 off, 1 low, 2 high (high by default against motion sickness)
        public bool teleport = true;   // walk by teleport instead of the stick (the most comfortable way in VR)
        public int version;            // 1: the comfortable defaults have been applied once to an older save
        public bool largeText;
        public bool seated;            // raises the view for seated play
        public bool stickSteer;        // false: in the headset you turn the wheel and move the gear lever by hand
        public bool asked;             // the comfort picker has been shown once (on the driver's seat)
        public int preset = -1;        // the last comfort preset chosen (-1 none, 0 gentle, 1 balanced, 2 free)
    }

    [Serializable]
    public sealed class MilestoneResult
    {
        public string id;
        public float bestSeconds = -1f;
        public int bestStars;
        public int attempts;
    }

    [Serializable]
    public sealed class StorySave
    {
        public int version = 1;
        public int day;
        public int next;
        public int demeritPoints;
        public int finesFrw;
        public string[] cards = new string[0];
        public MilestoneResult[] results = new MilestoneResult[0];
        // Learner driving: no fines, demerits or police stops until the player has driven cleanLicenceMetres
        // in a row without breaking a rule; then the full licence (and real fines) apply.
        public float learnerCleanMetres;
        public bool fullLicence;
        // Every launch is a new session; the weather changes with each one.
        public int sessions;
        // Progression (every reward has a unique key, so it is never given twice).
        public int xp;
        public string[] rewardKeys = new string[0];
        public string[] discovered = new string[0];
        public string[] badges = new string[0];
        public string[] flags = new string[0];
        public PhotoRecord[] photos = new PhotoRecord[0];
        public ComfortSave comfort = new ComfortSave();
        // A milestone in progress when the app closed resumes where it was.
        public bool inRun;
        public float runElapsed;
        public float runPenalty;
    }
}
