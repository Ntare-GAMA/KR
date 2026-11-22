using KR.Gameplay.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KR.Gameplay.Interaction
{
    // Things within reach of a point: the grabbable whose grip point is nearest.
    static class Reach
    {
        static readonly Collider[] Hits = new Collider[24];

        public static IGrabbable Nearest(Vector3 at, float radius)
        {
            int n = Physics.OverlapSphereNonAlloc(at, radius, Hits, ~0, QueryTriggerInteraction.Collide);
            IGrabbable best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var g = Hits[i].GetComponentInParent<IGrabbable>();
                if (g == null || !g.CanGrab)
                    continue;
                float d = (g.GripPoint - at).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = g;
                }
            }
            return best;
        }
    }

    // A Quest Touch controller: the hand follows the tracked controller; squeeze the grip to hold.
    public sealed class VrHandDriver : IHandDriver
    {
        readonly PlayerRig _rig;
        readonly bool _left;
        bool _closed;

        public VrHandDriver(PlayerRig rig, bool left)
        {
            _rig = rig;
            _left = left;
        }

        Transform Source => _left ? _rig.VrLeftHand : _rig.VrRightHand;

        public bool Closed
        {
            get
            {
                float g = RigInput.Grip(_left);
                // Hysteresis: a half-squeezed grip does not flicker between holding and dropping.
                var tune = InteractionTuning.Current;
                _closed = _closed ? g > tune.gripOpenAt : g > tune.gripCloseAt;
                return _closed && _rig.CurrentMode == PlayerRig.Mode.Walking;
            }
        }

        public bool HoldsInFront => false;

        public void UpdatePose(Transform hand)
        {
            var s = Source;
            if (s != null)
                hand.SetPositionAndRotation(s.position, s.rotation);
        }

        public IGrabbable FindTarget(Transform hand) =>
            Source != null && _rig.CurrentMode == PlayerRig.Mode.Walking ? Reach.Nearest(hand.position, InteractionTuning.Current.handReach) : null;

        public Vector3? ThrowVelocity(Transform hand) => null;

        public void Haptic(float amplitude, float seconds)
        {
            var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(_left ? UnityEngine.XR.XRNode.LeftHand : UnityEngine.XR.XRNode.RightHand);
            if (device.isValid)
                device.SendHapticImpulse(0, amplitude, seconds);
        }
    }

    // Keyboard and mouse: look at something within 2.4 m and press G to pick it up (held in front of you), G again
    // to put it down, right-click to throw it.
    public sealed class DesktopHandDriver : IHandDriver
    {
        static readonly RaycastHit[] Hits = new RaycastHit[12];
        readonly PlayerRig _rig;
        bool _holding, _throw;
        int _pressFrame;

        public HandInteractor Hand { get; set; }

        public DesktopHandDriver(PlayerRig rig) => _rig = rig;

        public bool Closed
        {
            get
            {
                var kb = Keyboard.current;
                var mouse = Mouse.current;
                if (kb != null && kb.gKey.wasPressedThisFrame && _pressFrame != Time.frameCount)
                {
                    _pressFrame = Time.frameCount;
                    _holding = Hand == null || Hand.Held == null;
                }
                if (_holding && mouse != null && mouse.rightButton.wasPressedThisFrame && Hand != null && Hand.Held != null)
                {
                    _holding = false;
                    _throw = true;
                }
                // Pressed G with nothing in reach: the hand opens again.
                if (_holding && Hand != null && Hand.Held == null && Time.frameCount > _pressFrame)
                    _holding = false;
                return _holding && _rig.CurrentMode == PlayerRig.Mode.Walking;
            }
        }

        public bool HoldsInFront => true;

        public void UpdatePose(Transform hand)
        {
            var cam = Camera.main;
            if (cam != null)
                hand.SetPositionAndRotation(cam.transform.position + cam.transform.rotation * new Vector3(0.16f, -0.2f, 0.55f), cam.transform.rotation);
        }

        public IGrabbable FindTarget(Transform hand)
        {
            var cam = Camera.main;
            if (cam == null || _rig.CurrentMode != PlayerRig.Mode.Walking)
                return null;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            int n = Physics.SphereCastNonAlloc(ray, 0.06f, Hits, InteractionTuning.Current.desktopReach, ~0, QueryTriggerInteraction.Collide);
            IGrabbable best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var g = Hits[i].collider.GetComponentInParent<IGrabbable>();
                if (g != null && g.CanGrab && Hits[i].distance < bestD)
                {
                    bestD = Hits[i].distance;
                    best = g;
                }
            }
            return best;
        }

        public Vector3? ThrowVelocity(Transform hand)
        {
            if (!_throw)
                return Vector3.zero;   // put down gently
            _throw = false;
            var cam = Camera.main;
            return cam != null ? cam.transform.forward * InteractionTuning.Current.desktopThrowSpeed + Vector3.up * 1.5f : Vector3.zero;
        }

        public void Haptic(float amplitude, float seconds) { }
    }
}
