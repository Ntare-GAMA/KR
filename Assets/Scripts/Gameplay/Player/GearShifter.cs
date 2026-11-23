using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KR.Gameplay.Player
{
    // The automatic gear lever on the centre console, to the driver's right (owner feedback 2026-11-23: "gear change
    // should appear on the gear shifter"; "the hands in the game should change the gear"). The car model has none, so
    // a simple lever, knob and P R D gate are made here. The lever always shows the selected gear, whatever changed it
    // (R key, right stick click or your hand), and the lit letter on the gate is the gear.
    // In the headset with hand steering on: take the knob with the right grip and push it forward for P, to the
    // middle for R or pull it back for D; the gear changes when the lever reaches that position (only when stopped,
    // otherwise it springs back and the car tells you to stop first).
    public sealed class GearShifter : MonoBehaviour
    {
        // Lever lean (degrees forward) for each gear, and where the console sits from the driver's hip point.
        const float AngleP = 13f, AngleR = 0f, AngleD = -13f;
        const float LeverLength = 0.12f, GrabReach = 0.1f;
        // Over the car model's own (moulded, fixed) gear stick, so the working lever covers it: measured from the body
        // mesh by PlayCheckDrive's console probe (car space, metres: centre of the console, 65 cm up, 25 cm ahead of
        // the car's origin; that stick's knob tops out at 78 cm).
        static readonly Vector3 Mount = new Vector3(-0.01f, 0.65f, 0.25f);

        DrivableCar _car;
        PlayerRig _rig;
        Transform _pivot, _knob;
        Text _gate;
        float _angle = AngleP, _changedAt = -10f;   // starts in P
        bool _held, _wasGrip;
        DrivableCar.GearSel _aimed;

        public bool Held => _held;
        public Transform Knob => _knob;
        // Seconds since the gear last changed (the driver's right hand shows the change when it was not done by hand).
        public float SinceChange => Time.time - _changedAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var rig = Object.FindFirstObjectByType<PlayerRig>();
            if (rig != null && rig.Car != null && rig.Car.GetComponent<GearShifter>() == null)
                rig.Car.gameObject.AddComponent<GearShifter>();
        }

        void Awake()
        {
            _car = GetComponent<DrivableCar>();
            if (_car == null || _car.HipPoint == null)
            {
                enabled = false;
                return;
            }
            Build();
            _car.GearChanged += g => _changedAt = Time.time;
        }

        void Start() => _rig = FindFirstObjectByType<PlayerRig>();

        void Build()
        {
            var car = transform;
            var basePos = car.TransformPoint(Mount);
            var root = new GameObject("Gear shifter").transform;
            root.SetParent(car, false);
            root.SetPositionAndRotation(basePos, car.rotation);

            // Boot, lever and knob: plain shapes in the cabin's dark plastic and leather colours, no colliders.
            Shape(PrimitiveType.Cylinder, root, new Vector3(0f, 0.005f, 0f), new Vector3(0.05f, 0.012f, 0.07f), new Color(0.06f, 0.06f, 0.065f));
            _pivot = new GameObject("Lever pivot").transform;
            _pivot.SetParent(root, false);
            Shape(PrimitiveType.Cylinder, _pivot, new Vector3(0f, LeverLength * 0.5f, 0f), new Vector3(0.022f, LeverLength * 0.5f, 0.022f), new Color(0.12f, 0.12f, 0.13f));
            _knob = Shape(PrimitiveType.Sphere, _pivot, new Vector3(0f, LeverLength, 0f), new Vector3(0.05f, 0.06f, 0.06f), new Color(0.09f, 0.08f, 0.08f));

            // The gate letters beside the lever, facing up and back towards the driver.
            var plate = new GameObject("Gear gate", typeof(RectTransform));
            plate.transform.SetParent(root, false);
            plate.transform.localPosition = new Vector3(0.075f, 0.014f, 0f);
            plate.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            var canvas = plate.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)plate.transform).sizeDelta = new Vector2(60f, 200f);
            plate.transform.localScale = Vector3.one * 0.0009f;
            var go = new GameObject("Letters", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(plate.transform, false);
            _gate = go.AddComponent<Text>();
            _gate.font = KR.Gameplay.UI.KrTheme.Font(KR.Gameplay.UI.KrTheme.Weight.Bold);
            _gate.fontSize = 46;
            _gate.alignment = TextAnchor.MiddleCenter;
            _gate.supportRichText = true;
            _gate.horizontalOverflow = HorizontalWrapMode.Overflow;
            _gate.verticalOverflow = VerticalWrapMode.Overflow;
            var r = _gate.rectTransform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        static Material _lit;

        static Transform Shape(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color colour)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());   // never part of the car's physics
            go.name = type.ToString();
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale * 2f;   // primitives are 1 m across (the cylinder 2 m tall)
            if (type == PrimitiveType.Cylinder)
                go.transform.localScale = new Vector3(scale.x * 2f, scale.y, scale.z * 2f);
            var renderer = go.GetComponent<Renderer>();
            if (_lit == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                _lit = shader != null ? new Material(shader) : renderer.sharedMaterial;
                if (_lit.HasProperty("_Smoothness"))
                    _lit.SetFloat("_Smoothness", 0.35f);
            }
            renderer.sharedMaterial = _lit;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", colour);
            block.SetColor("_Color", colour);
            renderer.SetPropertyBlock(block);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        static float AngleOf(DrivableCar.GearSel g) => g == DrivableCar.GearSel.P ? AngleP : g == DrivableCar.GearSel.R ? AngleR : AngleD;

        void Update()
        {
            if (_pivot == null)
                return;
            float goal = AngleOf(_car.Selected);
            bool hands = _rig != null && _car.Driven && VrRig.Active && Comfort.HandSteering && _rig.VrRightHand != null;
            if (hands && !GamePause.Paused)
                HandInput(ref goal);
            else
                _held = false;
            _angle = Mathf.MoveTowards(_angle, goal, (_held ? 400f : 160f) * Time.deltaTime);
            _pivot.localRotation = Quaternion.Euler(_angle, 0f, 0f);

            var sel = _car.Selected;
            _gate.text = Letter("P", sel == DrivableCar.GearSel.P) + "\n" + Letter("R", sel == DrivableCar.GearSel.R) + "\n" + Letter("D", sel == DrivableCar.GearSel.D);
        }

        static string Letter(string g, bool on) => on ? $"<color=#ff4b4b>{g}</color>" : $"<color=#5a5a5a>{g}</color>";

        void HandInput(ref float goal)
        {
            var hand = _rig.VrRightHand;
            float grip = RigInput.Grip(false);
            bool squeezing = grip > (_held ? 0.35f : 0.6f);
            bool near = (hand.position - _knob.position).sqrMagnitude < GrabReach * GrabReach;
            var rimHand = WheelGrab.Current != null ? WheelGrab.Current.TargetFor(false) : null;
            if (!_held && squeezing && !_wasGrip && near && rimHand == null)
            {
                _held = true;
                _aimed = _car.Selected;
                Buzz(0.3f);
            }
            else if (_held && !squeezing)
                _held = false;
            _wasGrip = grip > 0.6f;
            if (!_held)
                return;

            // The lever follows the hand forward and back (its lean from the pivot), within the gate.
            var local = _pivot.parent.InverseTransformPoint(hand.position);
            float lean = Mathf.Atan2(local.z, Mathf.Max(0.05f, local.y)) * Mathf.Rad2Deg;
            lean = Mathf.Clamp(lean, AngleD - 4f, AngleP + 4f);
            goal = lean;
            var at = lean > (AngleP + AngleR) * 0.5f ? DrivableCar.GearSel.P : lean > (AngleR + AngleD) * 0.5f ? DrivableCar.GearSel.R : DrivableCar.GearSel.D;
            if (at != _aimed)
            {
                _aimed = at;
                if (_car.SelectGear(at))
                    Buzz(0.5f);
            }
            // Refused (moving): the lever stays in the gear you are in.
            if (_car.Selected != at)
                goal = AngleOf(_car.Selected);
        }

        static void Buzz(float strength)
        {
            var d = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (d.isValid)
                d.SendHapticImpulse(0, strength, 0.05f);
        }
    }
}
