using KR.Gameplay.Player;
using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // What happens after you hit another car: it stops where it is with a long blast of the horn, its driver gets out
    // and comes to you upset, the cars stuck behind start hooting and their drivers shout, and traffic queues.
    // Stop, shift to P, get out and talk to the driver (the "acc_driver" conversation) to settle it; they drive
    // off afterwards. Driving away instead leaves the scene of a crash (flag "left_crash", and the officer hears
    // of it). Horns use the recorded clip when given (SetHorn), otherwise one synthesised here.
    public sealed class AccidentScene : MonoBehaviour
    {
        [SerializeField] PlayerRig rig;
        [SerializeField] RoadNetworkTraffic traffic;
        [SerializeField] StoryDirector director;
        [SerializeField] ConversationRunner runner;
        [SerializeField, Tooltip("The other driver: a person with an Npc (conversation acc_driver), hidden until needed.")]
        Npc driver;
        [SerializeField] float minImpact = 1.5f;
        [SerializeField] float leaveDistance = 90f;

        static readonly string[] DriverLines =
        {
            "Eh! Didn't you see me?", "Look at my car!", "Come out, we need to sort this out.",
            "I was in my lane!", "Stop there, don't drive off!",
        };
        static readonly string[] QueueLines = { "Move the cars aside!", "Come on, we're late!", "Pull over, please!", "What's going on up there?" };

        int _hit = -1;
        float _nextLine, _nextHoot, _settledAt;
        bool _talked;
        Vector3 _scene;
        [SerializeField] AudioClip hornClip;   // recorded horn; synthesised when missing
        AudioClip _horn;
        AudioSource[] _horns;
        SpeechBubble _driverBubble, _queueBubble;
        PersonAnimator _driverAnim;

        public bool Active => _hit >= 0;

        public void SetHorn(AudioClip clip) => hornClip = clip;

        public void Configure(PlayerRig playerRig, RoadNetworkTraffic vehicles, StoryDirector story, ConversationRunner conversations, Npc otherDriver)
        {
            rig = playerRig;
            traffic = vehicles;
            director = story;
            runner = conversations;
            driver = otherDriver;
        }

        void Start()
        {
            if (rig == null || rig.Car == null || traffic == null)
            {
                enabled = false;
                return;
            }
            rig.Car.gameObject.AddComponent<CollisionRelay>().Hit += OnHit;
            if (runner != null)
                runner.Finished += OnTalked;
            _horn = hornClip != null ? hornClip : HornClip();
            _horns = new AudioSource[3];
            for (int i = 0; i < _horns.Length; i++)
            {
                var src = new GameObject($"Horn {i}").AddComponent<AudioSource>();
                src.transform.SetParent(transform, false);
                src.clip = _horn;
                src.spatialBlend = 1f;
                src.minDistance = 6f;
                src.maxDistance = 120f;
                src.rolloffMode = AudioRolloffMode.Linear;
                _horns[i] = src;
            }
            _driverBubble = SpeechBubble.Create(transform);
            _queueBubble = SpeechBubble.Create(transform);
            if (driver != null)
            {
                _driverAnim = driver.GetComponent<PersonAnimator>();
                driver.gameObject.SetActive(false);
            }
        }

        void OnDestroy()
        {
            if (runner != null)
                runner.Finished -= OnTalked;
        }

        void OnHit(Collision collision)
        {
            if (_hit >= 0 || collision.relativeVelocity.magnitude < minImpact)
                return;
            int index = traffic.IndexOf(collision.transform);
            if (index < 0)
                return;
            var body = traffic.Body(index);
            _hit = index;
            _talked = false;
            _settledAt = 0f;
            _scene = body.position;
            traffic.Halt(index, 600f);
            Hoot(body.position, 0, 1.4f);
            _nextLine = Time.time + 1.5f;
            _nextHoot = Time.time + Random.Range(3f, 6f);
            director?.SetFlag("crash");
            // A real police car comes to the scene (no siren without a vehicle).
            var police = FindFirstObjectByType<EmergencyResponse>();
            if (police != null)
                police.AttendCrash(body.position);

            // The driver gets out on their side (left-hand drive) and faces your car.
            if (driver != null)
            {
                // Out on the side away from your car, so they are not stuck between the two.
                var side = -body.right;
                if (Vector3.Dot(rig.Car.transform.position - body.position, side) > 0f)
                    side = -side;
                var at = body.position + side * 1.7f;
                at.y = GroundY(at, body.position.y);
                driver.transform.SetPositionAndRotation(at, Quaternion.LookRotation(Flat(rig.Car.transform.position - at)));
                driver.gameObject.SetActive(true);
                _driverAnim?.SetTalking(true, rig.Car.transform.position);
                SoundBank.Play("car_door", at + Vector3.up, 0.9f);
            }
            rig.Toast("You hit another car. Stop, shift to P and get out to talk to the driver.", 6f);
        }

        // Ground (road, pavement or grass) under p, not a car roof; fallback keeps the old height.
        static float GroundY(Vector3 p, float fallback)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, p.y + 3f, p.z), Vector3.down, 8f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            // The walkable surface nearest the current height (a parked car's roof is not ground).
            float best = fallback, gap = float.MaxValue;
            foreach (var h in hits)
                if (h.collider.attachedRigidbody == null && h.normal.y > 0.7f && !(h.collider is CapsuleCollider) &&
                    Mathf.Abs(h.point.y - fallback) < gap)
                {
                    gap = Mathf.Abs(h.point.y - fallback);
                    best = h.point.y;
                }
            return best;
        }

        void Update()
        {
            if (_hit < 0)
                return;
            var body = traffic.Body(_hit);
            float now = Time.time;
            if (_settledAt > 0f)
            {
                if (now >= _settledAt)
                    Finish();
                return;
            }
            // The driver walks up to wherever you are (within a few metres of their car) and keeps on at you.
            if (driver != null && driver.gameObject.activeSelf && !_talked)
            {
                Vector3 you = rig.CurrentMode == PlayerRig.Mode.Driving ? rig.Car.transform.position : rig.Player.transform.position;
                // In your car: they come to your window (the driver's side, left in Rwanda), where you can see them,
                // not between the two cars. On foot: up to you.
                Vector3 goal = rig.CurrentMode == PlayerRig.Mode.Driving
                    ? rig.Car.transform.position - rig.Car.transform.right * 2.1f + rig.Car.transform.forward * 0.6f
                    : Vector3.MoveTowards(_scene, you, 4f);
                goal.y = GroundY(goal, driver.transform.position.y);
                Vector3 to = goal - driver.transform.position;
                if (Flat(to).magnitude > (rig.CurrentMode == PlayerRig.Mode.Driving ? 0.3f : 1.8f) && runner != null && !runner.Talking)
                {
                    var next = driver.transform.position + Flat(to).normalized * Mathf.Min(1.6f * Time.deltaTime, Flat(to).magnitude);
                    next.y = GroundY(next, next.y);
                    driver.transform.position = next;
                }
                if (runner == null || !runner.Talking)
                    driver.transform.rotation = Quaternion.LookRotation(Flat(you - driver.transform.position) + Vector3.forward * 1e-4f);
                if (now >= _nextLine && (runner == null || !runner.Talking))
                {
                    _driverBubble.Say(driver.transform, 2.25f, DriverLines[Random.Range(0, DriverLines.Length)]);
                    _nextLine = now + Random.Range(4f, 7f);
                }
            }
            // The queue behind loses patience.
            if (now >= _nextHoot)
            {
                _nextHoot = now + Random.Range(4f, 9f);
                int waiting = NearestWaiting(body != null ? body.position : _scene);
                if (waiting >= 0)
                {
                    var car = traffic.Body(waiting);
                    Hoot(car.position, 1 + Random.Range(0, 2), Random.Range(0.4f, 1.1f));
                    if (Random.value < 0.6f)
                        _queueBubble.Say(car, 2.3f, QueueLines[Random.Range(0, QueueLines.Length)], 3f);
                }
            }
            // Driving off without stopping to talk.
            if (!_talked && rig.CurrentMode == PlayerRig.Mode.Driving && Flat(rig.Car.transform.position - _scene).magnitude > leaveDistance)
            {
                director?.SetFlag("left_crash");
                rig.Toast("You left the scene of a crash. Always stop, help and exchange details.", 6f);
                Finish();
            }
        }

        void OnTalked(string conversation, Npc npc)
        {
            if (_hit < 0 || npc != driver)
                return;
            _talked = true;
            director?.SetFlag("crash_settled");
            _driverAnim?.SetTalking(false, Vector3.zero);
            _settledAt = Time.time + 4f;
        }

        void Finish()
        {
            if (_hit >= 0)
                traffic.Halt(_hit, 0f);
            _hit = -1;
            _driverBubble.Hide();
            _queueBubble.Hide();
            if (driver != null)
                driver.gameObject.SetActive(false);
        }

        // A slow or stopped car within 70 m of the crash, other than the one you hit.
        int NearestWaiting(Vector3 at)
        {
            int best = -1;
            float bestD = 70f * 70f;
            for (int i = 0; i < traffic.Count; i++)
            {
                if (i == _hit || traffic.CurrentSpeed(i) > 0.8f)
                    continue;
                var b = traffic.Body(i);
                if (b == null)
                    continue;
                float d = (b.position - at).sqrMagnitude;
                if (d < bestD && d > 9f)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        void Hoot(Vector3 at, int source, float seconds)
        {
            var src = _horns[Mathf.Clamp(source, 0, _horns.Length - 1)];
            src.transform.position = at;
            src.pitch = Random.Range(0.92f, 1.08f);
            src.Stop();
            double start = AudioSettings.dspTime + 0.05;
            src.PlayScheduled(start);
            src.SetScheduledEndTime(start + seconds);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        // A two-tone car horn (about 420 and 500 Hz, a little overdriven), 1.5 s, made at start-up.
        static AudioClip HornClip()
        {
            const int rate = 44100;
            int n = (int)(rate * 1.5f);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float s = Mathf.Sin(2f * Mathf.PI * 420f * t) + 0.8f * Mathf.Sin(2f * Mathf.PI * 500f * t) +
                          0.25f * Mathf.Sin(2f * Mathf.PI * 840f * t) + 0.2f * Mathf.Sin(2f * Mathf.PI * 1000f * t);
                s = (float)System.Math.Tanh(s * 1.6f) * 0.55f;
                float env = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((1.5f - t) / 0.05f);
                data[i] = s * env;
            }
            var clip = AudioClip.Create("Car horn (synthesised)", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
