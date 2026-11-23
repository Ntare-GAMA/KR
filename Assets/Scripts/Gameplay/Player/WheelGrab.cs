using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Player
{
    // Steering with your hands in the headset (owner feedback 2026-11-23: "the user should rotate or spin the wheel,
    // not the controllers"). Squeeze a grip with your hand on the rim and that hand holds the wheel; move it round the
    // rim and the wheel turns with it (both hands: their turns are averaged). Let go and the wheel slowly returns
    // towards straight ahead while the car moves, as a real one does. Press the middle of the wheel (the hub) with
    // either hand for the horn. The controllers keep the pedals (triggers), handbrake, lights, belt and the door.
    // Off in Settings ("Steering: left stick") and on a desktop: nothing here runs and the stick or keys steer.
    public sealed class WheelGrab : MonoBehaviour
    {
        const float Reach = 0.13f;          // how far from the rim a hand can take hold (m)
        const float Depth = 0.16f;          // and how far in front of or behind it
        const float HubRadius = 0.075f;     // the horn pad
        const float GripOn = 0.6f, GripOff = 0.35f;
        const float ReturnRate = 140f;      // wheel degrees per second back to centre at 10 m/s with no hands

        sealed class HandHold
        {
            public bool left, held, wasGrip;
            public Vector3 lastDir;          // the hand's direction from the hub, in the wheel's plane (world)
            public Transform target;          // where the hand model sits on the rim (child of the wheel)
        }

        DrivableCar _car;
        PlayerRig _rig;
        GearShifter _shifter;
        HandHold _l, _r;
        HandHold[] _both;
        float _angle, _radius = 0.18f;
        bool _active;

        public static bool Horn { get; private set; }
        public static WheelGrab Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Horn = false;
            var rig = Object.FindFirstObjectByType<PlayerRig>();
            if (rig != null && rig.Car != null && rig.Car.GetComponent<WheelGrab>() == null)
                rig.Car.gameObject.AddComponent<WheelGrab>();
        }

        void Start()
        {
            Current = this;
            _car = GetComponent<DrivableCar>();
            _rig = FindFirstObjectByType<PlayerRig>();
            _shifter = GetComponent<GearShifter>();
            if (_car == null || _car.SteeringWheel == null)
            {
                enabled = false;
                return;
            }
            if (_car.LeftGrip != null)
                _radius = Vector3.ProjectOnPlane(_car.LeftGrip.position - _car.SteeringWheel.position, _car.SteeringWheel.forward).magnitude;
            _l = new HandHold { left = true, target = new GameObject("Left hand on rim").transform };
            _r = new HandHold { left = false, target = new GameObject("Right hand on rim").transform };
            _both = new[] { _l, _r };
            _l.target.SetParent(_car.SteeringWheel, false);
            _r.target.SetParent(_car.SteeringWheel, false);
        }

        void OnDestroy()
        {
            if (Current == this)
                Current = null;
            Horn = false;
        }

        // Where the hand model goes while it holds the rim; null: the hand follows its controller.
        public Transform TargetFor(bool left)
        {
            var h = left ? _l : _r;
            return _active && h != null && h.held ? h.target : null;
        }

        public bool Active => _active;

        void Update()
        {
            _active = _car != null && _rig != null && _car.Driven && VrRig.Active && Comfort.HandSteering &&
                      _rig.VrLeftHand != null && _rig.VrRightHand != null;
            if (!_active)
            {
                if (_l != null) _l.held = false;
                if (_r != null) _r.held = false;
                Horn = false;
                _angle = 0f;
                if (_car != null)
                    _car.HandWheelAngle = float.NaN;
                return;
            }
            if (GamePause.Paused)
                return;

            var hub = _car.SteeringWheel.position;
            var axis = _car.WheelRest * Vector3.forward;   // towards the driver; does not turn with the wheel
            float turn = 0f;
            int holding = 0;
            foreach (var h in _both)
            {
                var hand = h.left ? _rig.VrLeftHand : _rig.VrRightHand;
                float grip = RigInput.Grip(h.left);
                bool squeezing = grip > (h.held ? GripOff : GripOn);
                bool onShifter = !h.left && _shifter != null && _shifter.Held;
                var rel = hand.position - hub;
                var inPlane = Vector3.ProjectOnPlane(rel, axis);
                if (!h.held && squeezing && !h.wasGrip && !onShifter && inPlane.sqrMagnitude > 0.0025f &&
                    Mathf.Abs(inPlane.magnitude - _radius) < Reach && Mathf.Abs(Vector3.Dot(rel, axis)) < Depth)
                {
                    h.held = true;
                    h.lastDir = inPlane.normalized;
                    Buzz(h.left, 0.25f);
                }
                else if (h.held && (!squeezing || onShifter))
                    h.held = false;
                h.wasGrip = grip > GripOn;

                if (h.held && inPlane.sqrMagnitude > 0.0004f)
                {
                    var dir = inPlane.normalized;
                    // Positive about the axis that points at the driver: clockwise as you see it, a right turn.
                    turn += Vector3.SignedAngle(h.lastDir, dir, axis);
                    h.lastDir = dir;
                    holding++;
                    // The hand model sits on the rim where the hand is.
                    h.target.position = hub + dir * _radius;
                }
            }

            float lockDeg = _car.WheelLock;
            if (holding > 0)
                _angle += turn / holding;
            else
                // Hands off: the wheel comes back towards straight ahead as the car rolls (caster self-centring).
                _angle = Mathf.MoveTowards(_angle, 0f, ReturnRate * Mathf.Clamp01(Mathf.Abs(_car.Speed) / 10f) * Time.deltaTime);
            _angle = Mathf.Clamp(_angle, -lockDeg, lockDeg);
            _car.HandWheelAngle = _angle;

            // Horn: either hand pressed on the middle of the wheel.
            bool horn = false;
            for (int i = 0; i < 2; i++)
            {
                var rel = (i == 0 ? _rig.VrLeftHand : _rig.VrRightHand).position - hub;
                if (Vector3.ProjectOnPlane(rel, axis).magnitude < HubRadius && Mathf.Abs(Vector3.Dot(rel, axis)) < 0.07f)
                    horn = true;
            }
            Horn = horn;
        }

        static void Buzz(bool left, float strength)
        {
            var d = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(left ? UnityEngine.XR.XRNode.LeftHand : UnityEngine.XR.XRNode.RightHand);
            if (d.isValid)
                d.SendHapticImpulse(0, strength, 0.04f);
        }
    }
}
