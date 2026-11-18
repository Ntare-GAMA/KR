using UnityEngine;

namespace KR.Gameplay.World
{
    // Passes this body's collisions to whoever listens (the accident scene listens to the player's car).
    public sealed class CollisionRelay : MonoBehaviour
    {
        public event System.Action<Collision> Hit;

        void OnCollisionEnter(Collision collision) => Hit?.Invoke(collision);
    }
}
