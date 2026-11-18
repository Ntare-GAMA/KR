using UnityEngine;

namespace KR.Gameplay.Player
{
    // Movement and camera tuning for the player (all tuning values live in ScriptableObjects).
    [CreateAssetMenu(fileName = "PlayerTuning", menuName = "Kigali Rush/Player Tuning")]
    public sealed class PlayerTuning : ScriptableObject
    {
        [Header("Walking")]
        public float walkSpeed = 1.9f;
        public float runSpeed = 5.2f;
        public float turnSharpness = 12f;
        public float gravity = 20f;
        [Tooltip("How close the player must be to the car to get in.")]
        public float interactRadius = 3.5f;

        [Header("Desktop camera (replaced by the headset in VR)")]
        public float cameraDistance = 4.2f;
        public float cameraHeight = 1.65f;
        public float mouseSensitivity = 0.12f;
        public float minPitch = -20f;
        public float maxPitch = 65f;

        [Header("Animation")]
        [Tooltip("Seconds standing still before the player shifts weight or looks around.")]
        public float idleBreakSeconds = 9f;
    }
}
