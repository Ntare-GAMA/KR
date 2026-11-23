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
    //   Caught or not, an officer gets out (a copy of the city's patrol officer), walks over and speaks to you.
    // After a crash between cars (AccidentScene) a police car drives to the scene instead, and its officer comes over.
    // Responders set out from out of your sight (behind you or behind buildings) and switch the siren off once they
    // are on the scene; the beacons keep flashing. No siren is ever heard without its vehicle.
    // Both vehicles are copies of the city's own police car and ambulance, made once at the start and reused.
    // Knocking down a person on foot kills them (owner 2026-11-23: "a person, that's killing"): two paramedics (copies
    // of anyone in the city) wheel the stretcher, and a detainee van (a copy of the city's van) comes with the police.
    // Stopped and caught: the officer arrests you and you are taken away in the van; the milestone fails. Driving off
    // first ("if I fail to stop"): the fine for refusing to stop is multiplied, caught later or not.
    public sealed class EmergencyResponse : MonoBehaviour
    {
        const float AmbulanceSpeed = 17f, PoliceTop = 27f, CatchRadius = 25f, LoseDistance = 320f, LoseSeconds = 15f;

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
        Responder _ambulance, _police, _van;
        Transform[] _medics = new Transform[0];
        bool _killed, _fled, _arresting, _vanLeaving;
        float _vanReplan;
        Material _red, _blue, _off;
        readonly List<Vector3> _zebras = new List<Vector3>();
        Vector3 _victim;
        float _ambulanceLeaveAt, _replan, _stoppedFor, _awayFor, _chaseStart, _policeLeaveAt;
        bool _chasing, _crossing, _attending, _leavingPlanned, _closedIn;
        Vector3 _attendAt;
        OfficerOnFoot _officer;
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
            _van = Make(traffic, "Van", "Detainee van (police)") ?? Make(traffic, "Police", "Detainee van (police)");
            _medics = Medics(2);

            _zebras.AddRange(ZebraCentres());

            _officer = OfficerOnFoot.Create(transform);
            CarImpacts.PersonHit += OnPersonHit;
        }

        // A crash between cars: a police car comes to the scene (no chase) and its officer speaks to you.
        public void AttendCrash(Vector3 scene)
        {
            if (_police == null || _police.active)
                return;
            _attendAt = scene;
            _attending = true;
            _chasing = false;
            Dispatch(_police, scene, 250f, 400f);
        }

        void OnDestroy() => CarImpacts.PersonHit -= OnPersonHit;

        // The middle of each zebra crossing. The crossing objects themselves all sit at the map origin (their bars
        // are drawn in world space), so the centre comes from the bars' bounds, not the transform.
        public static List<Vector3> ZebraCentres()
        {
            var centres = new List<Vector3>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (!t.name.StartsWith("Zebra crossing "))
                    continue;
                var rs = t.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0)
                    continue;
                var b = rs[0].bounds;
                foreach (var r in rs)
                    b.Encapsulate(r.bounds);
                centres.Add(b.center);
            }
            return centres;
        }

        // For the Editor's play-mode check: the same response as a real hit at where, and what it is doing now.
        public void Simulate(Vector3 where) => OnPersonHit(where, true);
        public Transform Ambulance => _ambulance != null && _ambulance.active ? _ambulance.body : null;
        public Transform Police => _police != null && _police.active ? _police.body : null;
        public bool AmbulanceWithPatient => _ambulanceLeaveAt > 0f;
        public bool Chasing => _chasing;
        public bool OnCrossing => _crossing;

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
            r.siren = SoundBank.Loop(go.transform, "siren", true, 8f, 140f);   // heard only once the vehicle is close enough to see
            go.SetActive(false);
            // Traffic stops for it (and passes it once it is parked at the scene); ignored while it is hidden.
            RoadNetworkTraffic.AddObstacle(go.transform, 1.1f);
            return r;
        }

        // Two paramedics: copies of people from the city (anyone not in uniform and not a story character), body and
        // animation only, hidden until the ambulance needs them.
        Transform[] Medics(int count)
        {
            var pool = new List<PersonAnimator>();
            foreach (var p in Object.FindObjectsByType<PersonAnimator>(FindObjectsSortMode.None))
            {
                string n = p.name;
                if (n.Contains("olice") || n.Contains("fficer") || p.GetComponent<Npc>() != null ||
                    p.GetComponentInParent<RoadNetworkTraffic>() != null || p.GetComponentInParent<Rigidbody>() != null)
                    continue;
                pool.Add(p);
            }
            var list = new List<Transform>();
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                var template = pool[Random.Range(0, pool.Count)];
                pool.Remove(template);
                var go = Instantiate(template.gameObject, transform);
                go.name = $"Paramedic {i + 1}";
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                    if (!(mb is PersonAnimator))
                        Destroy(mb);
                go.SetActive(false);
                RoadNetworkTraffic.AddObstacle(go.transform, 0.4f);
                list.Add(go.transform);
            }
            if (list.Count == 0)
                Debug.LogWarning("[Emergency] No people in the city to copy as paramedics; the stretcher moves on its own.");
            return list.ToArray();
        }

        void OnPersonHit(Vector3 where, bool onFoot)
        {
            if (_rig == null || _rig.Car == null)
                return;
            Debug.Log($"[Emergency] Person hit at {where:F0} (on foot {onFoot}); ambulance busy {_ambulance != null && _ambulance.active}, police busy {_police != null && _police.active}");
            _victim = where;
            _crossing = false;
            foreach (var z in _zebras)
                if ((z - where).sqrMagnitude < 8f * 8f)
                    _crossing = true;

            if (_ambulance != null && !_ambulance.active)
            {
                Dispatch(_ambulance, where, 160f, 260f);
                _ambulanceLeaveAt = 0f;
            }
            if (_police != null && !_chasing)
            {
                Dispatch(_police, _rig.Car.transform.position, 250f, 400f);
                _chasing = true;
                _attending = false;
                _closedIn = false;
                _chaseStart = Time.time;
                _stoppedFor = _awayFor = 0f;
                _policeLeaveAt = 0f;
                _director?.SetFlag(_crossing ? "hit_on_crossing" : "hit_pedestrian");
            }
            if (onFoot)
            {
                _killed = true;
                _fled = false;
                _arresting = false;
                _director?.SetFlag("killed_person");
                if (_van != null && !_van.active)
                {
                    _vanLeaving = false;
                    Dispatch(_van, _rig.Car.transform.position, 220f, 380f);
                }
                _rig.Toast(_crossing
                    ? "You hit someone on a zebra crossing and killed them. Stop the car: an ambulance, the police and a detainee van are coming."
                    : "You hit someone and killed them. Stop the car: an ambulance, the police and a detainee van are coming.", 7f);
            }
            else
            {
                // A rider knocked off a moto: a crash with a vehicle, so a fine; the police still come.
                _director?.ChargeFine(RuleId.HitVehicle);
                _rig.Toast("An ambulance is on its way, and so are the police. Stop the car.", 6f);
            }
        }

        // Starts a responder on the street network some way from the target and routes it there.
        void Dispatch(Responder r, Vector3 target, float min, float max)
        {
            var graph = _nav != null ? _nav.Graph : null;
            Vector3 start = target + Random.insideUnitSphere.normalized * min;
            if (graph != null && graph.Count > 0)
            {
                // A street node at the right distance that you cannot see from where you are (so the vehicle does
                // not pop into view); any node at the right distance if none is hidden.
                bool found = false, any = false;
                for (int k = 0; k < 800 && !found; k++)
                {
                    var p = graph.Position(Random.Range(0, graph.Count));
                    float d = (p - target).sqrMagnitude;
                    if (d <= min * min || d >= max * max)
                        continue;
                    if (!any)
                    {
                        start = p;
                        any = true;
                    }
                    if (OutOfSight(p))
                    {
                        start = p;
                        found = true;
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

        static bool OutOfSight(Vector3 p)
        {
            var cam = Camera.main;
            if (cam == null)
                return true;
            var eye = cam.transform.position;
            var top = p + Vector3.up * 1.6f;
            if (Vector3.Angle(cam.transform.forward, top - eye) > 75f)
                return true;
            return Physics.Linecast(eye, top, 1 << 0, QueryTriggerInteraction.Ignore);
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
            // Carry on from the route point just past the one nearest the vehicle, never back to a point behind it
            // (re-planning every second and a half used to send it round in circles).
            int nearest = 0;
            float best = float.MaxValue;
            for (int k = 0; k < r.route.Count; k++)
            {
                float d = Flat(r.route[k] - r.body.position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = k;
                }
            }
            r.next = Mathf.Min(nearest + 1, r.route.Count - 1);
            if (r.next < r.route.Count - 1 && Vector3.Dot(Flat(r.route[r.next] - r.body.position), Flat(r.body.forward)) < -0.5f &&
                Flat(r.route[r.next] - r.body.position).magnitude < 12f)
                r.next++;
        }

        static readonly Unity.Profiling.ProfilerMarker ProfileMarker = new Unity.Profiling.ProfilerMarker("KR.Emergency");

        void Update()
        {
            using (ProfileMarker.Auto())
                Tick();
        }

        void Tick()
        {
            float dt = Time.deltaTime;
            Flash(_ambulance);
            Flash(_police);
            UpdateAmbulance(dt);
            UpdatePolice(dt);
            UpdateVan(dt);
            Flash(_van);
            if (_officer != null && _rig != null)
                _officer.Tick(dt, _rig.CurrentMode == PlayerRig.Mode.Driving ? _rig.Car.transform.position : _rig.Player.transform.position);
        }

        // The stretcher comes out of the back of the ambulance, goes to the person on the ground, they are lifted
        // onto it (shown under a blanket) and it goes back in; then the ambulance leaves for the hospital.
        // TODO: paramedic characters (none in the project yet; the stretcher moves on its own).
        System.Collections.IEnumerator PickUp(Responder r, Transform victim)
        {
            var stretcher = Stretcher();
            var body = r.body;
            Vector3 rear = GroundAt(body.position - body.forward * 3.2f);
            Vector3 beside = victim != null ? GroundAt(victim.position + (rear - victim.position).normalized * 0.9f) : _victim;
            var face = Quaternion.LookRotation(Flat(beside - rear) + body.forward * 1e-3f);
            stretcher.blanket.SetActive(false);
            stretcher.root.SetActive(true);
            foreach (var m in _medics)
                if (m != null)
                    m.gameObject.SetActive(true);
            yield return Roll(stretcher.root.transform, rear, beside, face, 3f);
            // The crew either side of the person, looking down at them.
            for (int k = 0; k < _medics.Length; k++)
                if (_medics[k] != null)
                {
                    var side = face * Vector3.right * (k == 0 ? -0.9f : 0.9f);
                    var at = GroundAt(beside + side);
                    _medics[k].SetPositionAndRotation(at, Quaternion.LookRotation(Flat(beside - at) + face * Vector3.forward * 1e-3f));
                }
            yield return new WaitForSeconds(3f);
            if (victim != null)
                CarImpacts.Collect(victim);
            stretcher.blanket.SetActive(true);
            if (_rig != null && (_rig.Player.transform.position - _victim).sqrMagnitude < 80f * 80f)
                _rig.Toast("The paramedics lift them onto the stretcher and cover them.", 5f);
            yield return new WaitForSeconds(1.5f);
            yield return Roll(stretcher.root.transform, beside, rear, face, 3f);
            stretcher.root.SetActive(false);
            foreach (var m in _medics)
                if (m != null)
                    m.gameObject.SetActive(false);
            yield return new WaitForSeconds(2f);
            _ambulanceLeaveAt = Time.time;
        }

        System.Collections.IEnumerator Roll(Transform t, Vector3 from, Vector3 to, Quaternion facing, float seconds)
        {
            var dir = Flat(to - from);
            var walk = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : facing;
            for (float k = 0f; k < 1f; k += Time.deltaTime / seconds)
            {
                var p = GroundAt(Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, k)));
                t.SetPositionAndRotation(p, facing);
                // One paramedic at each end of the stretcher, walking with it.
                for (int i = 0; i < _medics.Length; i++)
                    if (_medics[i] != null)
                        _medics[i].SetPositionAndRotation(GroundAt(p + facing * Vector3.forward * (i == 0 ? 1.25f : -1.25f)), walk);
                yield return null;
            }
            t.SetPositionAndRotation(to, facing);
        }

        sealed class StretcherParts
        {
            public GameObject root, blanket;
        }

        StretcherParts _stretcher;

        // A wheeled stretcher: a padded top on a frame and legs, and a blanket shown once someone is on it.
        StretcherParts Stretcher()
        {
            if (_stretcher != null)
                return _stretcher;
            var root = new GameObject("Ambulance stretcher");
            root.transform.SetParent(transform, false);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Material M(Color c)
            {
                var m = new Material(shader);
                m.SetColor("_BaseColor", c);
                return m;
            }
            var metal = M(new Color(0.75f, 0.77f, 0.8f));
            var pad = M(new Color(0.15f, 0.2f, 0.35f));
            var blanket = M(new Color(0.85f, 0.35f, 0.1f));
            void Part(Transform parent, Material m, Vector3 at, Vector3 size, string name = "Part")
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(parent, false);
                go.transform.localPosition = at;
                go.transform.localScale = size;
                go.GetComponent<Renderer>().sharedMaterial = m;
            }
            Part(root.transform, pad, new Vector3(0f, 0.78f, 0f), new Vector3(0.62f, 0.08f, 1.95f));
            Part(root.transform, metal, new Vector3(0f, 0.7f, 0f), new Vector3(0.58f, 0.05f, 1.9f));
            foreach (float x in new[] { -0.26f, 0.26f })
                foreach (float z in new[] { -0.8f, 0.8f })
                {
                    Part(root.transform, metal, new Vector3(x, 0.35f, z), new Vector3(0.04f, 0.7f, 0.04f));
                    Part(root.transform, metal, new Vector3(x, 0.05f, z), new Vector3(0.05f, 0.1f, 0.1f), "Wheel");
                }
            var cover = new GameObject("Blanket");
            cover.transform.SetParent(root.transform, false);
            Part(cover.transform, blanket, new Vector3(0f, 0.92f, -0.1f), new Vector3(0.52f, 0.2f, 1.45f));
            Part(cover.transform, pad, new Vector3(0f, 0.9f, 0.78f), new Vector3(0.4f, 0.14f, 0.3f), "Pillow");
            root.SetActive(false);
            return _stretcher = new StretcherParts { root = root, blanket = cover };
        }

        static Vector3 GroundAt(Vector3 p) => Floor.At(p, 1.5f, 5f);

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
                    _ambulanceLeaveAt = float.MaxValue;   // with the patient until the stretcher is back in
                    if (r.siren != null) r.siren.Stop();   // on the scene: beacons only
                    if (_rig != null && (_rig.Player.transform.position - _victim).sqrMagnitude < 80f * 80f)
                        _rig.Toast("The ambulance has arrived. Two paramedics bring a stretcher.", 5f);
                    StartCoroutine(PickUp(r, CarImpacts.LastVictim));
                }
            }
            else if (_ambulanceLeaveAt > 0f)
            {
                if (Time.time > _ambulanceLeaveAt)
                {
                    _ambulanceLeaveAt = -1f;
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
                if (_attending)
                {
                    // On the way to a crash; once there: siren off, the officer gets out and comes over.
                    if (Drive(r, 16f, 9f, dt))
                    {
                        _attending = false;
                        if (r.siren != null) r.siren.Stop();
                        bool near = (you - _attendAt).sqrMagnitude < 40f * 40f;
                        _officer?.GetOut(r.body, near ? you : _attendAt, new[]
                        {
                            "Police. Is anyone hurt?",
                            "Your driving licence and your identity card, please.",
                            "Thank you. Move the cars to the side if you can, and exchange your details with the other driver.",
                        }, i =>
                        {
                            if (i == 1 && near)
                                Wallet.Instance?.Show(5f, Wallet.Card.Licence, Wallet.Card.Identity);
                        });
                        _policeLeaveAt = Time.time + 30f;
                        _leavingPlanned = false;
                    }
                    return;
                }
                // Wait for the officer to get back in, then drive off quietly.
                if (_officer != null && _officer.Busy || Time.time < _policeLeaveAt)
                    return;
                if (!_leavingPlanned)
                {
                    _leavingPlanned = true;
                    Plan(r, r.body.position + r.body.forward * 300f);
                }
                if (Drive(r, 12f, 10f, dt))
                    Stand(r);
                return;
            }
            float distance = Vector3.Distance(r.body.position, you);
            float yourSpeed = _rig.CurrentMode == PlayerRig.Mode.Driving ? Mathf.Abs(_rig.Car.Speed) : 0f;
            // Driving away from the person you killed before the police got to you: a hit and run.
            if (_killed && !_fled && Flat(you - _victim).magnitude > 60f)
            {
                _fled = true;
                _director?.SetFlag("hit_and_run");
                _rig.Toast("You drove off after killing someone. The fines are multiplied. Stop for the police.", 6f);
            }

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
            // You can only get away once the police have actually caught up with you (they start out of sight, far off).
            if (distance < LoseDistance * 0.6f)
                _closedIn = true;
            _awayFor = _closedIn && distance > LoseDistance ? _awayFor + dt : 0f;

            if (_stoppedFor > 3f)
            {
                _chasing = false;
                _policeLeaveAt = Time.time + 6f;
                _leavingPlanned = false;
                r.speed = 0f;
                if (r.siren != null) r.siren.Stop();
                _director?.SetFlag("stopped_by_police_after_hit");
                if (_killed)
                {
                    Arrest(r, you);
                    return;
                }
                string said = "Police. Stay where you are. You knocked someone down" + (_crossing ? " on a zebra crossing" : "") +
                              ". The ambulance is with them. You did right to stop.";
                string offence = _crossing ? "Hitting a pedestrian on a zebra crossing" : "Hitting a pedestrian";
                var lines = new[]
                {
                    said,
                    "Your driving licence and your identity card, please.",
                    "Thank you, NTARE Shiloh. I am writing you a ticket for this.",
                    "Here is your ticket. Drive slowly near crossings; people on foot always come first.",
                };
                _officer?.GetOut(r.body, you, lines, i =>
                {
                    if (i == 1)
                        Wallet.Instance?.Show(5f, Wallet.Card.Licence, Wallet.Card.Identity);
                    else if (i == 3)
                    {
                        // The fine is not made up: until the Minister's Order is supplied it stays a placeholder,
                        // as for every other road rule in the game.
                        Wallet.Instance?.Ticket(offence, _nav != null && !string.IsNullOrEmpty(_nav.StreetName) ? _nav.StreetName : "Kimironko",
                                                "to be set by the Minister's Order (placeholder)");
                        _director?.SetFlag("ticket_after_hit");
                    }
                });
                if (_officer == null)
                    _rig.Toast(said, 7f);
            }
            else if (_awayFor > LoseSeconds || (Time.time - _chaseStart > 240f && distance > 180f))
            {
                _chasing = false;
                _policeLeaveAt = Time.time;
                _leavingPlanned = false;
                if (r.siren != null) r.siren.Stop();
                _director?.SetFlag("fled_hit");
                _director?.Rules?.ReportRefusedStop(you);
                if (_killed)
                {
                    _killed = false;
                    _director?.ChargeFine(RuleId.RefuseStop, StoryDirector.LeaveSceneMultiplier,
                                          "You drove off after killing someone and were not caught: the fine is tripled.");
                    SendVanAway();
                }
                _rig.Toast("You got away from the police, for now. Leaving someone you hit and refusing to stop for the police are serious offences.", 7f);
            }
        }

        // ---------- arrest after a killing ----------

        void Arrest(Responder r, Vector3 you)
        {
            _arresting = true;
            string where = _crossing ? " on a zebra crossing" : "";
            var lines = new[]
            {
                $"Police. Stay where you are. You knocked someone down{where} and they have died.",
                "Your driving licence and your identity card, please.",
                "NTARE Shiloh, you are under arrest. Step out of the car.",
                "You are coming with us in the van.",
            };
            if (_officer != null)
                _officer.GetOut(r.body, you, lines, i =>
                {
                    if (i == 1)
                        Wallet.Instance?.Show(5f, Wallet.Card.Licence, Wallet.Card.Identity);
                    else if (i == lines.Length - 1)
                        StartCoroutine(TakenAway(LineWait()));
                });
            else
            {
                _rig.Toast(lines[0] + " " + lines[2], 7f);
                StartCoroutine(TakenAway(4f));
            }
        }

        static float LineWait() => 5f;

        // Waits for the last words and for the van to pull up (at most 25 s), then the cut: you are taken away.
        System.Collections.IEnumerator TakenAway(float words)
        {
            yield return new WaitForSeconds(words);
            float until = Time.time + 25f;
            while (_van != null && _van.active && Time.time < until && _rig != null &&
                   Flat(_van.body.position - (_rig.CurrentMode == PlayerRig.Mode.Driving ? _rig.Car.transform.position : _rig.Player.transform.position)).magnitude > 14f)
                yield return null;
            bool fled = _fled;
            ArrestScreen.Show("ARRESTED",
                "You killed a person with the car. The police take you away in the detainee van." +
                (fled ? "\nYou drove off first: the fine for refusing to stop is tripled." : "") +
                "\nThe milestone is over.", 4f, () =>
                {
                    _officer?.Hide();
                    if (_rig != null)
                        _rig.TakeAway(_rig.StartSpot, _rig.StartYaw);
                    if (fled)
                        _director?.ChargeFine(RuleId.RefuseStop, StoryDirector.LeaveSceneMultiplier, "You drove off after killing someone.");
                    _director?.Arrested();
                    if (_police != null && _police.active)
                        Stand(_police);
                    if (_van != null && _van.active)
                        Stand(_van);
                });
            _killed = _fled = _arresting = false;
        }

        // The van comes to wherever you are; after the arrest (or once you got away) it drives off and is put away.
        void UpdateVan(float dt)
        {
            var r = _van;
            if (r == null || !r.active || _rig == null)
                return;
            if (_vanLeaving)
            {
                if (Drive(r, 14f, 10f, dt))
                    Stand(r);
                return;
            }
            Vector3 you = _rig.CurrentMode == PlayerRig.Mode.Driving ? _rig.Car.transform.position : _rig.Player.transform.position;
            float distance = Flat(r.body.position - you).magnitude;
            _vanReplan -= dt;
            if (_vanReplan <= 0f)
            {
                _vanReplan = 2f;
                if (distance > 40f)
                    Plan(r, you);
                else
                {
                    r.route.Clear();
                    r.next = 0;
                    r.route.Add(you - (you - r.body.position).normalized * 9f);
                }
            }
            if (Drive(r, distance < 30f ? 6f : 22f, 3f, dt) && r.siren != null && r.siren.isPlaying)
                r.siren.Stop();   // pulled up: beacons only
        }

        void SendVanAway()
        {
            if (_van == null || !_van.active)
                return;
            _vanLeaving = true;
            if (_van.siren != null) _van.siren.Stop();
            Plan(_van, _van.body.position + _van.body.forward * 300f);
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
            // Never into another emergency vehicle or your car (owner 2026-11-23: "the police car is passing through
            // the ambulance"): stop a few metres behind whatever is ahead; near the end of the route that counts as
            // arrived, so it parks behind it instead of waiting there for ever.
            float clear = Clearance(r);
            if (clear < 1f)
            {
                r.speed = Mathf.MoveTowards(r.speed, 0f, 14f * dt);
                if (last && remaining < arrive + 14f)
                {
                    r.next = r.route.Count;
                    return true;
                }
                return false;
            }
            // Slow for the end of the route and for sharp turns.
            float target = Mathf.Min(topSpeed, Mathf.Sqrt(2f * 7f * clear));
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

        // Free metres ahead of r before another responder or your car (2.6 m wide corridor, 14 m long, keeping
        // a 5 m gap); float.MaxValue when the way is clear.
        float Clearance(Responder r)
        {
            float room = float.MaxValue;
            var p = r.body.position;
            var f = Flat(r.body.forward).normalized;
            void Check(Transform other, float halfLength)
            {
                if (other == null || other == r.body || !other.gameObject.activeInHierarchy)
                    return;
                var d = Flat(other.position - p);
                float along = Vector3.Dot(d, f);
                float lateral = Mathf.Abs(d.x * f.z - d.z * f.x);
                if (along > 0.5f && along < 14f + halfLength && lateral < 2.6f)
                    room = Mathf.Min(room, along - halfLength - 5f);
            }
            if (_ambulance != null && _ambulance.active) Check(_ambulance.body, 2.5f);
            if (_police != null && _police.active) Check(_police.body, 2.5f);
            if (_van != null && _van.active) Check(_van.body, 2.5f);
            if (_rig != null && _rig.Car != null)
                Check(_rig.Car.transform, 2.3f);
            return room;
        }

        // On the road surface (never on another vehicle), level with it.
        // On the road surface (never on another vehicle or a person), level with it: the ground is sampled under
        // all four wheels, on every layer (roads and kerbs are not all on the default layer), from well above, so
        // the vehicle never sinks into a raised road or pavement.
        void Place(Responder r, Vector3 p, Vector3 forward)
        {
            var flat = Flat(forward);
            var rot = flat.sqrMagnitude > 0.001f ? Quaternion.LookRotation(flat) : r.body.rotation;
            var right = rot * Vector3.right;
            var fwd = rot * Vector3.forward;
            float front = Surface(p + fwd * 1.6f, p.y), back = Surface(p - fwd * 1.6f, p.y);
            float left = Surface(p - right * 0.85f, p.y), rightY = Surface(p + right * 0.85f, p.y);
            float y = Mathf.Max(Surface(p, p.y), (front + back) * 0.5f, (left + rightY) * 0.5f);
            var pitch = Quaternion.FromToRotation(fwd, (p + fwd * 1.6f + Vector3.up * front - (p - fwd * 1.6f + Vector3.up * back)).normalized);
            r.body.SetPositionAndRotation(new Vector3(p.x, y, p.z), Quaternion.Slerp(rot, pitch * rot, 0.8f));
        }

        // The highest walkable surface under p within reach of the current height (2.5 m above, 6 m below).
        static float Surface(Vector3 p, float currentY)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, currentY + 2.5f, p.z), Vector3.down, Hits, 8.5f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider.attachedRigidbody != null || h.normal.y < 0.75f || h.collider is CapsuleCollider || h.collider is CharacterController)
                    continue;
                if (h.point.y > best)
                    best = h.point.y;
            }
            return best > float.MinValue ? best : currentY;
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
