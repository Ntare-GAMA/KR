using System.Collections.Generic;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // A place where breaking a rule is seen: a traffic officer (roundabout control, checkpoint) or a fixed speed
    // camera. Officers stop the driver (Law 014/2026, Art. 6 and 35); cameras only record speeding.
    public sealed class PolicePost : MonoBehaviour
    {
        public enum Kind { Officer, Checkpoint, SpeedCamera }

        [SerializeField] Kind kind = Kind.Officer;
        [SerializeField] float sight = 70f;

        static readonly List<PolicePost> All = new List<PolicePost>();

        public Kind Type => kind;
        public bool CanStop => kind != Kind.SpeedCamera;

        public void Configure(Kind postKind, float sightRange)
        {
            kind = postKind;
            sight = sightRange;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        // The nearest post that sees this position, for this kind of fault (cameras see speeding only).
        public static PolicePost Watching(Vector3 position, RuleId rule)
        {
            PolicePost best = null;
            float bestDist = float.MaxValue;
            foreach (var p in All)
            {
                if (p.kind == Kind.SpeedCamera && rule != RuleId.Speeding)
                    continue;
                float d = Vector3.Distance(p.transform.position, position);
                if (d <= p.sight && d < bestDist)
                {
                    best = p;
                    bestDist = d;
                }
            }
            return best;
        }
    }
}
