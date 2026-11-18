using System.Collections.Generic;
using UnityEngine;

namespace KR.Gameplay.Player
{
    // The player's only visible body: a pair of hands (PolyOne "Free Pack - VR Hands", one rigged mesh with both
    // hands). In the car both hands hold the steering wheel at 9 and 3 and turn with it, fingers closed round the
    // rim. Walking in VR they follow the tracked controllers (never drawn), fingers closing with grip and trigger.
    // Walking on a desktop they are hidden.
    // Each side of the rig (J_Left / J_Right and everything below) is moved as one piece; how the hand is oriented
    // inside that piece is measured from the bones at start, so no hand-tuned rotation offsets are needed.
    public sealed class PlayerHands : MonoBehaviour
    {
        [SerializeField] PlayerRig rig;
        [SerializeField] Renderer handsRenderer;
        [SerializeField, Range(0f, 1f)] float wheelGrip = 0.85f;
        [SerializeField] float fingerCurl = 75f;      // degrees per finger joint when fully closed
        [SerializeField] float thumbCurl = 30f;

        sealed class Side
        {
            public Transform root;                    // J_Left / J_Right, moved as a rigid piece
            public Quaternion basisInRoot;            // the hand's frame (fingers forward, back of hand up) inside root
            public Vector3 palmInRoot;                // palm centre in root space
            public readonly List<(Transform joint, Quaternion rest, Vector3 axis, float max)> fingers =
                new List<(Transform, Quaternion, Vector3, float)>();
            public float closed, target;

            public void SnapFingers()
            {
                closed = target;
                foreach (var (joint, rest, axis, max) in fingers)
                    joint.localRotation = rest * Quaternion.AngleAxis(closed * max, axis);
            }
        }

        Side _left, _right;

        public void Configure(PlayerRig playerRig, Renderer renderer)
        {
            rig = playerRig;
            handsRenderer = renderer;
        }

        void Awake()
        {
            _left = Build("J_Left", true);
            _right = Build("J_Right", false);
            if (handsRenderer is SkinnedMeshRenderer smr)
                smr.updateWhenOffscreen = true;   // bones move far from the mesh's own root
        }

        Side Build(string sideName, bool left)
        {
            Transform Find(string n)
            {
                foreach (var t in GetComponentsInChildren<Transform>(true))
                    if (t.name == n)
                        return t;
                return null;
            }
            var root = Find(sideName);
            var hand = Find($"{sideName}_Hand");
            var middle = Find($"{sideName}_HandMiddle1");
            var index = Find($"{sideName}_HandIndex1");
            var pinky = Find($"{sideName}_HandPinky1");
            var thumbTip = Find($"{sideName}_HandThumb4") ?? Find($"{sideName}_HandThumb3");
            if (root == null || hand == null || middle == null || index == null || pinky == null)
            {
                Debug.LogWarning($"[Hands] {sideName} bones not found; that hand stays where the model put it.");
                return null;
            }

            // Hand frame from the rest pose: fingers point wrist -> middle knuckle; the back of the hand is the
            // side of the knuckle plane away from the thumb tip.
            Vector3 fingers = (middle.position - hand.position).normalized;
            Vector3 across = index.position - pinky.position;
            Vector3 normal = Vector3.Cross(fingers, across).normalized;
            if (thumbTip != null && Vector3.Dot(thumbTip.position - hand.position, normal) > 0f)
                normal = -normal;
            var basisWorld = Quaternion.LookRotation(fingers, normal);   // forward = fingers, up = back of hand

            var s = new Side
            {
                root = root,
                basisInRoot = Quaternion.Inverse(root.rotation) * basisWorld,
                palmInRoot = root.InverseTransformPoint((hand.position + middle.position) * 0.5f),
            };

            // Curl axis: turning a finger about it moves the fingertip towards the palm.
            Vector3 curlWorld = Vector3.Cross(normal, fingers).normalized;
            foreach (var finger in new[] { "Index", "Middle", "Ring", "Pinky", "Thumb" })
                for (int j = 1; j <= 3; j++)
                {
                    var joint = Find($"{sideName}_Hand{finger}{j}");
                    if (joint == null)
                        continue;
                    bool thumb = finger == "Thumb";
                    if (thumb && j == 1)
                        continue;
                    s.fingers.Add((joint, joint.localRotation, Quaternion.Inverse(joint.rotation) * curlWorld, thumb ? thumbCurl : fingerCurl));
                }
            return s;
        }

        // Editor renders (no play mode): hands on the wheel of the given car.
        public void PoseOnWheel(DrivableCar car)
        {
            if (_left == null && _right == null)
                Awake();
            Hold(_left, car.LeftGrip, true, wheelGrip);
            Hold(_right, car.RightGrip, false, wheelGrip);
            _left?.SnapFingers();
            _right?.SnapFingers();
            if (handsRenderer != null)
                handsRenderer.enabled = true;
        }

        void LateUpdate()
        {
            if (rig == null)
                return;
            var car = rig.CurrentMode == PlayerRig.Mode.Driving ? rig.Car : null;
            bool vrWalking = car == null && VrRig.Active && rig.VrLeftHand != null && rig.VrRightHand != null;
            bool show = car != null || vrWalking;
            if (handsRenderer != null && handsRenderer.enabled != show)
                handsRenderer.enabled = show;
            if (!show)
                return;

            if (car != null)
            {
                // On the rim: fingers point away from the driver into the wheel, back of the hand outwards.
                Hold(_left, car.LeftGrip, true, wheelGrip);
                Hold(_right, car.RightGrip, false, wheelGrip);
            }
            else
            {
                Follow(_left, rig.VrLeftHand, true, RigInput.HandClosed(true));
                Follow(_right, rig.VrRightHand, false, RigInput.HandClosed(false));
            }
        }

        // Steering-wheel grip. Grip frames are children of the steering-wheel pivot (local Z towards the driver,
        // local +X towards the car's left), so the hands turn with the wheel.
        void Hold(Side s, Transform grip, bool left, float closed)
        {
            if (s == null || grip == null)
                return;
            var wheel = grip.parent != null ? grip.parent : grip;
            Vector3 fingers = -wheel.forward;
            Vector3 back = left ? wheel.right : -wheel.right;
            Place(s, grip.position, Quaternion.LookRotation(fingers, back), closed);
        }

        // A tracked controller held in a handshake grip: fingers forward, back of the hand outwards.
        void Follow(Side s, Transform controller, bool left, float closed)
        {
            if (s == null || controller == null)
                return;
            Vector3 back = left ? -controller.right : controller.right;
            Place(s, controller.position, Quaternion.LookRotation(controller.forward, back), closed);
        }

        void Place(Side s, Vector3 palm, Quaternion hand, float closed)
        {
            var rootRot = hand * Quaternion.Inverse(s.basisInRoot);
            s.root.SetPositionAndRotation(palm - rootRot * s.palmInRoot, rootRot);
            s.target = closed;
            s.closed = Mathf.MoveTowards(s.closed, closed, Time.deltaTime * 6f);
            foreach (var (joint, rest, axis, max) in s.fingers)
                joint.localRotation = rest * Quaternion.AngleAxis(s.closed * max, axis);
        }
    }
}
