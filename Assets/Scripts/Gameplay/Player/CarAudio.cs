using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Player
{
    // Sounds of the player's car: doors and the starter when you get in, an idle and a revving loop blended by an
    // automatic gearbox's engine speed, the horn (H / left grip), tyre squeal on hard braking or sliding, and
    // impacts. All sources are made once at start (no allocations while driving).
    [RequireComponent(typeof(DrivableCar))]
    public sealed class CarAudio : MonoBehaviour
    {
        [SerializeField] AudioClip start, idle, rev, horn, doorOpen, doorClose, skid, crash, thud;
        [SerializeField] float volume = 0.8f;

        // Speed (m/s) where each automatic gear tops out; the engine note rises through each band.
        static readonly float[] GearTop = { 4.5f, 9f, 15f, 22f, 30f, 40f };

        DrivableCar _car;
        Rigidbody _rb;
        AudioSource _idle, _rev, _horn, _skid, _oneShot, _roll;
        bool _wasDriven, _wasHandbrake;
        float _running, _rpm, _lastHit;

        public void Configure(AudioClip engineStart, AudioClip engineIdle, AudioClip engineRev, AudioClip hornClip,
                              AudioClip open, AudioClip close, AudioClip tyres, AudioClip impact, AudioClip bump)
        {
            start = engineStart;
            idle = engineIdle;
            rev = engineRev;
            horn = hornClip;
            doorOpen = open;
            doorClose = close;
            skid = tyres;
            crash = impact;
            thud = bump;
        }

        void Awake()
        {
            _car = GetComponent<DrivableCar>();
            _rb = GetComponent<Rigidbody>();
            _idle = Source("Engine idle", idle, true, 3f);
            _rev = Source("Engine rev", rev, true, 3f);
            _horn = Source("Horn", horn, true, 6f);
            _skid = Source("Tyres", skid, true, 4f);
            _oneShot = Source("Doors and impacts", null, false, 3f);
        }

        // Shared sounds from the SoundBank (ready after every Awake): tyre roll on the road.
        void Start() => _roll = SoundBank.Loop(transform, "road_roll", true, 3f, 40f);

        AudioSource Source(string name, AudioClip clip, bool loop, float minDistance)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.minDistance = minDistance;
            s.maxDistance = 80f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.dopplerLevel = 0f;
            s.volume = 0f;
            return s;
        }

        void Update()
        {
            bool driven = _car.Driven;
            if (driven != _wasDriven)
            {
                _wasDriven = driven;
                if (driven)
                {
                    Play(doorClose, 0.9f);
                    Play(start, 0.9f);
                    _running = Time.time + (start != null ? start.length * 0.8f : 0f);
                }
                else
                {
                    Play(doorOpen, 0.9f);
                    _running = -1f;
                }
            }

            bool engineOn = driven && _running >= 0f && Time.time >= _running;
            float speed = Mathf.Abs(_car.Speed);
            // Engine speed 0..1: where the car is inside its current gear's band, raised by the accelerator.
            int g = 0;
            while (g < GearTop.Length - 1 && speed > GearTop[g])
                g++;
            float lo = g == 0 ? 0f : GearTop[g - 1];
            float band = Mathf.Clamp01((speed - lo) / (GearTop[g] - lo));
            float target = engineOn ? Mathf.Clamp01(0.15f + band * 0.6f + Mathf.Abs(_car.Throttle) * 0.35f) : 0f;
            _rpm = Mathf.MoveTowards(_rpm, target, Time.deltaTime * 1.8f);

            Loop(_idle, engineOn, volume * Mathf.Lerp(0.7f, 0.15f, _rpm), 0.9f + _rpm * 0.3f);
            Loop(_rev, engineOn, volume * Mathf.Lerp(0.05f, 0.85f, _rpm), 0.75f + _rpm * 0.65f);

            // Tyres rolling on asphalt, louder with speed.
            SoundBank.Fade(_roll, driven ? volume * Mathf.Clamp01(speed / 18f) * 0.6f : 0f, 2f);
            if (_roll != null)
                _roll.pitch = 0.85f + Mathf.Clamp01(speed / 30f) * 0.35f;

            // Seatbelt click, handbrake ratchet.
            if (driven && RigInput.BeltPressed)
                SoundBank.Play("seatbelt", transform.position, 0.9f);
            bool handbrake = driven && (RigInput.Handbrake || _car.Parked);
            if (handbrake && !_wasHandbrake && _running >= 0f)
                SoundBank.Play("handbrake", transform.position, 0.7f);
            _wasHandbrake = handbrake;

            bool honk = driven && RigInput.Horn;
            Loop(_horn, honk, volume, 1f);

            // Tyres: sliding sideways, or braking hard / the handbrake at speed.
            float side = Mathf.Abs(Vector3.Dot(_rb.linearVelocity, transform.right));
            bool squeal = driven && (side > 3.5f || speed > 8f && (_car.BrakePedal > 0.85f || RigInput.Handbrake));
            Loop(_skid, squeal, volume * Mathf.Clamp01(Mathf.Max(side / 8f, speed / 20f)), 0.9f + Mathf.Clamp01(speed / 30f) * 0.2f);
        }

        static void Loop(AudioSource s, bool on, float vol, float pitch)
        {
            if (s.clip == null)
                return;
            s.volume = Mathf.MoveTowards(s.volume, on ? vol : 0f, Time.deltaTime * 4f);
            s.pitch = pitch;
            if (s.volume > 0.001f && !s.isPlaying)
                s.Play();
            else if (s.volume <= 0.001f && s.isPlaying)
                s.Stop();
        }

        void Play(AudioClip clip, float vol)
        {
            if (clip != null)
                _oneShot.PlayOneShot(clip, vol * volume);
        }

        void OnCollisionEnter(Collision c)
        {
            float hit = c.relativeVelocity.magnitude;
            if (hit < 2.5f || Time.time - _lastHit < 0.6f)
                return;
            _lastHit = Time.time;
            if (hit > 7f)
            {
                // Two recordings of crashes, and breaking glass on a hard one.
                if (Random.value < 0.5f || SoundBank.Get("crash") == null)
                    Play(crash, 1f);
                else
                    SoundBank.Play("crash", c.GetContact(0).point, 1f);
                if (hit > 11f)
                    SoundBank.Play("glass", c.GetContact(0).point, 0.9f);
            }
            else
                Play(thud, Mathf.Clamp01(hit / 7f) + 0.2f);
        }
    }
}
