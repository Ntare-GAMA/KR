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

        public Mode CurrentMode { get; private set; } = Mode.Walking;
        public PlayerMotor Player => player;
        public DrivableCar Car => car;
        public Vector3 Home => home;
        public FirstPersonCamera DesktopCamera => firstPerson;
        public string Prompt { get; private set; }
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
            player.Teleport(home, homeYaw);
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

        void Update()
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
                if (_toasts[i].until < Time.unscaledTime)
                    _toasts.RemoveAt(i);

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
                Prompt = near ? (VrRig.Active ? "A  Get in the car" : "E  Get in the car") : null;
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
                        Toast(VrRig.Active ? "Shift to P first: click the right stick until the screen shows P." : "Shift to P first: press R until P shows.");
                    else
                        ExitCar();
                }
            }
        }

        void EnterCar()
        {
            VrRig.Fade(0.35f);
            player.EnterSeat(car.Seat);
            car.SetDriven(true);
            RoadNetworkTraffic.Player = car.transform;
            CurrentMode = Mode.Driving;
            Toast(VrRig.Active ? "Right trigger accelerate, left trigger brake, left stick steer, right stick click P/D/R gear, B handbrake, left stick click lights, A get out (in P)."
                              : "W accelerate, S brake, A/D steer, R gear P/D/R, Space handbrake, L lights, E get out (in P).", 7f);
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
