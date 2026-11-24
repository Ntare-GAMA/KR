using System.Collections.Generic;
using KR.Gameplay.Player;
using KR.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Story
{
    // When you knock someone down: an ambulance sets out at once from a little way off and drives to them with its
    // siren and beacons, stays while they are looked after, then leaves; a police car comes for you, also with siren
    // and beacons, and follows you through the streets.
    //   Stop (and stay stopped) when it reaches you: you are caught. The officer's stop is recorded (flag).
    //   Get far enough away and stay out of reach for a while: you got away, but fleeing is refusing to stop for
    //   the police (the RefuseStop rule), and the story remembers it (flag "fled_hit").
    //   Caught or not, an officer gets out (a copy of the city's patrol officer), walks over and speaks to you. After
    //   a crash between cars (AccidentScene) a police car drives to the scene instead, and its officer comes over.
    //   Responders set out from out of your sight (behind you or behind buildings) and switch the siren off once they
    //   are on the scene; the beacons keep flashing. No siren is ever heard without its vehicle. Both vehicles are
    //   copies of the city's own police car and ambulance, made once at the start and reused. Knocking down a person
    //   on foot kills them: two paramedics (copies of anyone in the city) wheel the stretcher, and a detainee van (a
    //   copy of the city's van) comes with the police. Stopped and caught: the officer arrests you and you are taken
    //   away in the van; the milestone fails. Driving off first: the fine for refusing to stop
    //   is multiplied, caught later or not. This class decides who is sent where and what happens on arrival;
    //   EmergencyVehicle drives each vehicle and AmbulanceCrew plays the paramedics' stretcher scene.
    public sealed class EmergencyResponse : MonoBehaviour
    {
        const float AmbulanceSpeed = 17f, PoliceTop = 27f, CatchRadius = 25f, LoseDistance = 320f, LoseSeconds = 15f;

        PlayerRig _rig;
        Navigator _nav;
        StoryDirector _director;
        EmergencyVehicle _ambulance, _police, _van;
        AmbulanceCrew _crew;
        bool _killed, _fled, _arresting, _vanLeaving;
        float _vanReplan;
        readonly List<Vector3> _zebras = new List<Vector3>();
        Vector3 _victim;
        float _ambulanceLeaveAt, _replan, _stoppedFor, _awayFor, _chaseStart, _policeLeaveAt;
        bool _chasing, _crossing, _attending, _leavingPlanned, _closedIn;
        Vector3 _attendAt;
        OfficerOnFoot _officer;

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

            RoadNetworkTraffic traffic = null;
            foreach (var t in Object.FindObjectsByType<RoadNetworkTraffic>(FindObjectsSortMode.None))
                if (!t.IsWalkers)
                    traffic = t;
            _ambulance = EmergencyVehicle.Create(traffic, "Ambulance", "Ambulance (responding)", transform);
            _police = EmergencyVehicle.Create(traffic, "Police", "Police car (responding)", transform);
            _van = EmergencyVehicle.Create(traffic, "Van", "Detainee van (police)", transform) ??
                   EmergencyVehicle.Create(traffic, "Police", "Detainee van (police)", transform);
            _crew = new AmbulanceCrew(transform, 2);

            _zebras.AddRange(ZebraCentres());

            _officer = OfficerOnFoot.Create(transform);
            CarImpacts.PersonHit += OnPersonHit;
        }

        // A crash between cars: a police car comes to the scene (no chase) and its officer speaks to you.
        public void AttendCrash(Vector3 scene)
        {
            if (_police == null || _police.Active)
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
        public Transform Ambulance => _ambulance != null && _ambulance.Active ? _ambulance.Body : null;
        public Transform Police => _police != null && _police.Active ? _police.Body : null;
        public bool AmbulanceWithPatient => _ambulanceLeaveAt > 0f;
        public bool Chasing => _chasing;
        public bool OnCrossing => _crossing;

        void OnPersonHit(Vector3 where, bool onFoot)
        {
            if (_rig == null || _rig.Car == null)
                return;
            Debug.Log($"[Emergency] Person hit at {where:F0} (on foot {onFoot}); ambulance busy {_ambulance != null && _ambulance.Active}, police busy {_police != null && _police.Active}");
            _victim = where;
            _crossing = false;
            foreach (var z in _zebras)
                if ((z - where).sqrMagnitude < 8f * 8f)
                    _crossing = true;

            if (_ambulance != null && !_ambulance.Active)
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
                if (_van != null && !_van.Active)
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
        void Dispatch(EmergencyVehicle r, Vector3 target, float min, float max)
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
            r.SetOut(start, target, Graph);
        }

        RoadGraph Graph => _nav != null ? _nav.Graph : null;

        // Moves r along its route, never closer than a few metres to another responder or your car.
        bool Drive(EmergencyVehicle r, float topSpeed, float arrive, float dt) => r.Drive(topSpeed, arrive, Clearance(r), dt);

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

        static readonly Unity.Profiling.ProfilerMarker ProfileMarker = new Unity.Profiling.ProfilerMarker("KR.Emergency");

        void Update()
        {
            using (ProfileMarker.Auto())
                Tick();
        }

        void Tick()
        {
            float dt = Time.deltaTime;
            _ambulance?.Flash();
            _police?.Flash();
            UpdateAmbulance(dt);
            UpdatePolice(dt);
            UpdateVan(dt);
            _van?.Flash();
            if (_officer != null && _rig != null)
                _officer.Tick(dt, _rig.CurrentMode == PlayerRig.Mode.Driving ? _rig.Car.transform.position : _rig.Player.transform.position);
        }

        // 0: on the way; >0: with the patient until that time; -1: driving away.
        void UpdateAmbulance(float dt)
        {
            var r = _ambulance;
            if (r == null || !r.Active)
                return;
            if (_ambulanceLeaveAt == 0f)
            {
                if (Drive(r, AmbulanceSpeed, 7f, dt))
                {
                    _ambulanceLeaveAt = float.MaxValue;   // with the patient until the stretcher is back in
                    r.StopSiren();   // on the scene: beacons only
                    if (_rig != null && (_rig.Player.transform.position - _victim).sqrMagnitude < 80f * 80f)
                        _rig.Toast("The ambulance has arrived. Two paramedics bring a stretcher.", 5f);
                    StartCoroutine(_crew.PickUp(r.Body, CarImpacts.LastVictim, _victim, _rig, () => _ambulanceLeaveAt = Time.time));
                }
            }
            else if (_ambulanceLeaveAt > 0f)
            {
                if (Time.time > _ambulanceLeaveAt)
                {
                    _ambulanceLeaveAt = -1f;
                    r.Plan(Graph, _victim + r.Body.forward * 400f);
                }
            }
            else if (Drive(r, AmbulanceSpeed, 10f, dt))
                r.Stand();
        }

        void UpdatePolice(float dt)
        {
            var r = _police;
            if (r == null || !r.Active || _rig == null || _rig.Car == null)
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
                        r.StopSiren();
                        bool near = (you - _attendAt).sqrMagnitude < 40f * 40f;
                        _officer?.GetOut(r.Body, near ? you : _attendAt, new[]
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
                    r.Plan(Graph, r.Body.position + r.Body.forward * 300f);
                }
                if (Drive(r, 12f, 10f, dt))
                    r.Stand();
                return;
            }
            float distance = Vector3.Distance(r.Body.position, you);
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
                    r.Plan(Graph, you);
                else
                {
                    r.Direct(you - (you - r.Body.position).normalized * 7f);
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
                r.Halt();
                r.StopSiren();
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
                _officer?.GetOut(r.Body, you, lines, i =>
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
                r.StopSiren();
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

        void Arrest(EmergencyVehicle r, Vector3 you)
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
                _officer.GetOut(r.Body, you, lines, i =>
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
            while (_van != null && _van.Active && Time.time < until && _rig != null &&
                   Flat(_van.Body.position - (_rig.CurrentMode == PlayerRig.Mode.Driving ? _rig.Car.transform.position : _rig.Player.transform.position)).magnitude > 14f)
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
                    if (_police != null && _police.Active)
                        _police.Stand();
                    if (_van != null && _van.Active)
                        _van.Stand();
                });
            _killed = _fled = _arresting = false;
        }

        // The van comes to wherever you are; after the arrest (or once you got away) it drives off and is put away.
        void UpdateVan(float dt)
        {
            var r = _van;
            if (r == null || !r.Active || _rig == null)
                return;
            if (_vanLeaving)
            {
                if (Drive(r, 14f, 10f, dt))
                    r.Stand();
                return;
            }
            Vector3 you = _rig.CurrentMode == PlayerRig.Mode.Driving ? _rig.Car.transform.position : _rig.Player.transform.position;
            float distance = Flat(r.Body.position - you).magnitude;
            _vanReplan -= dt;
            if (_vanReplan <= 0f)
            {
                _vanReplan = 2f;
                if (distance > 40f)
                    r.Plan(Graph, you);
                else
                {
                    r.Direct(you - (you - r.Body.position).normalized * 9f);
                }
            }
            if (Drive(r, distance < 30f ? 6f : 22f, 3f, dt) && r.Siren != null && r.Siren.isPlaying)
                r.StopSiren();   // pulled up: beacons only
        }

        void SendVanAway()
        {
            if (_van == null || !_van.Active)
                return;
            _vanLeaving = true;
            _van.StopSiren();
            _van.Plan(Graph, _van.Body.position + _van.Body.forward * 300f);
        }

        // Free metres ahead of r before another responder or your car (2.6 m wide corridor, 14 m long, keeping
        // a 5 m gap); float.MaxValue when the way is clear.
        float Clearance(EmergencyVehicle r)
        {
            float room = float.MaxValue;
            var p = r.Body.position;
            var f = Flat(r.Body.forward).normalized;
            void Check(Transform other, float halfLength)
            {
                if (other == null || other == r.Body || !other.gameObject.activeInHierarchy)
                    return;
                var d = Flat(other.position - p);
                float along = Vector3.Dot(d, f);
                float lateral = Mathf.Abs(d.x * f.z - d.z * f.x);
                if (along > 0.5f && along < 14f + halfLength && lateral < 2.6f)
                    room = Mathf.Min(room, along - halfLength - 5f);
            }
            if (_ambulance != null && _ambulance.Active) Check(_ambulance.Body, 2.5f);
            if (_police != null && _police.Active) Check(_police.Body, 2.5f);
            if (_van != null && _van.Active) Check(_van.Body, 2.5f);
            if (_rig != null && _rig.Car != null)
                Check(_rig.Car.transform, 2.3f);
            return room;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
