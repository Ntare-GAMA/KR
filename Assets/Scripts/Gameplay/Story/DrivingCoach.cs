using KR.Gameplay.Player;
using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // A driving instructor in the passenger seat. While the player drives it looks ahead (signals, STOP signs,
    // speed, side of the road, seatbelt) and says what to do before a rule is broken, so a player who listens never
    // gets a fine. Learners hear everything; once the full licence is earned it only speaks for signals and signs.
    public sealed class DrivingCoach : MonoBehaviour
    {
        public enum Tone { Info, Caution, Warning }

        [SerializeField] RoadRulesMonitor rules;
        [SerializeField] float lookAhead = 70f;
        [SerializeField, Tooltip("Comfortable braking used to judge whether an amber light can be stopped for (m/s²).")]
        float comfortBraking = 5f;

        public string Line { get; private set; }
        public Tone Level { get; private set; }
        public bool Learner { get; set; } = true;

        float _timer;

        public void Configure(RoadRulesMonitor monitor) => rules = monitor;

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f)
                return;
            _timer = 0.2f;
            Line = null;
            if (rules == null || rules.Mode != Travel.Car)
                return;
            Think();
        }

        void Say(string text, Tone tone)
        {
            Line = text;
            Level = tone;
        }

        void Think()
        {
            float kmh = rules.SpeedKmh, ms = kmh / 3.6f;

            if (!rules.SeatbeltOn)
            {
                Say(VrRig.Active ? "Seatbelt first: press Y before you drive." : "Seatbelt first: press B before you drive.", kmh > 5f ? Tone.Warning : Tone.Caution);
                return;
            }
            if (rules.WrongSideNow && Learner)
            {
                Say("Keep right: in Rwanda you drive on the right-hand side of the road.", Tone.Warning);
                return;
            }

            bool hasSignal = rules.NextSignal(out float sigDist, out int state, out float change);
            hasSignal &= sigDist < lookAhead;
            bool hasStop = rules.NextStopSign(out float stopDist, out bool stopped);
            hasStop &= stopDist < lookAhead;

            // Whichever control comes first is the one that matters.
            if (hasStop && (!hasSignal || stopDist < sigDist))
            {
                if (stopped)
                    Say("Good stop. Look left, right and left again, then go when the road is clear.", Tone.Info);
                else if (ms < 0.8f && stopDist < 6f)
                    Say("Good stop. Look both ways, then go when the road is clear.", Tone.Info);
                else
                    Say($"STOP sign in {stopDist:0} m: come to a complete stop at the line, even if the road looks empty.",
                        stopDist < 25f ? Tone.Warning : Tone.Caution);
                return;
            }
            if (hasSignal)
            {
                float stopping = ms * ms / (2f * comfortBraking) + ms * 0.7f;   // braking plus reaction distance
                switch (state)
                {
                    case TrafficSignalSystem.Red:
                        if (ms < 0.5f && sigDist < 15f)
                            Say($"Red: wait behind the white line. Green in about {change:0} s.", Tone.Info);
                        else
                            Say($"Red light in {sigDist:0} m: slow down and stop before the white line.", sigDist < stopping + 10f ? Tone.Warning : Tone.Caution);
                        return;
                    case TrafficSignalSystem.Amber:
                        if (sigDist > stopping)
                            Say("Amber: stop at the line, you have room to stop safely.", Tone.Warning);
                        else
                            Say("Amber: too close to stop safely. Carry on through carefully.", Tone.Caution);
                        return;
                    default:
                        if (change < 4f && sigDist < 45f)
                        {
                            Say("Green, but about to change: ease off and be ready to stop.", Tone.Caution);
                            return;
                        }
                        if (Learner && sigDist < 35f)
                        {
                            Say("Green: you may go. Watch for people still crossing.", Tone.Info);
                            return;
                        }
                        break;
                }
            }

            if (kmh > rules.LimitKmh + 1f)
            {
                Say($"Too fast: {kmh:0} km/h in a {rules.LimitKmh:0} zone. Ease off.", kmh > rules.LimitKmh + 5f ? Tone.Warning : Tone.Caution);
                return;
            }
            if (Learner && kmh > rules.LimitKmh - 4f)
                Say($"You are at the {rules.LimitKmh:0} km/h limit. Hold this speed.", Tone.Info);
        }
    }
}
