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

    // Same review fields as the quiz records (CLAUDE.md section 9): only a human reviewer approves a card.
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
    }
}
