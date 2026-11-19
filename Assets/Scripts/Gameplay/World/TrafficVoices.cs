using KR.Gameplay.Player;
using UnityEngine;

namespace KR.Gameplay.World
{
    // Traffic you can hear: a few 3D engine sounds that follow the vehicles nearest to you (a diesel idle on buses,
    // a motorbike on moto-taxis, a car engine on the rest; pitch follows their speed), and a street-noise bed whose
    // volume follows how much traffic is around you. Voices are reassigned twice a second; nothing is allocated
    // after start.
    public sealed class TrafficVoices : MonoBehaviour
    {
        [SerializeField] RoadNetworkTraffic traffic;
        [SerializeField] PlayerRig rig;
        [SerializeField] AudioClip car, bus, moto, street;
        [SerializeField] int voices = 4;
        [SerializeField] float range = 45f, volume = 0.55f, streetVolume = 0.35f;

        AudioSource[] _src;
        int[] _car;
        float[] _dist;
        AudioSource _street, _hum, _nature, _wind;
        float _next;
        Vector3[] _lastPos;
        bool[] _passed;

        public void Configure(RoadNetworkTraffic vehicles, PlayerRig playerRig, AudioClip carClip, AudioClip busClip, AudioClip motoClip, AudioClip streetClip)
        {
            traffic = vehicles;
            rig = playerRig;
            car = carClip;
            bus = busClip;
            moto = motoClip;
            street = streetClip;
        }

        void Start()
        {
            if (traffic == null)
            {
                enabled = false;
                return;
            }
            _src = new AudioSource[voices];
            _car = new int[voices];
            _dist = new float[voices];
            _lastPos = new Vector3[voices];
            _passed = new bool[voices];
            for (int i = 0; i < voices; i++)
            {
                var s = new GameObject($"Traffic voice {i}").AddComponent<AudioSource>();
                s.transform.SetParent(transform, false);
                s.loop = true;
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.minDistance = 4f;
                s.maxDistance = range;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.dopplerLevel = 0.4f;
                _src[i] = s;
                _car[i] = -1;
            }
            if (street != null)
            {
                _street = gameObject.AddComponent<AudioSource>();
                _street.clip = street;
                _street.loop = true;
                _street.spatialBlend = 0f;
                _street.volume = 0f;
                _street.Play();
            }
            // Beds from the SoundBank: the city's distant hum everywhere; birds and wind where it's quiet.
            _hum = SoundBank.Loop(transform, "city_hum", false);
            _nature = SoundBank.Loop(transform, "nature", false);
            _wind = SoundBank.Loop(transform, "wind", false);
        }

        void Update()
        {
            var player = RoadNetworkTraffic.Player;
            if (player == null)
                return;
            var me = player.position;
            if (Time.time >= _next)
            {
                _next = Time.time + 0.5f;
                Assign(me);
            }
            for (int i = 0; i < _src.Length; i++)
            {
                var body = _car[i] >= 0 ? traffic.Body(_car[i]) : null;
                var s = _src[i];
                if (body == null)
                {
                    s.volume = Mathf.MoveTowards(s.volume, 0f, Time.deltaTime * 2f);
                    if (s.volume <= 0f && s.isPlaying) s.Stop();
                    continue;
                }
                var p = body.position;
                float v = Time.deltaTime > 0f ? Vector3.Distance(p, _lastPos[i]) / Time.deltaTime : 0f;
                _lastPos[i] = p;
                if (v > 60f) v = 0f;   // pooled car moved to a new road
                s.transform.position = p;
                s.pitch = s.clip == bus ? 0.9f + Mathf.Clamp01(v / 15f) * 0.3f : 0.8f + Mathf.Clamp01(v / 18f) * 0.6f;
                s.volume = Mathf.MoveTowards(s.volume, volume, Time.deltaTime * 2f);
                if (!s.isPlaying) s.Play();
                // A car going by close and fast: a pass-by (a whoosh when very close).
                float near = Vector3.Distance(p, me);
                if (!_passed[i] && near < 7f && v > 8f)
                {
                    _passed[i] = true;
                    SoundBank.Play(near < 3.5f ? "whoosh" : "passby", p, 0.8f);
                }
                else if (near > 12f)
                    _passed[i] = false;
            }
            if (_street != null)
            {
                float near = 0f;
                for (int i = 0; i < _dist.Length; i++)
                    if (_car[i] >= 0)
                        near += 1f - Mathf.Clamp01(_dist[i] / range);
                // In the car the windows shut most of the street out.
                bool inCar = rig != null && rig.CurrentMode == PlayerRig.Mode.Driving;
                _street.volume = Mathf.MoveTowards(_street.volume, streetVolume * Mathf.Clamp01(0.25f + near * 0.4f) * (inCar ? 0.4f : 1f), Time.deltaTime * 0.5f);
            }
            {
                float busy = 0f;
                for (int i = 0; i < _dist.Length; i++)
                    if (_car[i] >= 0)
                        busy += 1f - Mathf.Clamp01(_dist[i] / range);
                bool inCar = rig != null && rig.CurrentMode == PlayerRig.Mode.Driving;
                float calm = Mathf.Clamp01(1f - busy * 0.5f) * (inCar ? 0.2f : 1f);
                SoundBank.Fade(_hum, 0.16f * (inCar ? 0.5f : 1f), 0.3f);
                SoundBank.Fade(_nature, 0.3f * calm, 0.3f);
                SoundBank.Fade(_wind, 0.12f * calm, 0.3f);
            }
        }

        // The nearest vehicles within range get a voice; a voice keeps its vehicle while it stays among them.
        void Assign(Vector3 me)
        {
            for (int i = 0; i < _dist.Length; i++)
                _dist[i] = float.MaxValue;
            int n = traffic.Count;
            for (int k = 0; k < voices; k++)
            {
                int pick = -1;
                float pd = range * range;
                for (int c = 0; c < n; c++)
                {
                    var b = traffic.Body(c);
                    if (b == null || !b.gameObject.activeInHierarchy)
                        continue;
                    float d = (b.position - me).sqrMagnitude;
                    if (d >= pd || Taken(c, k))
                        continue;
                    pd = d;
                    pick = c;
                }
                if (_car[k] != pick)
                {
                    _car[k] = pick;
                    if (pick >= 0)
                    {
                        var clip = ClipFor(traffic.Body(pick).name);
                        if (_src[k].clip != clip)
                        {
                            _src[k].Stop();
                            _src[k].clip = clip;
                            _src[k].volume = 0f;
                            _src[k].time = clip != null ? Random.value * clip.length * 0.9f : 0f;
                        }
                        _lastPos[k] = traffic.Body(pick).position;
                    }
                }
                _dist[k] = pick >= 0 ? Mathf.Sqrt(pd) : float.MaxValue;
            }
        }

        bool Taken(int c, int upTo)
        {
            for (int k = 0; k < upTo; k++)
                if (_car[k] == c)
                    return true;
            return false;
        }

        AudioClip ClipFor(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("bus") || n.Contains("coaster") || n.Contains("truck"))
                return bus != null ? bus : car;
            if (n.Contains("moto") || n.Contains("scooter"))
            {
                // Two motorbike recordings, so a row of moto-taxis doesn't sound like one bike.
                var far = SoundBank.Get("moto_far");
                return far != null && Random.value < 0.5f ? far : moto != null ? moto : car;
            }
            return car;
        }
    }
}
