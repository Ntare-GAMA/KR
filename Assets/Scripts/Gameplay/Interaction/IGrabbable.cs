using UnityEngine;

namespace KR.Gameplay.Interaction
{
    // Anything a hand can take hold of: a loose object (Grabbable), a door handle (DoorHandle). HandInteractor
    // only talks to this interface, so new kinds of grabbable things need no change to the hands (open/closed).
    public interface IGrabbable
    {
        bool CanGrab { get; }
        // Where the hand closes on it (for "nearest within reach").
        Vector3 GripPoint { get; }
        void Grab(HandInteractor hand);
        // Every frame while held (LateUpdate, after the hands have moved).
        void HoldUpdate(HandInteractor hand);
        void Release(HandInteractor hand, Vector3 velocity);
    }

    // What drives a hand: a tracked Quest controller, or the keyboard and mouse on a desktop (strategy pattern).
    public interface IHandDriver
    {
        // Is the hand closed (grip squeezed, or the desktop grab toggled on)?
        bool Closed { get; }
        // Desktop hands pick things up at a distance and hold them in front of the camera.
        bool HoldsInFront { get; }
        // Moves the hand transform to this frame's pose.
        void UpdatePose(Transform hand);
        // The nearest grabbable thing the hand can reach this frame, or null.
        IGrabbable FindTarget(Transform hand);
        // A velocity to throw with instead of the hand's own (desktop throw), or null.
        Vector3? ThrowVelocity(Transform hand);
        void Haptic(float amplitude, float seconds);
    }
}
