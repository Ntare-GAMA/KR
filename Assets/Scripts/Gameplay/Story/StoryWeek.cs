using UnityEngine;

namespace KR.Gameplay.Story
{
    // The content of Story Mode: milestones in order, the road rules the monitor enforces, knowledge cards and
    // the authored news lines (generic, local and non-political; written by the team, never scraped).
    [CreateAssetMenu(menuName = "Kigali Rush/Story/Week")]
    public sealed class StoryWeek : ScriptableObject
    {
        public string title = "Rush Week";
        public string[] dayNames = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        public MilestoneDefinition[] milestones;
        public RoadRule[] rules;
        public KnowledgeCard[] cards;
        [TextArea] public string[] news;
        [Tooltip("Placeholder until the Minister's Order gives the real limit (km/h).")] public float urbanSpeedLimitKmh = 40f;
        public bool speedLimitFromOrder;

        public RoadRule Rule(RuleId id)
        {
            if (rules != null)
                foreach (var r in rules)
                    if (r.id == id)
                        return r;
            return null;
        }

        public KnowledgeCard Card(string id)
        {
            if (cards != null && !string.IsNullOrEmpty(id))
                foreach (var c in cards)
                    if (c.id == id)
                        return c;
            return null;
        }
    }
}
