using KR.Core;
using System;
using System.Collections;
using UnityEngine;

namespace KR.Gameplay.Interaction
{
    // A loose object you can pick up, carry and throw (a cup, a book, a ball, food). It is held where your hand
    // closed on it, so it turns with your wrist. Let go and it falls or flies with your swing; with returnHome it
    // glides back to where it was (food goes back on the plate) instead.
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Grabbable : MonoBehaviour, IGrabbable
    {
        [SerializeField] bool returnHome;

        public event Action<Grabbable> Grabbed, Released;

        public bool Held => _hand != null;
        public HandInteractor Hand => _hand;
        public bool CanGrab => isActiveAndEnabled && _hand == null && !_returning;
        public Vector3 GripPoint => (_collider ? _collider : _collider = GetComponentInChildren<Collider>()) is Collider c && c ? c.bounds.center : transform.position;

        Rigidbody _body;
        Collider _collider;
        // Looked up when first needed (Awake does not run for objects made in an Editor test).
        Rigidbody _rb => _body ? _body : _body = GetComponent<Rigidbody>();
        HandInteractor _hand;
        Vector3 _offsetPos, _homePos;
        Quaternion _offsetRot, _homeRot;
        Transform _homeParent;
        bool _wasKinematic, _returning;

        // A grabbable on an existing object (adds a Rigidbody and box collider if it has none).
        public static Grabbable Make(GameObject go, bool returnHome, bool startKinematic)
        {
            if (go.GetComponentInChildren<Collider>() == null)
                FitBox(go);
            var rb = go.GetOrAdd<Rigidbody>();
            rb.isKinematic = startKinematic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var g = go.GetOrAdd<Grabbable>();
            g.returnHome = returnHome;
            return g;
        }

        // A box collider round the object's meshes, in its own space.
        static void FitBox(GameObject go)
        {
            var box = go.AddComponent<BoxCollider>();
            var t = go.transform;
            bool any = false;
            Bounds local = default;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = t.InverseTransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                    else local.Encapsulate(p);
                }
            }
            if (!any)
                local = new Bounds(Vector3.zero, Vector3.one * 0.1f);
            box.center = local.center;
            box.size = Vector3.Max(local.size, Vector3.one * 0.04f);
        }

        void Awake()
        {
            _collider = GetComponentInChildren<Collider>();
            RememberHome();
        }

        // Where it goes back to with returnHome (e.g. after the waiter moves the plate).
        public void RememberHome()
        {
            _homeParent = transform.parent;
            _homePos = transform.localPosition;
            _homeRot = transform.localRotation;
        }

        public void Grab(HandInteractor hand)
        {
            _hand = hand;
            _wasKinematic = _rb.isKinematic;
            _rb.isKinematic = true;
            if (hand.HoldsInFront)
            {
                _offsetPos = Vector3.zero;
                _offsetRot = Quaternion.Inverse(hand.Rotation) * transform.rotation;
            }
            else
            {
                _offsetPos = Quaternion.Inverse(hand.Rotation) * (transform.position - hand.Position);
                _offsetRot = Quaternion.Inverse(hand.Rotation) * transform.rotation;
            }
            transform.SetParent(null, true);
            Grabbed?.Invoke(this);
        }

        public void HoldUpdate(HandInteractor hand)
        {
            var rot = hand.Rotation * _offsetRot;
            var pos = hand.Position + hand.Rotation * _offsetPos;
            _rb.MovePosition(pos);
            _rb.MoveRotation(rot);
            transform.SetPositionAndRotation(pos, rot);
        }

        public void Release(HandInteractor hand, Vector3 velocity)
        {
            _hand = null;
            Released?.Invoke(this);
            if (returnHome)
            {
                StartCoroutine(GoHome());
                return;
            }
            _rb.isKinematic = false;
            _rb.linearVelocity = Vector3.ClampMagnitude(velocity, InteractionTuning.Current.maxThrowSpeed);
        }

        IEnumerator GoHome()
        {
            _returning = true;
            _rb.isKinematic = true;
            transform.SetParent(_homeParent, true);
            Vector3 fromPos = transform.localPosition;
            Quaternion fromRot = transform.localRotation;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                transform.localPosition = Vector3.Lerp(fromPos, _homePos, k);
                transform.localRotation = Quaternion.Slerp(fromRot, _homeRot, k);
                yield return null;
            }
            transform.localPosition = _homePos;
            transform.localRotation = _homeRot;
            _rb.isKinematic = _wasKinematic;
            _returning = false;
        }

        void OnDisable()
        {
            if (_hand != null)
            {
                var h = _hand;
                _hand = null;
                h.ForgetIf(this);
            }
            _returning = false;
        }
    }
}
