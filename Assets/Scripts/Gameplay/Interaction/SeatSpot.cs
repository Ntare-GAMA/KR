using System.Collections.Generic;
using KR.Gameplay.Player;
using KR.Gameplay.Story;
using KR.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Interaction
{
    // Somewhere you can sit: a chair, a bench, a sofa, a waiting-room seat (owner, 2026-11-22: "i should be able to
    // sit outside"). The transform is the seat: its position is where you sit, its forward is the way you face.
    public sealed class SeatSpot : MonoBehaviour
    {
        static readonly List<SeatSpot> AllSeats = new List<SeatSpot>();
        public static IReadOnlyList<SeatSpot> All => AllSeats;

        public bool Taken { get; set; }
        public event System.Action<SeatSpot> SatDown, StoodUp;

        void OnEnable() => AllSeats.Add(this);
        void OnDisable() => AllSeats.Remove(this);

        internal void RaiseSat() => SatDown?.Invoke(this);
        internal void RaiseStood() => StoodUp?.Invoke(this);

        // A seat on a piece of furniture: at seat height, on the side its back is not, facing away from the back.
        public static SeatSpot Add(Transform furniture, Vector3 localSeat, Vector3 localForward)
        {
            var go = new GameObject("Seat");
            go.transform.SetParent(furniture, false);
            go.transform.localPosition = localSeat;
            go.transform.localRotation = Quaternion.LookRotation(localForward);
            return go.AddComponent<SeatSpot>();
        }
    }

    // Sitting down on the nearest free seat with E (A on the headset) and standing up again with the same button.
    // Restaurants keep their own table service; this covers every other chair, bench and sofa. On scene load,
    // seats are added to the city's benches and outdoor chairs (anything named Bench or Chair).
    public sealed class SeatController : MonoBehaviour
    {
        const float Reach = 1.3f;

        PlayerRig _rig;
        SpeechBubble _prompt;
        SeatSpot _seat, _near;

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
            if (rig == null || rig.GetComponent<SeatController>() != null)
                return;
            rig.gameObject.AddComponent<SeatController>();
            AddCitySeats(scene);
        }

        // Benches and outdoor chairs in the city get a seat each (one per metre and a half of a bench).
        static void AddCitySeats(Scene scene)
        {
            int n = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    string name = t.name.ToLowerInvariant();
                    bool bench = name.Contains("bench");
                    bool chair = name.Contains("chair") && !name.Contains("wheelchair");
                    if (!bench && !chair || t.GetComponentInChildren<SeatSpot>(true) != null || t.parent != null && t.parent.GetComponentInParent<SeatSpot>() != null)
                        continue;
                    // Only whole pieces (the top-most object with that name), with something to see.
                    if (t.parent != null && t.parent.name.ToLowerInvariant().Contains(bench ? "bench" : "chair"))
                        continue;
                    var rs = t.GetComponentsInChildren<Renderer>(true);
                    if (rs.Length == 0)
                        continue;
                    var b = rs[0].bounds;
                    foreach (var r in rs)
                        b.Encapsulate(r.bounds);
                    if (b.size.y < 0.35f || b.size.y > 1.6f || Mathf.Max(b.size.x, b.size.z) > 6f)
                        continue;
                    // The seat faces along the piece's forward, or for a bench across its long side.
                    var fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
                    if (fwd.sqrMagnitude < 0.01f)
                        fwd = Vector3.forward;
                    float seatY = b.min.y;   // the seat transform is on the ground: the seated eye height is measured from it
                    float length = bench ? Mathf.Max(b.size.x, b.size.z) : 0f;
                    var along = Vector3.Cross(Vector3.up, fwd);
                    int seats = bench ? Mathf.Clamp(Mathf.FloorToInt(length / 1.5f), 1, 3) : 1;
                    for (int k = 0; k < seats; k++)
                    {
                        float off = seats == 1 ? 0f : (k - (seats - 1) * 0.5f) * 1.2f;
                        var world = new Vector3(b.center.x, seatY, b.center.z) + along * off + fwd * 0.05f;
                        var go = new GameObject("Seat");
                        go.transform.SetParent(t, true);
                        go.transform.SetPositionAndRotation(world, Quaternion.LookRotation(fwd));
                        go.AddComponent<SeatSpot>();
                        n++;
                    }
                }
            Debug.Log($"[Seats] {n} seats on the city's benches and chairs.");
        }

        void Start()
        {
            _rig = GetComponent<PlayerRig>();
            _prompt = SpeechBubble.Create(transform);
        }

        void Update()
        {
            if (_rig == null)
                return;
            if (_seat != null)
            {
                if (RigInput.UsePressed || _rig.CurrentMode != PlayerRig.Mode.Walking)
                    StandUp();
                return;
            }
            _near = null;
            if (_rig.CurrentMode != PlayerRig.Mode.Walking || _rig.Busy || GamePause.Paused || _rig.Player.Seated)
                return;
            var you = _rig.Player.transform.position;
            float best = Reach * Reach;
            foreach (var s in SeatSpot.All)
            {
                if (s.Taken)
                    continue;
                var d = s.transform.position - you;
                d.y *= 0.3f;
                if (d.sqrMagnitude < best)
                {
                    best = d.sqrMagnitude;
                    _near = s;
                }
            }
            // The car and building doors take E first when they are right here.
            bool carNear = _rig.Car != null && !_rig.Car.Driven && (_rig.Car.transform.position - you).sqrMagnitude < 3.5f * 3.5f;
            if (_near == null || carNear)
                return;
            _prompt.Say(_near.transform, 1.0f, $"<b>{(VrRig.Active ? "A" : "E")}</b>  Sit down", 0.2f);
            if (RigInput.UsePressed)
                SitDown(_near);
        }

        public void SitDown(SeatSpot seat)
        {
            _seat = seat;
            seat.Taken = true;
            _rig.Player.EnterSeat(seat.transform);
            SoundBank.Play("chair", seat.transform.position, 0.6f);
            _rig.Toast($"{(VrRig.Active ? "A" : "E")} to stand up.", 2.5f);
            seat.RaiseSat();
        }

        public void StandUp()
        {
            if (_seat == null)
                return;
            var seat = _seat;
            _seat = null;
            seat.Taken = false;
            var stand = seat.transform.position + seat.transform.forward * 0.6f;
            _rig.Player.LeaveSeat(stand, seat.transform.eulerAngles.y);
            seat.RaiseStood();
        }

        public bool Sitting => _seat != null;
        public SeatSpot Seat => _seat;
    }
}
