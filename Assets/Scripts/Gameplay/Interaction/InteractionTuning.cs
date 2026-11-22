using UnityEngine;

namespace KR.Gameplay.Interaction
{
    // Tuning for the hands (all tuning values live in ScriptableObjects). The game loads
    // Resources/InteractionTuning.asset; without it, these defaults apply.
    [CreateAssetMenu(fileName = "InteractionTuning", menuName = "Kigali Rush/Interaction Tuning")]
    public sealed class InteractionTuning : ScriptableObject
    {
        [Header("Headset hands")]
        [Tooltip("How far from the controller a hand can take hold of something (m).")]
        public float handReach = 0.11f;
        [Tooltip("Grip squeeze that closes the hand (0 to 1).")]
        public float gripCloseAt = 0.55f;
        [Tooltip("Grip squeeze below which a closed hand opens again (lower than gripCloseAt, so it does not flicker).")]
        public float gripOpenAt = 0.35f;
        public float grabHapticAmplitude = 0.35f;
        public float grabHapticSeconds = 0.05f;

        [Header("Desktop hand")]
        [Tooltip("How far away you can pick something up by looking at it and pressing G (m).")]
        public float desktopReach = 2.4f;
        [Tooltip("Speed of a right-click throw (m/s).")]
        public float desktopThrowSpeed = 7f;

        [Header("Objects")]
        [Tooltip("Fastest a thrown object can leave the hand (m/s).")]
        public float maxThrowSpeed = 9f;
        [Tooltip("Food within this distance of your mouth is a bite (m).")]
        public float mouthReach = 0.17f;

        [Header("Doors")]
        public float doorMaxAngle = 100f;
        [Tooltip("Pulled this far open (degrees), the door takes you in.")]
        public float doorOpensAt = 40f;

        static InteractionTuning _current;

        public static InteractionTuning Current
        {
            get
            {
                if (_current == null)
                    _current = Resources.Load<InteractionTuning>("InteractionTuning");
                if (_current == null)
                    _current = CreateInstance<InteractionTuning>();
                return _current;
            }
        }
    }
}
