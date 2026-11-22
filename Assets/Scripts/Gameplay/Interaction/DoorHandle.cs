using System;
using System.Collections;
using UnityEngine;

namespace KR.Gameplay.Interaction
{
    // A door you open with your hand: take the handle and pull, and the leaf swings round its hinge with your hand
    // (0 to maxAngle). Swing it past openAt and Opened fires once (BuildingInteriors then takes you in). With a
    // desktop hand (no tracked hand to pull with) taking the handle swings the door open by itself.
    public sealed class DoorHandle : MonoBehaviour, IGrabbable
    {
        [SerializeField] Transform hinge;
        [SerializeField] float sign = 1f;   // which way the leaf swings (+1 or -1 about the hinge's up axis)

        public event Action<DoorHandle> Opened;

        static float maxAngle => InteractionTuning.Current.doorMaxAngle;
        static float openAt => InteractionTuning.Current.doorOpensAt;

        public bool CanGrab => isActiveAndEnabled && !_swinging;
        public Vector3 GripPoint => transform.position;
        public float Angle { get; private set; }

        float _grabOffset;
        bool _fired, _swinging;

        public void Configure(Transform doorHinge, float swingSign)
        {
            hinge = doorHinge;
            sign = swingSign;
        }

        // Back to closed, ready again (the door is reused on another building).
        public void ResetDoor()
        {
            StopAllCoroutines();
            _swinging = false;
            _fired = false;
            Angle = 0f;
            if (hinge != null)
                hinge.localRotation = Quaternion.identity;
        }

        public void Grab(HandInteractor hand)
        {
            if (hand.HoldsInFront)
            {
                StartCoroutine(SwingOpen());
                hand.Drop();
                return;
            }
            _grabOffset = Angle - HandAngle(hand.Position);
        }

        public void HoldUpdate(HandInteractor hand)
        {
            if (hinge == null)
                return;
            float a = Mathf.Clamp(Mathf.DeltaAngle(0f, HandAngle(hand.Position) + _grabOffset), 0f, maxAngle);
            Set(a);
            if (!_fired && a >= openAt)
                hand.Pulse(0.25f, 0.04f);
        }

        public void Release(HandInteractor hand, Vector3 velocity) { }

        // The hand's angle round the hinge, in the swing direction, 0 = closed.
        float HandAngle(Vector3 hand)
        {
            var parent = hinge.parent != null ? hinge.parent : hinge;
            var local = parent.InverseTransformPoint(hand) - hinge.localPosition;
            // Closed, the leaf runs along the hinge's +x; it swings about +y.
            return Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg * sign;
        }

        void Set(float a)
        {
            Angle = a;
            hinge.localRotation = Quaternion.Euler(0f, -a * sign, 0f);
            if (!_fired && a >= openAt)
            {
                _fired = true;
                Opened?.Invoke(this);
            }
        }

        IEnumerator SwingOpen()
        {
            _swinging = true;
            for (float a = Angle; a < 90f; a += Time.deltaTime * 220f)
            {
                Set(a);
                yield return null;
            }
            Set(90f);
            _swinging = false;
        }
    }
}
