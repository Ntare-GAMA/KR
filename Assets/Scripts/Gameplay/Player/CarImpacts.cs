using KR.Core;
using System.Collections;
using System.Collections.Generic;
using KR.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace KR.Gameplay.Player
{
    // Real masses, and what happens when the player's car hits someone.
    //   Masses (kg, typical real values): the player's sedan 1,500; traffic by type: car 1,300, SUV 2,000,
    //   minibus 2,800, truck 9,000, bus 12,000, motorbike with rider 225; a person 75.
    //   Traffic and people move along paths (kinematic bodies), which physics treats as immovable: hitting a
    //   person used to stop the car like a wall. Now light road users (people, motorbikes) no longer block
    //   the car: the car loses only the speed real momentum says it would (a 75 kg person against 1,500 kg is
    //   about 5 %), and the person or the bike is knocked down, lies a few seconds, and gets up again.
    //   Cars, minibuses, trucks and buses stay solid (and start the accident scene, as before).
    // Added to the player's car when the Story scene loads.
    public sealed class CarImpacts : MonoBehaviour
    {
        public const float PlayerCarMass = 1500f, PersonMass = 75f, MotoMass = 225f;
        const float Restitution = 0.1f, LieSeconds = 7f, WaitForAmbulance = 150f;

        sealed class Light
        {
            public Collider collider;
            public Transform root, visual;
            public RoadNetworkTraffic traffic;
            public int index = -1;
            public float mass;
            public bool down, collected;
            public Behaviour[] movers;
            public Renderer[] hidden;
        }

        // The person most recently knocked down on foot (the ambulance comes for them), and the record behind it.
        public static Transform LastVictim { get; private set; }
        static Light _lastVictim;
        static CarImpacts _instance;

        // The ambulance crew has taken them: they disappear from the street (taken to hospital). They come back to
        // the street only once you are far away, so nobody is seen getting up after being taken.
        public static void Collect(Transform victim)
        {
            if (_lastVictim == null || _lastVictim.root != victim || _instance == null)
                return;
            _lastVictim.collected = true;
            _lastVictim.hidden = victim.GetComponentsInChildren<Renderer>();
            foreach (var r in _lastVictim.hidden)
                r.enabled = false;
        }

        PlayerRig _rig;
        Rigidbody _rb;
        Collider[] _carColliders;
        Bounds _probe;   // the car body in the car's local space
        readonly Dictionary<Collider, Light> _light = new Dictionary<Collider, Light>();
        readonly Collider[] _hits = new Collider[24];

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
            if (rig == null || rig.Car == null || rig.Car.GetComponent<CarImpacts>() != null)
                return;
            rig.Car.gameObject.AddComponent<CarImpacts>().Setup(rig);
        }

        // Typical mass of a traffic vehicle from its model name.
        public static float MassFor(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("moto") || n.Contains("scooter") || n.Contains("bike")) return MotoMass;
            if (n.Contains("bus") && !n.Contains("minibus")) return 12000f;
            if (n.Contains("coaster") || n.Contains("minibus") || n.Contains("van")) return 2800f;
            if (n.Contains("truck") || n.Contains("lorry")) return 9000f;
            if (n.Contains("suv") || n.Contains("jeep") || n.Contains("pickup") || n.Contains("land") || n.Contains("cruiser")) return 2000f;
            return 1300f;
        }

        void Setup(PlayerRig rig)
        {
            _rig = rig;
            _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                _rb.mass = PlayerCarMass;
                // Pushed out of traffic gently, not fired into the air.
                _rb.maxDepenetrationVelocity = 1.5f;
            }
            var cols = new List<Collider>();
            bool any = false;
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger)
                    continue;
                cols.Add(c);
                if (c is WheelCollider)
                    continue;
                var b = new Bounds(transform.InverseTransformPoint(c.bounds.center), Vector3.zero);
                var e = c.bounds.extents;
                for (int k = 0; k < 8; k++)
                    b.Encapsulate(transform.InverseTransformPoint(c.bounds.center + Vector3.Scale(e,
                        new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f))));
                _probe = any ? Encapsulate(_probe, b) : b;
                any = true;
            }
            if (!any)
                _probe = new Bounds(new Vector3(0f, 0.75f, 0f), new Vector3(1.8f, 1.4f, 4.5f));
            _carColliders = cols.ToArray();
            Collect();
            StartCoroutine(CollectLater());
        }

        static Bounds Encapsulate(Bounds a, Bounds b)
        {
            a.Encapsulate(b);
            return a;
        }

        IEnumerator CollectLater()
        {
            // People and traffic that start a little later (scheduled staff, pooled walkers).
            yield return new WaitForSeconds(5f);
            Collect();
        }

        // Real masses on every traffic body, and the list of light road users the car should not be stopped by.
        void Collect()
        {
            foreach (var traffic in FindObjectsByType<RoadNetworkTraffic>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                for (int i = 0; i < traffic.Count; i++)
                {
                    var body = traffic.Body(i);
                    if (body == null)
                        continue;
                    float mass = traffic.IsWalkers ? PersonMass : MassFor(body.name);
                    var rb = body.GetComponent<Rigidbody>();
                    if (rb != null)
                        rb.mass = mass;
                    if (mass > MotoMass)
                        continue;
                    foreach (var c in body.GetComponentsInChildren<Collider>(true))
                        Add(c, body, traffic, i, mass);
                }
            }
            // Everyone else on foot: staff, people out walking, people at the market.
            foreach (var person in FindObjectsByType<PersonAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (var c in person.GetComponentsInChildren<Collider>(true))
                    if (c.attachedRigidbody != null && c.attachedRigidbody.isKinematic)
                    {
                        c.attachedRigidbody.mass = PersonMass;
                        Add(c, person.transform, null, -1, PersonMass);
                    }
                var own = person.GetComponent<Collider>().OrNull() ?? person.GetComponentInParent<Collider>();
                if (own != null && own.attachedRigidbody != null && own.attachedRigidbody.isKinematic && !_light.ContainsKey(own))
                {
                    own.attachedRigidbody.mass = PersonMass;
                    Add(own, own.transform, null, -1, PersonMass);
                }
            }
        }

        void Add(Collider c, Transform root, RoadNetworkTraffic traffic, int index, float mass)
        {
            if (c == null || c.isTrigger || _light.ContainsKey(c))
                return;
            var anim = root.GetComponentInChildren<Animator>(true);
            var visual = anim != null ? anim.transform : root.childCount > 0 ? root.GetChild(0) : null;
            var movers = new List<Behaviour>();
            foreach (var b in root.GetComponentsInParent<Behaviour>(true))
                if (b is Wanderer || b is StreetWalker)
                    movers.Add(b);
            _light[c] = new Light { collider = c, root = root, visual = visual, traffic = traffic, index = index, mass = mass, movers = movers.ToArray() };
            foreach (var cc in _carColliders)
                if (cc != null && !(cc is WheelCollider))
                    Physics.IgnoreCollision(cc, c, true);
        }

        void FixedUpdate()
        {
            if (_rb == null || _rig == null || _rig.CurrentMode != PlayerRig.Mode.Driving)
                return;
            var v = _rb.linearVelocity;
            float speed = v.magnitude;
            if (speed < 1f)
                return;
            // The car body, stretched ahead by how far it travels in two steps, so we see people before the
            // wheels reach them.
            var size = _probe.size + new Vector3(0.2f, 0f, speed * Time.fixedDeltaTime * 2f + 0.3f);
            var centre = transform.TransformPoint(_probe.center + Vector3.forward * (Mathf.Sign(Vector3.Dot(v, transform.forward)) * size.z * 0.15f));
            int n = Physics.OverlapBoxNonAlloc(centre, size * 0.5f, _hits, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (_light.TryGetValue(_hits[i], out var who) && !who.down)
                    Hit(who, v);
        }

        // Traffic is kinematic (it cannot give way physically), so a touch from it could throw the car up or over.
        // While touching a traffic vehicle the car may not rise faster than walking pace or pitch and roll quickly.
        void OnCollisionEnter(Collision c) => Settle(c);
        void OnCollisionStay(Collision c) => Settle(c);

        void Settle(Collision c)
        {
            if (_rb == null || c.rigidbody == null || !c.rigidbody.isKinematic)
                return;
            var v = _rb.linearVelocity;
            if (v.y > 0.5f)
                _rb.linearVelocity = new Vector3(v.x, 0.5f, v.z);
            var w = transform.InverseTransformDirection(_rb.angularVelocity);
            w.x = Mathf.Clamp(w.x, -0.4f, 0.4f);
            w.z = Mathf.Clamp(w.z, -0.4f, 0.4f);
            _rb.angularVelocity = transform.TransformDirection(w);
        }

        // Someone on foot (true) or a rider (false) was knocked down here: the ambulance and the police respond
        // (EmergencyResponse).
        public static event System.Action<Vector3, bool> PersonHit;

        void Hit(Light who, Vector3 v)
        {
            var push = who.root.position - transform.position;
            push.y = 0f;
            if (push.sqrMagnitude < 0.01f)
                push = transform.forward;
            push.Normalize();
            float approach = Vector3.Dot(v, push);
            if (approach > 0f)
            {
                // One-dimensional collision along the line between them: the car keeps most of its speed.
                float dv = (1f + Restitution) * who.mass / (PlayerCarMass + who.mass) * approach;
                _rb.linearVelocity = v - push * dv;
            }
            who.down = true;
            who.collider.enabled = false;          // the wheels must not climb over them either
            bool onFoot = who.mass <= PersonMass;
            who.collected = false;
            if (onFoot)
            {
                LastVictim = who.root;
                _lastVictim = who;
                _instance = this;
            }
            if (who.traffic != null && who.index >= 0)
                who.traffic.Halt(who.index, onFoot ? WaitForAmbulance + 60f : LieSeconds + 3f);
            foreach (var m in who.movers)
                if (m != null)
                    m.enabled = false;
            StartCoroutine(KnockDown(who, push, Mathf.Clamp01(approach / 12f)));
            Buzz();
            var audio = GetComponent<CarAudio>();
            if (audio != null)
                audio.Bump(approach / 10f);
            PersonHit?.Invoke(who.root.position, onFoot);
            _rig.Toast(who.mass <= PersonMass
                ? "You hit someone! Slow down and watch for people on the road."
                : "You knocked a motorbike over! Give riders room.", 5f);
        }

        // Falls over in the direction of the push, lies there, then gets up and carries on.
        IEnumerator KnockDown(Light who, Vector3 push, float force)
        {
            var visual = who.visual;
            var anim = visual != null ? visual.GetComponent<Animator>() : null;
            Quaternion start = visual != null ? visual.localRotation : Quaternion.identity;
            Vector3 startPos = visual != null ? visual.localPosition : Vector3.zero;
            if (anim != null)
                anim.enabled = false;
            if (visual != null)
            {
                var axis = Vector3.Cross(Vector3.up, push);
                var parent = visual.parent;
                Quaternion lying = Quaternion.AngleAxis(84f, parent != null ? parent.InverseTransformDirection(axis) : axis) * start;
                Vector3 slide = (parent != null ? parent.InverseTransformDirection(push) : push) * (0.6f + 1.2f * force) + Vector3.up * 0.12f;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
                {
                    visual.localRotation = Quaternion.Slerp(start, lying, t * t);
                    visual.localPosition = startPos + slide * t;
                    yield return null;
                }
                visual.localRotation = lying;
                // Someone knocked down on foot stays down until the ambulance crew takes them (or, if no ambulance
                // can come, for a long while); a rider gets up again.
                if (who.mass <= PersonMass)
                {
                    float until = Time.time + WaitForAmbulance;
                    while (!who.collected && Time.time < until)
                        yield return null;
                    if (who.collected)
                    {
                        // Gone to hospital; back on the street only once you are well away.
                        while (_rb != null && (who.root.position - transform.position).sqrMagnitude < 150f * 150f)
                            yield return new WaitForSeconds(1f);
                        if (who.hidden != null)
                            foreach (var r in who.hidden)
                                if (r != null) r.enabled = true;
                        who.hidden = null;
                        who.collected = false;
                        if (who.traffic != null && who.index >= 0)
                            who.traffic.Halt(who.index, 0.1f);
                    }
                }
                else
                    yield return new WaitForSeconds(LieSeconds);
                Quaternion down = visual.localRotation;
                Vector3 downPos = visual.localPosition;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.8f)
                {
                    visual.localRotation = Quaternion.Slerp(down, start, t);
                    visual.localPosition = Vector3.Lerp(downPos, startPos, t);
                    yield return null;
                }
                visual.localRotation = start;
                visual.localPosition = startPos;
            }
            else
                yield return new WaitForSeconds(LieSeconds);
            if (anim != null)
                anim.enabled = true;
            foreach (var m in who.movers)
                if (m != null)
                    m.enabled = true;
            // Back to solid once the car is clear of them.
            while (_rb != null && (who.root.position - transform.position).sqrMagnitude < 9f)
                yield return new WaitForSeconds(0.5f);
            who.collider.enabled = true;
            who.down = false;
        }

        static void Buzz()
        {
            foreach (var node in new[] { XRNode.LeftHand, XRNode.RightHand })
            {
                var d = InputDevices.GetDeviceAtXRNode(node);
                if (d.isValid)
                    d.SendHapticImpulse(0, 0.7f, 0.25f);
            }
        }
    }
}
