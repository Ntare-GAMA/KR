using UnityEngine;

namespace KR.Gameplay.Story
{
    // One timed goal in Rush Week: get to a place before the clock runs out, optionally stay there for a while
    // (drink tea, read, attend class). The par time is the target; beating it earns stars.
    [CreateAssetMenu(menuName = "Kigali Rush/Story/Milestone")]
    public sealed class MilestoneDefinition : ScriptableObject
    {
        public string id;
        public string title;
        [TextArea] public string brief;
        public string placeName;
        [Tooltip("Optional: the player is moved here when the milestone starts (e.g. waking up at home).")]
        public bool startAtPoint;
        public Vector3 startPoint;
        public Vector3 position;
        public float radius = 18f;
        [Tooltip("0 = Monday.")] public int day;
        [Tooltip("Kigali hour the milestone becomes available (story clock).")] public float opensAtHour = 7f;
        [Tooltip("Seconds of real time to reach the place, including travel.")] public float parSeconds = 240f;
        [Tooltip("Seconds the player must stay inside the radius once there (0 = arrive only).")] public float dwellSeconds;
        public string dwellLabel;
        [Tooltip("Animator state played while dwelling, e.g. Act_Eat; skipped if the character lacks it.")] public string dwellAnimation;
        public string knowledgeCardId;
        [TextArea] public string[] arrivalLines;
        public string speaker;
    }
}
