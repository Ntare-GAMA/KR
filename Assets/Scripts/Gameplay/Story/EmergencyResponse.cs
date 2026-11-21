using System.Collections.Generic;
using KR.Gameplay.Player;
using KR.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Story
{
    // When you knock someone down (owner decision 2026-11-21, especially on a zebra crossing): an ambulance sets out
    // at once from a little way off and drives to them with its siren and beacons, stays while they are looked after,
    // then leaves; a police car comes for you, also with siren and beacons, and follows you through the streets.
    //   Stop (and stay stopped) when it reaches you: you are caught. The officer's stop is recorded (flag).
    //   Get far enough away and stay out of reach for a while: you got away, but fleeing is refusing to stop for
    //   the police (the RefuseStop rule), and the story remembers it (flag "fled_hit").
    // Both vehicles are copies of the city's own police car and ambulance, made once at the start and reused.
    public sealed class EmergencyResponse : MonoBehaviour
    {
        const float AmbulanceSpeed = 17f, PoliceTop = 27f, CatchRadius = 12f, LoseDistance = 320f, LoseSeconds = 15f;

        sealed class Responder
        {
            public Transform body;
            public Renderer[] beacons;
            public AudioSource siren;
            public readonly List<Vector3> route = new List<Vector3>();
            public int next;
            public float speed;
            public bool active;
        }

        PlayerRig _rig;
        Navigator _nav;
        StoryDirector _director;
        Responder _ambulance, _police;
        Material _red, _blue, _off;
        readonly List<Vector3> _zebras = new List<Vector3>();
        Vector3 _victim;
        float _ambulanceLeaveAt, _replan, _stoppedFor, _awayFor, _chaseStart, _policeLeaveAt;
        bool _chasing, _crossing;
        static readonly RaycastHit[] Hits = new RaycastHit[8];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var nav = Object.FindFirstObjectByType<Navigator>();
            if (nav != null && nav.GetComponent<EmergencyResponse>() == null)
                nav.gameObject.AddComponent<EmergencyResponse>();
        }

        void Start()
        {
            _nav = GetComponent<Navigator>();
            _rig = Object.FindFirstObjectByType<PlayerRig>();
            _director = Object.FindFirstObjectByType<StoryDirector>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            _red = new Material(shader) { name = "Beacon red" };
            _red.SetColor("_BaseColor", new Color(1f, 0.08f, 0.05f) * 3f);
            _blue = new Material(shader) { name = "Beacon blue" };
            _blue.SetColor("_BaseColor", new Color(0.1f, 0.3f, 1f) * 3f);
            _off = new Material(shader) { name = "Beacon off" };
            _off.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.1f));

            RoadNetworkTraffic traffic = null;
            foreach (var t in Object.FindObjectsByType<RoadNetworkTraffic>(FindObjectsSortMode.None))
                if (!t.IsWalkers)
                    traffic = t;
            _ambulance = Make(traffic, "Ambulance", "Ambulance (responding)");
            _police = Make(traffic, "Police", "Police car (responding)");

            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.StartsWith("Zebra"))
                    _zebras.Add(t.position);

            CarImpacts.PersonHit += OnPersonHit;
        }

        void OnDestroy() => CarImpacts.PersonHit -= OnPersonHit;

        // A copy of the city's own vehicle of that kind, hidden until needed, with beacons on the roof and a siren.
        Responder Make(RoadNetworkTraffic traffic, string kind, string name)
        {
            Transform source = null;
            if (traffic != null)
                for (int i = 0; i < traffic.Count && source == null; i++)
                {
                    var b = traffic.Body(i);
                    if (b != null && b.name.IndexOf(kind, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        source = b;
                }
            if (source == null)
            {
                Debug.LogWarning($"[Emergency] No {kind} in the city's traffic; that response is skipped.");
                return null;
            }
            var go = Instantiate(source.gameObject, transform);
            go.name = name;
            var r = new Responder { body = go.transform };
            var box = go.GetComponent<BoxCollider>();
            Vector3 top = box != null ? box.center + Vector3.up * box.size.y * 0.5f : new Vector3(0f, 1.8f, 0f);
            float half = box != null ? Mathf.Min(0.45f, box.size.x * 0.25f) : 0.4f;
            var beacons = new List<Renderer>();
            foreach (float side in new[] { -1f, 1f })
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(b.GetComponent<Collider>());
                b.name = "Beacon";
                b.transform.SetParent(go.transform, false);
                b.transform.localPosition = top + new Vector3(side * half, 0.07f, 0f);
                b.transform.localScale = new Vector3(0.3f, 0.12f, 0.2f);
                beacons.Add(b.GetComponent<Renderer>());
            }
            r.beacons = beacons.ToArray();
            r.siren = SoundBank.Loop(go.transform, "siren", true, 8f, 220f);
            go.SetActive(false);
            return r;
        }

        void OnPersonHit(Vector3 where, bool onFoot)
        {
            if (_rig == null || _rig.Car == null)
                return;
            _victim = where;
            _crossing = false;
            foreach (var z in _zebras)
                if ((z - where).sqrMagnitude < 8f * 8f)
                    _crossing = true;

            if (_ambulance != null && !_ambulance.active)
            {
                Dispatch(_ambulance, where, 140f, 240f);
                _ambulanceLeaveAt = 0f;
            }
            if (_police != null && !_chasing)
            {
                Dispatch(_police, _rig.Car.transform.position, 180f, 280f);
                _chasing = true;
                _chaseStart = Time.time;
                _stoppedFor = _awayFor = 0f;
                _policeLeaveAt = 0f;
                _director?.SetFlag(_crossing ? "hit_on_crossing" : "hit_pedestrian");
            }
            _rig.Toast(_crossing
                ? "You hit someone on a zebra crossing. An ambulance is on its way, and so are the police. Stop the car."
                : "An ambulance is on its way, and so are the police. Stop the car.", 6f);
        }

        // Starts a responder on the street network some way from the target and routes it there.
        void Dispatch(Responder r, Vector3 target, float min, float max)
        {
            var graph = _nav != null ? _nav.Graph : null;
            Vector3 start = target + Random.insideUnitSphere.normalized * min;
            if (graph != null && graph.Count > 0)
            {
                for (int k = 0; k < 400; k++)
                {
                    var p = graph.Position(Random.Range(0, graph.Count));
                    float d = (p - target).sqrMagnitude;
                    if (d > min * min && d < max * max)
                    {
                        start = p;
                        break;
                    }
                }
            }
            r.body.gameObject.SetActive(true);
            Place(r, start, (target - start).normalized);
            Plan(r, target);
            r.speed = 0f;
            r.active = true;
            if (r.siren != null)
            {
                r.siren.volume = 0.9f;
                if (!r.siren.isPlaying)
                    r.siren.Play();
            }
        }

        void Plan(Responder r, Vector3 target)
        {
            r.route.Clear();
            r.next = 0;
            var graph = _nav != null ? _nav.Graph : null;
            if (graph != null)
            {
                // Emergency vehicles may use any street, either way.
                var path = graph.Route(graph.Nearest(r.body.position), graph.Nearest(target), false);
                if (path != null)
                    r.route.AddRange(path);
            }
            r.route.Add(target);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Flash(_ambulance);
            Flash(_police);
            UpdateAmbulance(dt);
            UpdatePolice(dt);
        }

        // 0: on the way; >0: with the patient until that time; -1: driving away.
        void UpdateAmbulance(float dt)
        {
            var r = _ambulance;
            if (r == null || !r.active)
                return;
            if (_ambulanceLeaveAt == 0f)
            {
                if (Drive(r, AmbulanceSpeed, 7f, dt))
                {
                    _ambulanceLeaveAt = Time.time + 30f;
                    if (r.siren != null) r.siren.volume = 0.25f;
                    if (_rig != null && (_rig.Player.transform.position - _victim).sqrMagnitude < 80f * 80f)
                        _rig.Toast("The ambulance has arrived. The paramedics are looking after them.", 5f);
                }
            }
            else if (_ambulanceLeaveAt > 0f)
            {
                if (Time.time > _ambulanceLeaveAt)
                {
                    _ambulanceLeaveAt = -1f;
                    if (r.siren != null) r.siren.volume = 0.9f;
                    Plan(r, _victim + r.body.forward * 400f);
                }
            }
            else if (Drive(r, AmbulanceSpeed, 10f, dt))
                Stand(r);
        }

        void UpdatePolice(float dt)
        {
            var r = _police;
            if (r == null || !r.active || _rig == null || _rig.Car == null)
                return;
            Vector3 you = _rig.CurrentMode == PlayerRig.Mode.Driving ? _rig.Car.transform.position : _rig.Player.transform.position;
            if (!_chasing)
            {
                // After the stop: wait a little, then drive off.
                if (Time.time > _policeLeaveAt && Drive(r, 12f, 10f, dt))
                    Stand(r);
                return;
            }
            float distance = Vector3.Distance(r.body.position, you);
            float yourSpeed = _rig.CurrentMode == PlayerRig.Mode.Driving ? Mathf.Abs(_rig.Car.Speed) : 0f;

            // Re-plan towards you every second and a half; close in directly once near.
            _replan -= dt;
            if (_replan <= 0f)
            {
                _replan = 1.5f;
                if (distance > 40f)
                    Plan(r, you);
                else
                {
                    r.route.Clear();
                    r.next = 0;
                    r.route.Add(you - (you - r.body.position).normalized * 7f);
                }
            }
            float top = Mathf.Clamp(yourSpeed + 7f, 14f, PoliceTop);
            Drive(r, distance < 10f ? 0f : top, 2f, dt);

            if (distance < CatchRadius && yourSpeed < 1.5f)
                _stoppedFor += dt;
            else
                _stoppedFor = Mathf.Max(0f, _stoppedFor - dt);
            _awayFor = distance > LoseDistance ? _awayFor + dt : 0f;

            if (_stoppedFor > 3f)
            {
                _chasing = false;
                _policeLeaveAt = Time.time + 25f;
                if (r.siren != null) r.siren.volume = 0.2f;
                _director?.SetFlag("stopped_by_police_after_hit");
                _rig.Toast("Police: \"Stay where you are. You knocked someone down" + (_crossing ? " on a zebra crossing" : "") +
                           ". The ambulance is with them. You did right to stop.\"", 7f);
                Plan(r, r.body.position + r.body.forward * 300f);
            }
            else if (_awayFor > LoseSeconds || (Time.time - _chaseStart > 240f && distance > 180f))
            {
                _chasing = false;
                _policeLeaveAt = Time.time;
                if (r.siren != null) r.siren.volume = 0f;
                _director?.SetFlag("fled_hit");
                _director?.Rules?.ReportRefusedStop(you);
                _rig.Toast("You got away from the police, for now. Leaving someone you hit and refusing to stop for the police are serious offences.", 7f);
                Plan(r, r.body.position + r.body.forward * 300f);
            }
        }

        // Moves along the route at up to topSpeed; true once at the end (within arrive metres of it).
        bool Drive(Responder r, float topSpeed, float arrive, float dt)
        {
            if (r.route.Count == 0)
                return true;
            while (r.next < r.route.Count - 1 && Flat(r.route[r.next] - r.body.position).magnitude < 4f)
                r.next++;
            Vector3 goal = r.route[Mathf.Min(r.next, r.route.Count - 1)];
            Vector3 to = Flat(goal - r.body.position);
            bool last = r.next >= r.route.Count - 1;
            float remaining = to.magnitude;
            if (last && remaining < arrive)
            {
                r.speed = Mathf.MoveTowards(r.speed, 0f, 12f * dt);
                r.next = r.route.Count;
                return true;
            }
            // Slow for the end of the route and for sharp turns.
            float target = topSpeed;
            if (last)
                target = Mathf.Min(target, Mathf.Sqrt(2f * 6f * Mathf.Max(0f, remaining - arrive)));
            float turn = Vector3.Angle(Flat(r.body.forward), to);
            target *= Mathf.Lerp(1f, 0.4f, Mathf.InverseLerp(20f, 90f, turn));
            r.speed = Mathf.MoveTowards(r.speed, target, (r.speed < target ? 6f : 10f) * dt);
            if (to.sqrMagnitude > 0.01f)
            {
                var dir = Vector3.RotateTowards(Flat(r.body.forward).normalized, to.normalized, Mathf.Deg2Rad * 120f * dt, 0f);
                Place(r, r.body.position + dir * r.speed * dt, dir);
            }
            return false;
        }

        // On the road surface (never on another vehicle), level with it.
        void Place(Responder r, Vector3 p, Vector3 forward)
        {
            float y = p.y;
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 3f, p.z), Vector3.down, Hits, 9f, 1 << 0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
                if (Hits[i].collider.attachedRigidbody == null && Hits[i].normal.y > 0.75f && Hits[i].distance < best)
                {
                    best = Hits[i].distance;
                    y = Hits[i].point.y;
                }
            var flat = Flat(forward);
            r.body.SetPositionAndRotation(new Vector3(p.x, y, p.z), flat.sqrMagnitude > 0.001f ? Quaternion.LookRotation(flat) : r.body.rotation);
        }

        void Stand(Responder r)
        {
            r.active = false;
            if (r.siren != null)
                r.siren.Stop();
            r.body.gameObject.SetActive(false);
        }

        void Flash(Responder r)
        {
            if (r == null || !r.active || r.beacons == null)
                return;
            bool a = Mathf.Repeat(Time.time * 2.5f, 1f) < 0.5f;
            r.beacons[0].sharedMaterial = a ? _red : _off;
            r.beacons[1].sharedMaterial = a ? _off : _blue;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
