using System;
using System.Collections.Generic;
using UnityEngine;

namespace KR.Gameplay.World
{
    // Drives sun/moon, sky, ambient light, fog, glowing windows, street lamps and vehicle lights from a 24 h clock.
    // Kigali is near the equator: the sun rises in the east around 06:00 and sets in the west around 18:00.
    // All material changes are made on runtime clones, never on project assets.
    public sealed class DayNightCycle : MonoBehaviour
    {
        // A skybox that takes over from fromHour until the next key. A null sky means the scene's own
        // (procedural) skybox, whose sun disc follows the directional light.
        [Serializable]
        public sealed class SkyKey
        {
            public float fromHour;
            public Material sky;
            public float exposure = 1f;
        }

        // Built-in skyboxes cannot cross-blend, so the sky dims briefly around each switch instead.
        const float SwitchFadeHours = 0.25f;

        [SerializeField] Light sun;
        [SerializeField] InstancedScatter scatter;
        [SerializeField, Tooltip("City objects placed as GameObjects (editable): their window and sign materials glow at night.")]
        Transform[] cityRoots;
        [SerializeField] StreetLightPool streetLights;
        [SerializeField] Transform traffic;
        [SerializeField] SkyKey[] skies;
        [SerializeField, Range(0f, 24f)] float hour = 7.5f;
        [SerializeField, Tooltip("0 = clock only moves when set (Story Mode does this).")]
        float gameMinutesPerSecond;

        enum Glow { None, Window, Lamp, Tail, Sign }

        readonly List<(Material material, Glow glow)> _glowing = new List<(Material, Glow)>();
        readonly List<Material> _ownedClones = new List<Material>();
        Material _daySky;
        Material[] _skyClones;
        Material _originalSky;
        bool _ready;
        float _overcast;
        float _baseFogDensity;

        public float Hour
        {
            get => hour;
            set
            {
                hour = Mathf.Repeat(value, 24f);
                if (_ready)
                    Apply();
            }
        }

        public float Night { get; private set; }

        // 0 clear .. 1 heavy shower; set by WeatherCycle. Dims the sun, greys the sky and thickens the fog.
        public float Overcast
        {
            get => _overcast;
            set
            {
                _overcast = Mathf.Clamp01(value);
                if (_ready)
                    Apply();
            }
        }

        public float GameMinutesPerSecond
        {
            get => gameMinutesPerSecond;
            set => gameMinutesPerSecond = Mathf.Max(0f, value);
        }

        public static DayNightCycle Instance { get; private set; }

        void Awake() => Instance = this;

        public void Configure(Light sunLight, InstancedScatter instanced, StreetLightPool lamps, Transform trafficRoot,
                              SkyKey[] skyKeys, float startHour)
        {
            sun = sunLight;
            scatter = instanced;
            streetLights = lamps;
            traffic = trafficRoot;
            skies = skyKeys;
            hour = startHour;
        }

        void Start()
        {
            _originalSky = RenderSettings.skybox;
            _baseFogDensity = RenderSettings.fogDensity;
            if (_originalSky != null)
            {
                _daySky = new Material(_originalSky);
                _ownedClones.Add(_daySky);
            }
            if (skies != null && skies.Length > 0)
            {
                Array.Sort(skies, (a, b) => a.fromHour.CompareTo(b.fromHour));
                _skyClones = new Material[skies.Length];
                for (int i = 0; i < skies.Length; i++)
                {
                    if (skies[i].sky == null)
                        continue;
                    _skyClones[i] = new Material(skies[i].sky);
                    _ownedClones.Add(_skyClones[i]);
                }
            }

            if (scatter != null && scatter.RuntimeMaterials != null)
            {
                var seen = new HashSet<Material>();
                foreach (var m in scatter.RuntimeMaterials)
                    if (m != null && seen.Add(m))
                        Register(m);
            }
            if (cityRoots != null)
                GlowCity();
            if (traffic != null)
                CloneTrafficMaterials();
            _ready = true;
            Apply();
        }

        void OnDestroy()
        {
            if (_originalSky != null)
                RenderSettings.skybox = _originalSky;
            if (_baseFogDensity > 0f)
                RenderSettings.fogDensity = _baseFogDensity;
            foreach (var m in _ownedClones)
                Destroy(m);
        }

        void Update()
        {
            if (gameMinutesPerSecond > 0f)
                Hour = hour + gameMinutesPerSecond / 60f * Time.deltaTime;
        }

        static Glow Classify(string materialName)
        {
            string n = materialName.ToLowerInvariant();
            if (n.Contains("taillight"))
                return Glow.Tail;
            if (n.Contains("headlight") || n == "light" || n.Contains("street_light_glass") || n.Contains("pole-light") ||
                n.Contains("traffic-light-eu-light") || n == "emissive")
                return Glow.Lamp;
            if ((n.Contains("glass") && n != "glass") || n.Contains("window"))
                return Glow.Window;
            // Road signs are retroreflective: at night they show their own colours faintly from any distance.
            if (n.Contains("road_signs") || n.Contains("roadsign") || n.Contains("trafficprops") || n.Contains("traffic sign"))
                return Glow.Sign;
            return Glow.None;
        }

        public void ConfigureCity(Transform[] roots) => cityRoots = roots;

        // Each glowing material (windows, signs) is copied once and the copy shared by every renderer that used it,
        // so night lighting never touches the material assets.
        void GlowCity()
        {
            var clones = new Dictionary<Material, Material>();
            foreach (var root in cityRoots)
            {
                if (root == null)
                    continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null || Classify(m.name) == Glow.None)
                            continue;
                        if (!clones.TryGetValue(m, out var copy))
                        {
                            copy = new Material(m) { name = m.name };
                            clones[m] = copy;
                            _ownedClones.Add(copy);
                            Register(copy);
                        }
                        mats[i] = copy;
                        changed = true;
                    }
                    if (changed)
                        r.sharedMaterials = mats;
                }
            }
        }

        void Register(Material m)
        {
            var glow = Classify(m.name);
            if (glow == Glow.None || !m.HasProperty("_EmissionColor"))
                return;
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (glow == Glow.Sign && m.HasProperty("_BaseMap") && m.HasProperty("_EmissionMap"))
                m.SetTexture("_EmissionMap", m.GetTexture("_BaseMap"));   // glow in the sign's own colours
            _glowing.Add((m, glow));
        }

        void CloneTrafficMaterials()
        {
            var clones = new Dictionary<Material, Material>();
            foreach (var r in traffic.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || Classify(m.name) == Glow.None)
                        continue;
                    if (!clones.TryGetValue(m, out var clone))
                    {
                        clone = new Material(m) { name = m.name };
                        clones[m] = clone;
                        _ownedClones.Add(clone);
                        Register(clone);
                    }
                    mats[i] = clone;
                    changed = true;
                }
                if (changed)
                    r.sharedMaterials = mats;
            }
        }

        void Apply()
        {
            // Elevation: 1 at noon, 0 at 06:00/18:00, -1 at midnight.
            float elevation = Mathf.Sin((hour - 6f) / 12f * Mathf.PI);
            float day = Mathf.Clamp01(elevation * 4f);
            float twilight = Mathf.Clamp01(1f - Mathf.Abs(elevation) * 6f);
            Night = 1f - Mathf.Clamp01((elevation + 0.05f) * 8f);

            if (sun != null)
            {
                if (elevation > -0.05f)
                {
                    float progress = Mathf.Clamp01((hour - 6f) / 12f);
                    float pitch = Mathf.Max(2f, Mathf.Asin(Mathf.Clamp01(elevation)) * Mathf.Rad2Deg);
                    sun.transform.rotation = Quaternion.Euler(pitch, Mathf.Lerp(270f, 90f, progress), 0f);
                    sun.color = Color.Lerp(new Color(1f, 0.52f, 0.28f), new Color(1f, 0.95f, 0.87f), day);
                    sun.intensity = Mathf.Lerp(0.35f, 1.6f, day) * (1f - Night);
                }
                else
                {
                    sun.transform.rotation = Quaternion.Euler(55f, 200f, 0f);   // moonlight
                    sun.color = new Color(0.55f, 0.65f, 1f);
                    sun.intensity = 0.18f;
                }
            }

            Color Blend(Color nightC, Color twilightC, Color dayC) =>
                Color.Lerp(Color.Lerp(nightC, dayC, 1f - Night), twilightC, twilight * 0.55f);

            RenderSettings.ambientSkyColor = Blend(new Color(0.05f, 0.06f, 0.11f), new Color(0.50f, 0.38f, 0.42f), new Color(0.60f, 0.70f, 0.86f));
            RenderSettings.ambientEquatorColor = Blend(new Color(0.04f, 0.04f, 0.07f), new Color(0.50f, 0.36f, 0.30f), new Color(0.56f, 0.58f, 0.53f));
            RenderSettings.ambientGroundColor = Blend(new Color(0.02f, 0.02f, 0.03f), new Color(0.22f, 0.16f, 0.12f), new Color(0.26f, 0.25f, 0.19f));
            RenderSettings.fogColor = Blend(new Color(0.05f, 0.06f, 0.10f), new Color(0.82f, 0.56f, 0.42f), new Color(0.72f, 0.79f, 0.86f));

            if (_overcast > 0f)
            {
                float dim = Mathf.Lerp(1f, 0.55f, _overcast);
                if (sun != null)
                    sun.intensity *= Mathf.Lerp(1f, 0.3f, _overcast);
                RenderSettings.ambientSkyColor *= dim;
                RenderSettings.ambientEquatorColor *= dim;
                RenderSettings.ambientGroundColor *= dim;
                var rainGrey = new Color(0.5f, 0.54f, 0.58f) * Mathf.Lerp(0.15f, 1f, 1f - Night);
                RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, rainGrey, _overcast);
            }
            if (_baseFogDensity > 0f)
                RenderSettings.fogDensity = _baseFogDensity * (1f + 3f * _overcast);

            ApplySky(twilight);

            foreach (var (m, glow) in _glowing)
            {
                Color c = glow switch
                {
                    Glow.Window => new Color(1f, 0.75f, 0.45f) * (1.3f * Night),
                    Glow.Lamp => new Color(1f, 0.88f, 0.65f) * (5f * Night),
                    Glow.Sign => Color.white * (0.45f * Night),
                    _ => new Color(1f, 0.08f, 0.04f) * (3f * Night),
                };
                m.SetColor("_EmissionColor", c);
            }
            if (streetLights != null)
                streetLights.SetNight(Night);
        }

        void ApplySky(float twilight)
        {
            Material sky = _daySky;
            float exposure = 1f, dip = 1f;
            if (_skyClones != null)
            {
                int k = skies.Length - 1;
                for (int i = 0; i < skies.Length; i++)
                    if (skies[i].fromHour <= hour)
                        k = i;
                var next = skies[(k + 1) % skies.Length];
                float sinceStart = Mathf.Repeat(hour - skies[k].fromHour, 24f);
                float untilNext = Mathf.Repeat(next.fromHour - hour, 24f);
                dip = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(Mathf.Min(sinceStart, untilNext) / SwitchFadeHours));
                if (_skyClones[k] != null)
                {
                    sky = _skyClones[k];
                    exposure = skies[k].exposure;
                }
            }
            if (sky == null)
                return;

            if (sky == _daySky)
            {
                if (sky.HasProperty("_Exposure"))
                    sky.SetFloat("_Exposure", Mathf.Lerp(0.06f, 1.25f, Mathf.Max(1f - Night, twilight * 0.6f)) * dip);
                if (sky.HasProperty("_AtmosphereThickness"))
                    sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(0.85f, 1.7f, twilight));
            }
            else if (sky.HasProperty("_Exposure"))
                sky.SetFloat("_Exposure", exposure * dip * Mathf.Lerp(1f, 0.55f, _overcast));
            RenderSettings.skybox = sky;
        }
    }
}
