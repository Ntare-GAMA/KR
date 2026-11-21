using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace KR.Gameplay.Player
{
    // One place for player input: keyboard (desktop) and Quest Touch controllers (OpenXR) give the same actions.
    //   Walk: WASD / left stick      Run: Shift / left stick click     Snap turn: right stick (VR only)
    //   Car in/out: E / A            Start milestone: Enter / X        Seatbelt: B key / Y
    //   Accelerate: W / right trigger    Brake: S / left trigger    Gear D/R: R / right stick click
    //   Steer: A,D / left stick    Handbrake: Space / B    Headlights: L / left stick click
    //   Info panel (walking): left menu button / Tab    Full map (desktop): M    Next destination: N / right grip
    //   Car radio volume (driving): right stick up/down / - and +
    public static class RigInput
    {
        static int _frame = -1;
        static bool _use, _confirm, _belt, _wasUse, _wasConfirm, _wasBelt;
        static bool _gear, _lights, _map, _dest, _menu, _wasGear, _wasLights, _wasMap, _wasDest, _wasMenu;
        static bool _shutter, _wasShutter, _navReady = true, _radioReady = true, _indicatorReady = true;
        static int _navX, _navY, _radio, _indicator;

        public static Vector2 Move
        {
            get
            {
                var v = Stick(XRController.leftHand);
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
                }
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        public static bool Run =>
            Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed ||
            Button(XRController.leftHand, "thumbstickClicked", "primary2DAxisClick", "joystickClicked");

        // Right stick x, for snap turning in VR.
        public static float Turn => Stick(XRController.rightHand).x;

        public static float Throttle
        {
            get
            {
                var kb = Keyboard.current;
                float t = Axis(XRController.rightHand, "trigger") - Axis(XRController.leftHand, "trigger");
                if (kb != null)
                {
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed) t += 1f;
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed) t -= 1f;
                }
                return Mathf.Clamp(t, -1f, 1f);
            }
        }

        public static float Steer
        {
            get
            {
                var kb = Keyboard.current;
                float s = Stick(XRController.leftHand).x;
                if (kb != null)
                {
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) s += 1f;
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) s -= 1f;
                }
                return Mathf.Clamp(s, -1f, 1f);
            }
        }

        public static bool Handbrake =>
            Keyboard.current != null && Keyboard.current.spaceKey.isPressed ||
            Button(XRController.rightHand, "secondaryButton");

        // Car pedals, kept apart so the brake only ever brakes (reverse is a gear, not the brake pedal).
        public static float Accelerator
        {
            get
            {
                var kb = Keyboard.current;
                float a = Axis(XRController.rightHand, "trigger");
                if (kb != null && (kb.wKey.isPressed || kb.upArrowKey.isPressed))
                    a = 1f;
                return Mathf.Clamp01(a);
            }
        }

        public static float Brake
        {
            get
            {
                var kb = Keyboard.current;
                float b = Axis(XRController.leftHand, "trigger");
                if (kb != null && (kb.sKey.isPressed || kb.downArrowKey.isPressed))
                    b = 1f;
                return Mathf.Clamp01(b);
            }
        }

        // Gear D/R: R key or right stick click. Headlights: L key or left stick click.
        // Info panel (status, milestone, map) outside the car: left controller menu button (☰) or Tab.
        // Full map on a desktop: M. Next destination: N key or right grip.
        public static bool GearPressed { get { Refresh(); return _gear; } }
        public static bool LightsPressed { get { Refresh(); return _lights; } }
        public static bool MapPressed { get { Refresh(); return _map; } }
        public static bool DestinationPressed { get { Refresh(); return _dest; } }
        public static bool MenuPressed { get { Refresh(); return _menu; } }

        // Photo camera: hold the right grip (C toggles on a desktop); the right trigger (left click) is the shutter.
        public static float RightGrip => Axis(XRController.rightHand, "grip");
        public static bool ShutterPressed { get { Refresh(); return _shutter; } }
        public static bool CameraKeyPressed => Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;
        // Menu navigation on the info panel and in conversations: left stick (arrows / W S A D on a desktop).
        public static int NavX { get { Refresh(); return _navX; } }
        public static int NavY { get { Refresh(); return _navY; } }
        // Car radio volume while driving: right stick up/down, one step per push (- and + keys on a desktop).
        public static int RadioStep { get { Refresh(); return _radio; } }
        // Turn indicators while driving: -1 left, +1 right, one per press (Z / X, or a flick of the right stick).
        public static int IndicatorStep { get { Refresh(); return _indicator; } }

        // How closed each real hand is (grip and trigger squeezed), 0 to 1, for the hand models' fingers.
        public static float HandClosed(bool left)
        {
            var d = left ? XRController.leftHand : XRController.rightHand;
            return Mathf.Clamp01(Mathf.Max(Axis(d, "grip"), Axis(d, "trigger") * 0.7f));
        }

        public static bool UsePressed { get { Refresh(); return _use; } }
        public static bool ConfirmPressed { get { Refresh(); return _confirm; } }
        public static bool BeltPressed { get { Refresh(); return _belt; } }
        // Horn (held): H, or the left grip while driving (both hands are on the wheel).
        public static bool Horn =>
            Keyboard.current != null && Keyboard.current.hKey.isPressed || Axis(XRController.leftHand, "grip") > 0.7f;

        // Edge detection once per frame, so several readers see the same press.
        static void Refresh()
        {
            if (_frame == Time.frameCount)
                return;
            _frame = Time.frameCount;
            var kb = Keyboard.current;
            bool use = Button(XRController.rightHand, "primaryButton");
            bool confirm = Button(XRController.leftHand, "primaryButton");
            bool belt = Button(XRController.leftHand, "secondaryButton");
            _use = kb != null && kb.eKey.wasPressedThisFrame || use && !_wasUse;
            _confirm = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) || confirm && !_wasConfirm;
            _belt = kb != null && kb.bKey.wasPressedThisFrame || belt && !_wasBelt;
            _wasUse = use;
            _wasConfirm = confirm;
            _wasBelt = belt;

            bool gear = Button(XRController.rightHand, "thumbstickClicked", "primary2DAxisClick", "joystickClicked");
            bool lights = Button(XRController.leftHand, "thumbstickClicked", "primary2DAxisClick", "joystickClicked");
            bool menu = Button(XRController.leftHand, "menu", "menuButton", "start");
            bool dest = false;   // destinations are chosen on the info panel's Settings page in VR
            _gear = kb != null && kb.rKey.wasPressedThisFrame || gear && !_wasGear;
            _lights = kb != null && kb.lKey.wasPressedThisFrame || lights && !_wasLights;
            _map = kb != null && kb.mKey.wasPressedThisFrame;
            _menu = kb != null && kb.tabKey.wasPressedThisFrame || menu && !_wasMenu;
            _dest = kb != null && kb.nKey.wasPressedThisFrame || dest && !_wasDest;
            _wasGear = gear;
            _wasLights = lights;
            _wasMenu = menu;

            bool trigger = Axis(XRController.rightHand, "trigger") > 0.7f;
            var mouse = Mouse.current;
            _shutter = trigger && !_wasShutter || mouse != null && mouse.leftButton.wasPressedThisFrame;
            _wasShutter = trigger;

            // One step per stick push (back to centre before the next), plus the keyboard arrows.
            var stick = Stick(XRController.leftHand);
            _navX = _navY = 0;
            if (stick.magnitude < 0.3f)
                _navReady = true;
            else if (_navReady && stick.magnitude > 0.7f)
            {
                _navReady = false;
                if (Mathf.Abs(stick.x) > Mathf.Abs(stick.y)) _navX = stick.x > 0f ? 1 : -1;
                else _navY = stick.y > 0f ? 1 : -1;
            }
            if (kb != null)
            {
                if (kb.upArrowKey.wasPressedThisFrame) _navY = 1;
                if (kb.downArrowKey.wasPressedThisFrame) _navY = -1;
                if (kb.leftArrowKey.wasPressedThisFrame) _navX = -1;
                if (kb.rightArrowKey.wasPressedThisFrame) _navX = 1;
            }
            _wasDest = dest;

            var right = Stick(XRController.rightHand);
            _radio = 0;
            if (Mathf.Abs(right.y) < 0.3f)
                _radioReady = true;
            else if (_radioReady && Mathf.Abs(right.y) > 0.7f && Mathf.Abs(right.y) > Mathf.Abs(right.x))
            {
                _radioReady = false;
                _radio = right.y > 0f ? 1 : -1;
            }
            if (kb != null)
            {
                if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) _radio = -1;
                if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) _radio = 1;
            }

            _indicator = 0;
            if (Mathf.Abs(right.x) < 0.3f)
                _indicatorReady = true;
            else if (_indicatorReady && Mathf.Abs(right.x) > 0.7f && Mathf.Abs(right.x) > Mathf.Abs(right.y))
            {
                _indicatorReady = false;
                _indicator = right.x > 0f ? 1 : -1;
            }
            if (kb != null)
            {
                if (kb.zKey.wasPressedThisFrame) _indicator = -1;
                if (kb.xKey.wasPressedThisFrame) _indicator = 1;
            }
        }

        // Control names differ slightly between OpenXR interaction profiles; try the common ones.
        static Vector2 Stick(InputDevice device)
        {
            if (device == null)
                return Vector2.zero;
            var c = device.TryGetChildControl<Vector2Control>("thumbstick") ??
                    device.TryGetChildControl<Vector2Control>("primary2DAxis") ??
                    device.TryGetChildControl<Vector2Control>("joystick");
            var v = c != null ? c.ReadValue() : Vector2.zero;
            return v.magnitude < 0.15f ? Vector2.zero : v;    // dead zone
        }

        static float Axis(InputDevice device, string name)
        {
            var c = device?.TryGetChildControl<AxisControl>(name);
            return c != null ? c.ReadValue() : 0f;
        }

        static bool Button(InputDevice device, params string[] names)
        {
            if (device == null)
                return false;
            foreach (var n in names)
            {
                var c = device.TryGetChildControl<ButtonControl>(n);
                if (c != null)
                    return c.isPressed;
            }
            return false;
        }
    }
}
