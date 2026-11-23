using System.Collections.Generic;
using KR.Gameplay.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KR.Gameplay.Player
{
    // The player in Story Mode: walking and driving in first person, getting in and out of the car, and short
    // on-screen messages. The only visible body is a pair of hands (PlayerHands).
    public sealed class PlayerRig : MonoBehaviour
    {
        public enum Mode { Walking, Driving }

        [SerializeField] PlayerTuning tuning;
        [SerializeField] PlayerMotor player;
        [SerializeField] FirstPersonCamera firstPerson;
        [SerializeField] DrivableCar car;
        [SerializeField] Vector3 home;
        [SerializeField] float homeYaw;

        readonly List<(string text, float until)> _toasts = new List<(string, float)>();
        Vector3 _lastFeet;

        public Mode CurrentMode { get; private set; } = Mode.Walking;
        public PlayerMotor Player => player;
        public DrivableCar Car => car;
        public Vector3 Home => home;
        // Where you start: on the ground beside the car's driver door (worked out when the scene starts).
        public Vector3 StartSpot { get; private set; }
        public float StartYaw { get; private set; }
        public FirstPersonCamera DesktopCamera => firstPerson;
        public string Prompt { get; private set; }
        // A line for the bottom of the screen this frame (things close to your face, like a meal, are hinted here
        // instead of in a bubble right in front of your eyes). Call every frame it should show.
        public void Hint(string text)
        {
            _hint = text;
            _hintFrame = Time.frameCount;
        }
        public string CurrentHint => Time.frameCount - _hintFrame <= 1 ? _hint : null;
        string _hint;
        int _hintFrame = -10;
        public IReadOnlyList<(string text, float until)> Toasts => _toasts;
        // Set by VrRig while a headset is active: the tracked controller poses the hands follow.
        public Transform VrLeftHand { get; set; }
        public Transform VrRightHand { get; set; }
        // Something else owns the controls right now (a conversation, the camera); getting in or out waits.
        public bool Busy { get; set; }

        public void Configure(PlayerTuning settings, PlayerMotor motor, FirstPersonCamera cam, DrivableCar ownCar,
                              Vector3 start, float startYaw)
        {
            tuning = settings;
            player = motor;
            firstPerson = cam;
            car = ownCar;
            home = start;
            homeYaw = startYaw;
        }

        void Start()
        {
            player.gameObject.SetActive(true);
            player.Frozen = false;
            StartSpot = home;
            StartYaw = homeYaw;
            BesideCar();
            player.Teleport(StartSpot, StartYaw);
            _lastFeet = StartSpot;
            StartCoroutine(ReportHeight());
            if (firstPerson != null)
                firstPerson.enabled = true;
            RoadNetworkTraffic.Player = player.transform;
            SetCursor(true);
            if (car != null)
            {
                car.Message += m => Toast(m, 3f);
                var lights = car.GetComponent<CarLights>();
                if (lights != null)
                    lights.Message += m => Toast(m, 2f);
            }
        }

        // The spot next to the driver's door (the left side: Rwanda drives on the right), on the ground as the
        // physics finds it here and now, facing the car. Falls back to the other side, then behind the car.
        void BesideCar()
        {
            if (car == null)
                return;
            var t = car.transform;
            var flatRight = Vector3.ProjectOnPlane(t.right, Vector3.up).normalized;
            var flatFwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            var tries = new[] { -flatRight * 1.9f + flatFwd * 0.4f, flatRight * 1.9f + flatFwd * 0.4f, -flatFwd * 3.6f };
            int mask = ~(1 << 2);
            foreach (var offset in tries)
            {
                var spot = t.position + offset;
                // Down from just above the car's roof: finds the road or yard, not a roof or a tree above it.
                if (!Physics.Raycast(new Vector3(spot.x, t.position.y + 2.5f, spot.z), Vector3.down, out var hit, 8f, mask, QueryTriggerInteraction.Ignore))
                    continue;
                if (hit.collider.attachedRigidbody != null && hit.collider.attachedRigidbody.transform == t)
                    continue;
                var ground = hit.point;
                // Room for a standing person (capsule from ankle to head height).
                if (Physics.CheckCapsule(ground + Vector3.up * 0.4f, ground + Vector3.up * 1.6f, 0.3f, mask, QueryTriggerInteraction.Ignore))
                    continue;
                StartSpot = ground;
                var look = t.position - ground;
                look.y = 0f;
                StartYaw = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look).eulerAngles.y : homeYaw;
                return;
            }
        }

        // A line in the log a few seconds after you appear: feet, eyes and the ground under you, to check the
        // start height on the headset (adb logcat -s Unity).
        System.Collections.IEnumerator ReportHeight()
        {
            yield return new WaitForSecondsRealtime(4f);
            var p = player.transform.position;
            float ground = Physics.Raycast(p + Vector3.up * 1f, Vector3.down, out var hit, 50f, ~(1 << 2), QueryTriggerInteraction.Ignore) ? hit.point.y : float.NaN;
            var cam = Camera.main;
            Debug.Log($"[Start] feet {p:F2}, ground {ground:F2}, eyes {(cam != null ? cam.transform.position.ToString("F2") : "none")}, " +
                      $"car {(car != null ? car.transform.position.ToString("F2") : "none")}, VR {VrRig.Active}, mode {CurrentMode}");
        }

        void Update()
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
                if (_toasts[i].until < Time.unscaledTime)
                    _toasts.RemoveAt(i);

            KeepCarNear();
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                SetCursor(Cursor.lockState != CursorLockMode.Locked);
            if (GamePause.Paused || Busy)
            {
                Prompt = null;
                return;
            }
            bool use = RigInput.UsePressed;

            if (CurrentMode == Mode.Walking)
            {
                bool near = car != null && !car.Driven &&
                            (car.transform.position - player.transform.position).sqrMagnitude < tuning.interactRadius * tuning.interactRadius;
                Prompt = near ? (VrRig.Active ? "Pull the door handle (or A)  Get in the car" : "E  Get in the car  (or G on the door handle)") : null;
                if (near && use)
                    EnterCar();
            }
            else
            {
                Prompt = null;
                if (use)
                {
                    if (Mathf.Abs(car.Speed) > 0.5f)
                        Toast("Stop the car first.");
                    else if (!car.Parked)
                        Toast(!VrRig.Active ? "Shift to P first: press R until P shows."
                            : Comfort.HandSteering ? "Shift to P first: push the gear lever all the way forward." : "Shift to P first: click the right stick until the screen shows P.");
                    else
                        ExitCar();
                }
            }
        }

        // The story sometimes moves you (the next milestone starts somewhere else). The car comes too: if you were
        // moved far on foot and the car is left far behind, it is parked on open ground near where you are now.
        void KeepCarNear()
        {
            if (car == null || CurrentMode != Mode.Walking)
                return;
            var feet = player.transform.position;
            bool jumped = (feet - _lastFeet).sqrMagnitude > 30f * 30f;
            _lastFeet = feet;
            if (!jumped || (car.transform.position - feet).sqrMagnitude < 60f * 60f)
                return;
            if (ParkCarNear(feet, player.transform.eulerAngles.y))
                Toast("Your car is parked right here. It shows on the map as the blue arrow.", 5f);
        }

        // Finds flat open ground 4 to 12 m away with room for the car, and parks it there, facing along yaw.
        public bool ParkCarNear(Vector3 near, float yaw)
        {
            int mask = ~(1 << 2);
            var half = new Vector3(1.05f, 0.65f, 2.45f);
            foreach (float ring in new[] { 5f, 7f, 9.5f, 12f })
                for (int k = 0; k < 12; k++)
                {
                    var dir = Quaternion.Euler(0f, yaw + 90f + k * 30f, 0f) * Vector3.forward;
                    var spot = near + dir * ring;
                    if (!Physics.Raycast(spot + Vector3.up * 4f, Vector3.down, out var hit, 12f, mask, QueryTriggerInteraction.Ignore) ||
                        hit.normal.y < 0.94f || hit.collider.attachedRigidbody != null)
                        continue;
                    // Parallel to the way you face (most likely along the street), on level ground.
                    var fwd = Vector3.ProjectOnPlane(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, hit.normal).normalized;
                    var rot = Quaternion.LookRotation(fwd, hit.normal);
                    bool level = true;
                    foreach (var corner in new[] { new Vector3(-1f, 0f, -2.2f), new Vector3(1f, 0f, -2.2f), new Vector3(-1f, 0f, 2.2f), new Vector3(1f, 0f, 2.2f) })
                    {
                        var c = hit.point + rot * corner;
                        if (!Physics.Raycast(c + Vector3.up * 1.5f, Vector3.down, out var ch, 3f, mask, QueryTriggerInteraction.Ignore) ||
                            Mathf.Abs(ch.point.y - hit.point.y) > 0.35f)
                        {
                            level = false;
                            break;
                        }
                    }
                    if (!level || Physics.CheckBox(hit.point + rot * new Vector3(0f, half.y + 0.3f, 0f), half, rot, mask, QueryTriggerInteraction.Ignore))
                        continue;
                    var rb = car.GetComponent<Rigidbody>();
                    var pos = hit.point + hit.normal * 0.15f;
                    if (rb != null)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        rb.position = pos;
                        rb.rotation = rot;
                    }
                    car.transform.SetPositionAndRotation(pos, rot);
                    Physics.SyncTransforms();
                    return true;
                }
            return false;
        }

        // Getting in from outside without the button (the driver's door pulled open by hand). False if the car is
        // not right here or someone else has the controls.
        public bool GetInCar()
        {
            if (CurrentMode != Mode.Walking || car == null || car.Driven || Busy)
                return false;
            if ((car.transform.position - player.transform.position).sqrMagnitude > (tuning.interactRadius + 1f) * (tuning.interactRadius + 1f))
                return false;
            EnterCar();
            return true;
        }

        void EnterCar()
        {
            VrRig.Fade(0.35f);
            player.EnterSeat(car.Seat);
            car.SetDriven(true);
            RoadNetworkTraffic.Player = car.transform;
            CurrentMode = Mode.Driving;
            Toast(!VrRig.Active ? "W accelerate, S brake, A/D steer, R gear P/D/R, Space handbrake, L lights, E get out (in P)."
                : Comfort.HandSteering ? "Grip the wheel rim and turn it with your hands. Gear lever by your right knee: grip it, pull back for D. Right trigger accelerate, left trigger brake, B handbrake, Y seatbelt, A get out (in P)."
                : "Right trigger accelerate, left trigger brake, left stick steer, right stick click P/D/R gear, B handbrake, left stick click lights, A get out (in P).", 8f);
        }

        void ExitCar()
        {
            VrRig.Fade(0.35f);
            car.SetDriven(false);
            player.LeaveSeat(car.ExitPoint, car.transform.eulerAngles.y);
            RoadNetworkTraffic.Player = player.transform;
            CurrentMode = Mode.Walking;
        }

        public void Toast(string text, float seconds = 4f)
        {
            _toasts.Add((text, Time.unscaledTime + seconds));
            while (_toasts.Count > 3)
                _toasts.RemoveAt(0);
        }

        static void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
