using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

namespace KR.Gameplay.Player
{
    // Headset play (Meta Quest through OpenXR). With no headset active it does nothing and the desktop first-person
    // camera keeps working. In VR: first person in your character's own body (head hidden, hands on the tracked
    // controllers, which are never drawn). Walking: the body stays under your head, also when you step around the
    // room; the stick moves relative to where you look and the right stick snap-turns. In the car your eyes are at
    // the driver's eye point (yaw only, so the car's pitch and roll on slopes never tilt your view) while the
    // character's hands hold the wheel. A vignette narrows the view while moving (comfort).
    public sealed class VrRig : MonoBehaviour
    {
        [SerializeField] PlayerRig rig;
        [SerializeField] Transform cameraOffset;
        [SerializeField] Camera head;
        [SerializeField] TrackedPoseDriver headPose;
        [SerializeField] GameObject leftHand, rightHand;
        [SerializeField] Behaviour desktopCamera;
        [SerializeField] Renderer vignette;
        [SerializeField] float snapAngle = 30f;
        [SerializeField, Tooltip("Eyes sit this far in front of the body's centre.")] float eyeForward = 0.1f;
        [SerializeField, Tooltip("A standing body turns to follow your view past this angle.")] float bodyDeadZone = 50f;

        static bool _active;
        bool _inVr, _seated, _turnReady = true;
        float _vignette;
        Vector3 _seatLocal;           // rig position in the car's yaw-only frame
        float _seatYaw;               // rig yaw relative to the car
        Vector3 _lastPos;
        Vector3 _lastPlayer;
        MaterialPropertyBlock _block;
        Texture _ring;

        // True while a headset is rendering (other scripts switch prompts and HUDs on this).
        public static bool Active => _active;

        public void Configure(PlayerRig playerRig, Transform offset, Camera headCamera, TrackedPoseDriver pose,
                              GameObject left, GameObject right, Behaviour desktop, Renderer comfortVignette)
        {
            rig = playerRig;
            cameraOffset = offset;
            head = headCamera;
            headPose = pose;
            leftHand = left;
            rightHand = right;
            desktopCamera = desktop;
            vignette = comfortVignette;
        }

        void Start()
        {
            SetVr(false);
        }

        void Update()
        {
            bool device = XRSettings.isDeviceActive;
            if (device != _inVr)
                SetVr(device);
        }

        void SetVr(bool on)
        {
            _inVr = on;
            _active = on;
            if (headPose != null)
                headPose.enabled = on;
            if (leftHand != null)
                leftHand.SetActive(on);
            if (rightHand != null)
                rightHand.SetActive(on);
            if (vignette != null)
                vignette.enabled = on;
            if (desktopCamera != null)
                desktopCamera.enabled = !on;
            if (head != null)
            {
                head.nearClipPlane = on ? 0.05f : 0.3f;
                if (on)
                {
                    head.transform.SetParent(cameraOffset, false);
                    head.transform.localPosition = Vector3.zero;
                    head.transform.localRotation = Quaternion.identity;
                }
            }
            if (rig != null)
            {
                rig.VrLeftHand = on && leftHand != null ? leftHand.transform : null;
                rig.VrRightHand = on && rightHand != null ? rightHand.transform : null;
                if (rig.Player != null)
                    rig.Player.FaceDeadZone = on ? bodyDeadZone : 0f;
            }
            if (on)
            {
                var subsystems = new List<XRInputSubsystem>();
                SubsystemManager.GetSubsystems(subsystems);
                foreach (var s in subsystems)
                    s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            }
            _seated = false;
            if (rig != null && rig.Player != null)
                _lastPlayer = rig.Player.transform.position;
        }

        // A short black fade that hides a cut (getting in or out of the car, a teleport step).
        static float _fade, _fadeSpeed = 3f;
        public static void Fade(float seconds)
        {
            _fade = 1f;
            _fadeSpeed = 1f / Mathf.Max(0.05f, seconds);
        }

        void LateUpdate()
        {
            if (!_inVr || rig == null || rig.Player == null)
                return;
            HeadsetPresence();
            if (Comfort.RecenterRequested)
            {
                Comfort.RecenterRequested = false;
                if (_seated)
                    SitDown(rig.Car);
                else
                    transform.RotateAround(head.transform.position, Vector3.up,
                                           Mathf.DeltaAngle(head.transform.eulerAngles.y, rig.Player.transform.eulerAngles.y));
            }
            if (cameraOffset != null)
                cameraOffset.localPosition = Vector3.up * (Comfort.Seated && !_seated ? 0.45f : 0f);
            bool driving = rig.CurrentMode == PlayerRig.Mode.Driving && rig.Car != null;
            if (driving != _seated)
            {
                _seated = driving;
                if (driving)
                    SitDown(rig.Car);
                else
                    _lastPlayer = rig.Player.transform.position;
            }

            if (_seated)
            {
                var seat = rig.Car.Seat;
                var frame = Quaternion.Euler(0f, seat.eulerAngles.y, 0f);
                transform.SetPositionAndRotation(seat.position + frame * _seatLocal, frame * Quaternion.Euler(0f, _seatYaw, 0f));
            }
            else
            {
                KeepBodyUnderHead();
                SnapTurn();
            }
            UpdateVignette();
        }

        // The stick moves the body (and the rig with it); stepping around the room moves the head, and the body
        // follows it, stopping at walls. Either way the eyes end up just in front of the body's centre.
        void KeepBodyUnderHead()
        {
            var player = rig.Player;
            Vector3 p = player.transform.position;
            transform.position += p - _lastPlayer;
            Vector3 fwd = Flat(head.transform.forward);
            Vector3 want = head.transform.position - fwd * eyeForward;
            player.MoveBy(Flat(want - p));
            p = player.transform.position;
            Vector3 residual = Flat(want - p);
            transform.position = new Vector3(transform.position.x - residual.x, p.y, transform.position.z - residual.z);
            _lastPlayer = p;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        // Recentre once on getting in: head at the driver's eye point, facing along the car.
        void SitDown(DrivableCar car)
        {
            var seat = car.Seat;
            var frame = Quaternion.Euler(0f, seat.eulerAngles.y, 0f);
            float headYaw = head.transform.eulerAngles.y;
            transform.RotateAround(head.transform.position, Vector3.up, Mathf.DeltaAngle(headYaw, seat.eulerAngles.y));
            transform.position += car.EyePoint - head.transform.position;
            _seatLocal = Quaternion.Inverse(frame) * (transform.position - seat.position);
            _seatYaw = Mathf.DeltaAngle(seat.eulerAngles.y, transform.eulerAngles.y);
        }

        // Taking the headset off pauses the game (and the car); putting it back on resumes.
        void HeadsetPresence()
        {
            var hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (hmd.isValid && hmd.TryGetFeatureValue(CommonUsages.userPresence, out bool present) && present == GamePause.HeadsetOff)
                GamePause.SetHeadsetOff(!present);
        }

        void SnapTurn()
        {
            if (GamePause.Paused)
                return;
            float x = RigInput.Turn;
            if (Comfort.TurnMode == Comfort.Turn.Smooth)
            {
                if (Mathf.Abs(x) > 0.2f)
                    transform.RotateAround(head.transform.position, Vector3.up, x * 90f * Time.deltaTime);
                return;
            }
            float angle = Comfort.TurnMode == Comfort.Turn.Snap45 ? 45f : snapAngle;
            if (Mathf.Abs(x) < 0.3f)
            {
                _turnReady = true;
                return;
            }
            if (!_turnReady || Mathf.Abs(x) < 0.7f)
                return;
            _turnReady = false;
            transform.RotateAround(head.transform.position, Vector3.up, Mathf.Sign(x) * angle);
        }

        // Darken the edge of the view in proportion to how fast the world moves past.
        void UpdateVignette()
        {
            if (vignette == null)
                return;
            float speed = Time.deltaTime > 0f ? (transform.position - _lastPos).magnitude / Time.deltaTime : 0f;
            _lastPos = transform.position;
            float target = Mathf.Clamp01(speed / (_seated ? 12f : 3f)) * Comfort.VignetteStrength;
            _vignette = Mathf.MoveTowards(_vignette, target, Time.deltaTime * 2.5f);
            _fade = Mathf.MoveTowards(_fade, 0f, Time.unscaledDeltaTime * _fadeSpeed);
            _block ??= new MaterialPropertyBlock();
            _ring ??= vignette.sharedMaterial.GetTexture("_BaseMap");
            vignette.GetPropertyBlock(_block);
            // Fading: the whole view goes black (plain texture); otherwise the ring-shaped comfort vignette.
            _block.SetTexture("_BaseMap", _fade > 0.01f ? Texture2D.whiteTexture : _ring);
            _block.SetColor("_BaseColor", new Color(0f, 0f, 0f, _fade > 0.01f ? Mathf.Max(_fade, _vignette) : _vignette));
            vignette.SetPropertyBlock(_block);
        }

    }
}
