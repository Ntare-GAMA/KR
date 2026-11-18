using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // Daily traffic pace: rush hours slow every vehicle down, so jams build where queues already form at signals
    // and junctions. The default curve is a placeholder shape (morning and evening peaks, a midday market bump);
    // replace it with the owner's 15-minute junction counts (story-mode-plan.md section 1.2).
    public sealed class TrafficRhythm : MonoBehaviour
    {
        [SerializeField] RoadNetworkTraffic traffic;
        [SerializeField, Tooltip("Pace factor by Kigali hour (0-24). 1 = free flow.")]
        AnimationCurve paceByHour = DefaultCurve();
        [SerializeField] bool fromCounts;

        public float Pace { get; private set; } = 1f;
        public bool Jammed => Pace < 0.55f;

        public void Configure(RoadNetworkTraffic vehicles) => traffic = vehicles;

        static AnimationCurve DefaultCurve() => new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(5.5f, 0.95f), new Keyframe(7.25f, 0.45f), new Keyframe(8.25f, 0.35f),
            new Keyframe(9.5f, 0.7f), new Keyframe(12.5f, 0.6f), new Keyframe(15f, 0.75f), new Keyframe(17.5f, 0.4f),
            new Keyframe(18.5f, 0.32f), new Keyframe(20f, 0.7f), new Keyframe(22f, 0.95f), new Keyframe(24f, 1f));

        void Update()
        {
            var cycle = DayNightCycle.Instance;
            float hour = cycle != null ? cycle.Hour : KigaliTime.HourOfDay;
            Pace = Mathf.Clamp(paceByHour.Evaluate(hour), 0.15f, 1.2f);
            if (traffic != null)
                traffic.SpeedScale = Pace;
        }
    }
}
