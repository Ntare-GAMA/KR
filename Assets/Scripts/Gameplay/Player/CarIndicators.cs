using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Player
{
    // Turn indicators of the player's car. Z / X (desktop) or a flick of the right stick left / right (headset,
    // while driving) signals left or right; the same again cancels, the other way switches. Like a real car they
    // cancel themselves once the steering wheel comes back after a turn. The orange lamps on the car (PolyDrive
    // Left_Indicator / Right_Indicator) and the green arrows in the instrument cluster flash about 80 times a
    // minute, with the relay's tick and tock (made here, no recording needed).
    // Added to the car when the scene loads.
    public sealed class CarIndicators : MonoBehaviour
    {
        const float Period = 0.75f;
        const float ArmAngle = 14f, CancelAngle = 3f;   // road-wheel degrees
        static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        static readonly Color Amber = new Color(1f, 0.45f, 0.05f), Green = new Color(0.2f, 1f, 0.3f);

        DrivableCar _car;
        Material[] _left = System.Array.Empty<Material>(), _right = System.Array.Empty<Material>();
        Material _dashLeft, _dashRight;
        AudioSource _relay;
        AudioClip _tick, _tock;
        float _clock;
        bool _armed, _lit;

        // -1 left, 0 off, +1 right.
        public int Signal { get; private set; }
        // True while the lamps are on in their flash.
        public bool Lit => _lit;

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
            if (rig == null || rig.Car == null || rig.Car.GetComponent<CarIndicators>() != null)
                return;
            rig.Car.gameObject.AddComponent<CarIndicators>().Setup(rig.Car);
        }

        void Setup(DrivableCar car)
        {
            _car = car;
            // Per-car copies of the lamp materials (every LOD, the boot lid too), so only this car flashes.
            var copies = new System.Collections.Generic.Dictionary<Material, Material>();
            var left = new System.Collections.Generic.List<Material>();
            var right = new System.Collections.Generic.List<Material>();
            var arrows = new System.Collections.Generic.List<(float x, Material m)>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null)
                        continue;
                    bool l = m.name.Contains("Left_Indicator"), rt = m.name.Contains("Right_Indicator");
                    bool arrow = r.name.StartsWith("Dashboard_Icon") && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null &&
                                 m.GetTexture("_BaseMap").name.Contains("Turn_indicator");
                    if (!l && !rt && !arrow)
                        continue;
                    if (!copies.TryGetValue(m, out var copy))
                    {
                        copy = new Material(m) { name = m.name + " (flashing)" };
                        copy.EnableKeyword("_EMISSION");
                        if (arrow)
                            copy.SetTexture("_EmissionMap", m.GetTexture("_BaseMap"));   // only the arrow glows
                        copy.SetColor(Emission, Color.black);
                        copies[m] = copy;
                        if (l) left.Add(copy);
                        if (rt) right.Add(copy);
                    }
                    mats[i] = copy;
                    changed = true;
                    if (arrow)
                        arrows.Add((transform.InverseTransformPoint(r.bounds.center).x, copy));
                }
                if (changed)
                    r.sharedMaterials = mats;
            }
            // The car's left is local -X: the left arrow is the one further left.
            arrows.Sort((a, b) => a.x.CompareTo(b.x));
            if (arrows.Count >= 2)
            {
                _dashLeft = arrows[0].m;
                _dashRight = arrows[arrows.Count - 1].m;
            }
            _left = left.ToArray();
            _right = right.ToArray();

            _tick = Click("Indicator tick", 2300f, 1f);
            _tock = Click("Indicator tock", 1700f, 0.8f);
            var go = new GameObject("Indicator relay");
            go.transform.SetParent(transform, false);
            var cluster = CarGeometry.Cluster;
            go.transform.localPosition = cluster != null ? cluster.centre : new Vector3(-0.35f, 1f, 0.5f);
            _relay = go.AddComponent<AudioSource>();
            _relay.playOnAwake = false;
            _relay.spatialBlend = 0.7f;
            _relay.minDistance = 0.5f;
            _relay.maxDistance = 6f;
            _relay.volume = 0.55f;
        }

        // A relay click: a sharp, quickly fading burst of tone and noise.
        static AudioClip Click(string name, float pitch, float level)
        {
            const int rate = 44100;
            int n = rate * 30 / 1000;
            var data = new float[n];
            var rng = new System.Random(name.GetHashCode());
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float env = Mathf.Exp(-t / 0.0035f);
                float noise = (float)rng.NextDouble() * 2f - 1f;
                data[i] = level * env * (0.55f * Mathf.Sin(2f * Mathf.PI * pitch * t) + 0.45f * noise) +
                          level * 0.25f * Mathf.Exp(-t / 0.012f) * Mathf.Sin(2f * Mathf.PI * 420f * t);
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Update()
        {
            if (_car == null)
                return;
            if (!_car.Driven)
            {
                Set(0);
            }
            else
            {
                int step = RigInput.IndicatorStep;
                if (step != 0)
                    Set(Signal == step ? 0 : step);
                // Self-cancelling: armed by a real turn the way you signalled, cancelled when the wheel comes back.
                if (Signal != 0)
                {
                    float angle = _car.SteerAngle * Signal;
                    if (angle > ArmAngle)
                        _armed = true;
                    else if (_armed && Mathf.Abs(_car.SteerAngle) < CancelAngle)
                        Set(0);
                }
            }

            bool lit = false;
            if (Signal != 0)
            {
                _clock += Time.deltaTime;
                lit = (_clock % Period) < Period * 0.5f;
            }
            if (lit != _lit)
            {
                _lit = lit;
                if (_relay != null && !GamePause.Paused)
                    _relay.PlayOneShot(lit ? _tick : _tock);
                Apply();
            }
        }

        void Set(int signal)
        {
            if (signal == Signal)
                return;
            Signal = signal;
            _armed = false;
            _clock = 0f;
            if (signal == 0 && _lit)
            {
                _lit = false;
                if (_relay != null)
                    _relay.PlayOneShot(_tock);
            }
            Apply();
        }

        void Apply()
        {
            Color lamp = Amber * 4f, arrow = Green * 3f;
            bool left = _lit && Signal < 0, right = _lit && Signal > 0;
            foreach (var m in _left)
                m.SetColor(Emission, left ? lamp : Color.black);
            foreach (var m in _right)
                m.SetColor(Emission, right ? lamp : Color.black);
            if (_dashLeft != null)
                _dashLeft.SetColor(Emission, left ? arrow : Color.black);
            if (_dashRight != null)
                _dashRight.SetColor(Emission, right ? arrow : Color.black);
        }
    }
}
