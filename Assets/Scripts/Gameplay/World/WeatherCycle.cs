using UnityEngine;
using UnityEngine.InputSystem;

namespace KR.Gameplay.World
{
    // Wind and weather for the PC scenes. Blends between Calm, Breeze, Windy and Shower following the clock
    // (still mornings, windier afternoons, a late-afternoon shower like Kigali's rainy seasons, calm nights),
    // with gusts and a slowly turning wind direction. Drives:
    //   the WindZone (the conifers' CTI shaders read it), terrain grass and flower waving,
    //   DayNightCycle.Overcast (light, sky, fog) and the rain particles around the camera.
    // F5 steps through the weathers by hand (Calm, Breeze, Windy, Shower, Overcast, back to automatic).
    public sealed class WeatherCycle : MonoBehaviour
    {
        public enum Kind { Calm, Breeze, Windy, Shower, Overcast }

        struct Preset
        {
            public float Wind, Turbulence, Pulse, PulseFrequency, GrassStrength, GrassAmount, GrassSpeed, Overcast, Rain;
        }

        static readonly Preset[] Presets =
        {
            new Preset { Wind = 0.15f, Turbulence = 0.1f, Pulse = 0.1f, PulseFrequency = 0.05f, GrassStrength = 0.12f, GrassAmount = 0.15f, GrassSpeed = 0.2f },
            new Preset { Wind = 0.6f, Turbulence = 0.35f, Pulse = 0.35f, PulseFrequency = 0.15f, GrassStrength = 0.45f, GrassAmount = 0.45f, GrassSpeed = 0.5f, Overcast = 0.05f },
            new Preset { Wind = 1.35f, Turbulence = 0.8f, Pulse = 0.9f, PulseFrequency = 0.35f, GrassStrength = 0.9f, GrassAmount = 0.8f, GrassSpeed = 0.95f, Overcast = 0.25f },
            new Preset { Wind = 1.05f, Turbulence = 1f, Pulse = 0.7f, PulseFrequency = 0.5f, GrassStrength = 0.75f, GrassAmount = 0.7f, GrassSpeed = 0.85f, Overcast = 0.85f, Rain = 1f },
            new Preset { Wind = 0.45f, Turbulence = 0.3f, Pulse = 0.25f, PulseFrequency = 0.12f, GrassStrength = 0.35f, GrassAmount = 0.35f, GrassSpeed = 0.4f, Overcast = 0.6f },
        };

        [SerializeField] WindZone wind;
        [SerializeField] Terrain terrain;
        [SerializeField] DayNightCycle cycle;
        [SerializeField] ParticleSystem rain;
        [SerializeField, Tooltip("Seconds to blend from one weather to the next.")] float blendSeconds = 25f;
        [SerializeField] float maxRainPerSecond = 6000f;

        Preset _now;
        Kind? _forced;
        float _baseStrength, _baseAmount, _baseSpeed;
        Color _baseTint;
        bool _captured;

        public Kind Target { get; private set; }
        public float Wind01 => Mathf.Clamp01(_now.Wind / 1.35f);
        public float Rain01 => _now.Rain;
        // Under a roof (inside a building or a restaurant): no rain falls around you.
        public static bool Sheltered { get; set; }
        bool _wasSheltered;

        // Raindrops that enter this collider vanish (the player's car, so no rain falls
        // inside it). The particle trigger test is a cheap shape check per drop, no physics.
        public void KeepRainOut(Collider shelter)
        {
            if (rain == null || shelter == null)
                return;
            var trigger = rain.trigger;
            trigger.enabled = true;
            trigger.enter = ParticleSystemOverlapAction.Kill;
            trigger.inside = ParticleSystemOverlapAction.Kill;
            trigger.exit = ParticleSystemOverlapAction.Ignore;
            trigger.outside = ParticleSystemOverlapAction.Ignore;
            trigger.AddCollider(shelter);
        }

        public void Configure(WindZone windZone, Terrain ground, DayNightCycle dayNight, ParticleSystem rainParticles)
        {
            wind = windZone;
            terrain = ground;
            cycle = dayNight;
            rain = rainParticles;
        }

        void OnEnable()
        {
            if (terrain != null && !_captured)
            {
                var d = terrain.terrainData;
                _baseStrength = d.wavingGrassStrength;
                _baseAmount = d.wavingGrassAmount;
                _baseSpeed = d.wavingGrassSpeed;
                _baseTint = d.wavingGrassTint;
                _captured = true;
            }
            Target = Scheduled();
            _now = Presets[(int)Target];
        }

        // The terrain data is an asset: put its waving settings back so play mode does not change it.
        void OnDisable()
        {
            if (terrain == null || !_captured)
                return;
            var d = terrain.terrainData;
            d.wavingGrassStrength = _baseStrength;
            d.wavingGrassAmount = _baseAmount;
            d.wavingGrassSpeed = _baseSpeed;
            d.wavingGrassTint = _baseTint;
        }

        public void Force(Kind? kind) => _forced = kind;

        // Force a weather and show it at once, with no blend (a new session starts in its own weather).
        public void SnapToForced()
        {
            if (_forced != null)
                Snap(_forced.Value);
        }

        public void Snap(Kind kind)
        {
            _forced = kind;
            Target = kind;
            _now = Presets[(int)kind];
        }

        Kind Scheduled()
        {
            float h = cycle != null ? cycle.Hour : 12f;
            if (h < 6f || h >= 21f) return Kind.Calm;
            if (h < 10f) return Kind.Breeze;
            if (h < 14f) return Mathf.PerlinNoise(h * 0.7f, 3.1f) > 0.55f ? Kind.Windy : Kind.Breeze;
            if (h < 16.5f) return Kind.Windy;
            if (h < 17.75f) return Kind.Shower;
            return Kind.Breeze;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f5Key.wasPressedThisFrame)
                _forced = _forced == null ? Kind.Calm : _forced == Kind.Overcast ? (Kind?)null : _forced + 1;

            Target = _forced ?? Scheduled();
            var to = Presets[(int)Target];
            float k = 1f - Mathf.Exp(-Time.deltaTime * 3f / Mathf.Max(1f, blendSeconds));
            _now.Wind = Mathf.Lerp(_now.Wind, to.Wind, k);
            _now.Turbulence = Mathf.Lerp(_now.Turbulence, to.Turbulence, k);
            _now.Pulse = Mathf.Lerp(_now.Pulse, to.Pulse, k);
            _now.PulseFrequency = Mathf.Lerp(_now.PulseFrequency, to.PulseFrequency, k);
            _now.GrassStrength = Mathf.Lerp(_now.GrassStrength, to.GrassStrength, k);
            _now.GrassAmount = Mathf.Lerp(_now.GrassAmount, to.GrassAmount, k);
            _now.GrassSpeed = Mathf.Lerp(_now.GrassSpeed, to.GrassSpeed, k);
            _now.Overcast = Mathf.Lerp(_now.Overcast, to.Overcast, k);
            _now.Rain = Mathf.Lerp(_now.Rain, to.Rain, k);

            // Gusts on top of the base wind, and a direction that wanders slowly around the east.
            float t = Time.time;
            float gust = Mathf.PerlinNoise(t * 0.23f, 7.7f);
            gust = gust * gust * 1.6f;
            if (wind != null)
            {
                wind.windMain = _now.Wind * (0.75f + gust * 0.6f);
                wind.windTurbulence = _now.Turbulence;
                wind.windPulseMagnitude = _now.Pulse;
                wind.windPulseFrequency = _now.PulseFrequency;
                float yaw = 70f + (Mathf.PerlinNoise(t * 0.01f, 1.3f) - 0.5f) * 90f;
                wind.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            if (terrain != null)
            {
                var d = terrain.terrainData;
                d.wavingGrassStrength = _now.GrassStrength * (0.8f + gust * 0.4f);
                d.wavingGrassAmount = _now.GrassAmount;
                d.wavingGrassSpeed = _now.GrassSpeed;
            }
            if (cycle != null && Mathf.Abs(cycle.Overcast - _now.Overcast) > 0.002f)
                cycle.Overcast = _now.Overcast;
            if (rain != null)
            {
                var cam = Camera.main;
                if (cam != null)
                    rain.transform.position = cam.transform.position + Vector3.up * 18f;
                var emission = rain.emission;
                emission.rateOverTime = _now.Rain < 0.02f || Sheltered ? 0f : _now.Rain * maxRainPerSecond;
                if (Sheltered && !_wasSheltered)
                    rain.Clear();   // the drops already falling round your head go too
                _wasSheltered = Sheltered;
                if (_now.Rain > 0.02f && !rain.isPlaying)
                    rain.Play();
            }
        }
    }
}
