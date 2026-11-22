using UnityEngine;

namespace KR.Gameplay.Interaction
{
    // One hand that can grab, hold and throw. Close the hand near something grabbable to take it; open it to let go
    // (thrown with the hand's own speed). Input and reach come from an IHandDriver, so the same hand works with a
    // Quest controller or with the keyboard and mouse. A short buzz confirms each grab.
    public sealed class HandInteractor : MonoBehaviour
    {
        const int VelocityFrames = 5;

        public static HandInteractor Left { get; private set; }
        public static HandInteractor Right { get; private set; }

        public IHandDriver Driver { get; private set; }
        public bool IsLeft { get; private set; }
        public IGrabbable Held { get; private set; }
        public IGrabbable Hover { get; private set; }
        public bool HoldsInFront => Driver.HoldsInFront;
        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;
        public Vector3 Velocity { get; private set; }

        // True while this hand has something in it or is about to take something (other uses of the grip wait).
        public bool Busy => Held != null || Hover != null;

        public static bool AnyHolding => Left != null && Left.Held != null || Right != null && Right.Held != null;

        readonly Vector3[] _history = new Vector3[VelocityFrames];
        readonly float[] _times = new float[VelocityFrames];
        int _cursor;
        bool _wasClosed;

        public static HandInteractor Create(Transform parent, string name, bool left, IHandDriver driver)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var hand = go.AddComponent<HandInteractor>();
            hand.Driver = driver;
            hand.IsLeft = left;
            if (left) Left = hand; else Right = hand;
            return hand;
        }

        void OnDestroy()
        {
            if (Left == this) Left = null;
            if (Right == this) Right = null;
        }

        void Update()
        {
            Driver.UpdatePose(transform);
            TrackVelocity();
            bool closed = Driver.Closed;
            if (Held == null)
            {
                Hover = Driver.FindTarget(transform);
                if (closed && !_wasClosed && Hover != null && Hover.CanGrab)
                {
                    Held = Hover;
                    Hover = null;
                    Held.Grab(this);
                    Driver.Haptic(InteractionTuning.Current.grabHapticAmplitude, InteractionTuning.Current.grabHapticSeconds);
                }
            }
            else if (!closed)
                Drop();
            _wasClosed = closed;
        }

        void LateUpdate()
        {
            if (Held != null)
                Held.HoldUpdate(this);
        }

        // Lets go of whatever is held (also used when the thing itself goes away, e.g. food eaten).
        public void Drop()
        {
            if (Held == null)
                return;
            var item = Held;
            Held = null;
            item.Release(this, Driver.ThrowVelocity(transform) ?? Velocity);
        }

        public void ForgetIf(IGrabbable item)
        {
            if (Held == item) Held = null;
            if (Hover == item) Hover = null;
        }

        public void Pulse(float amplitude, float seconds) => Driver.Haptic(amplitude, seconds);

        // Average speed over the last few frames: a throw uses the swing, not one jittery frame.
        void TrackVelocity()
        {
            _history[_cursor] = transform.position;
            _times[_cursor] = Time.unscaledTime;
            int oldest = (_cursor + 1) % VelocityFrames;
            float dt = _times[_cursor] - _times[oldest];
            Velocity = dt > 1e-4f ? (_history[_cursor] - _history[oldest]) / dt : Vector3.zero;
            _cursor = oldest;
        }
    }
}
