using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Player
{
    // Lights of the player's car. Headlights come on by themselves at night and in heavy overcast or rain
    // (DayNightCycle), and L / left stick click switches them by hand. Brake lights glow whenever the brake is
    // pressed, the reversing light in R, the tail lights with the headlights. One spot light lights the road
    // ahead (Quest budget: no shadows); the lamp glass glows through its emissive material.
    public sealed class CarLights : MonoBehaviour
    {
        [SerializeField] DrivableCar car;
        [SerializeField] Light beam;
        [SerializeField] Material[] headlamps;
        [SerializeField] Material taillamp, reverseLamp;
        [SerializeField] Color headColour = new Color(1f, 0.95f, 0.85f);
        [SerializeField, Range(0f, 1f)] float nightThreshold = 0.3f;

        static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        Material[] _heads;
        Material _tail, _reverse;
        bool? _manual;
        bool _on;

        public bool HeadlightsOn => _on;
        public event System.Action<string> Message;

        public void Configure(DrivableCar drivable, Light spot, Material[] heads, Material tail, Material reverse)
        {
            car = drivable;
            beam = spot;
            headlamps = heads;
            taillamp = tail;
            reverseLamp = reverse;
        }

        // Per-car copies, so the shared material assets never change while playing.
        void Awake()
        {
            _heads = new Material[headlamps != null ? headlamps.Length : 0];
            for (int i = 0; i < _heads.Length; i++)
                _heads[i] = Copy(headlamps[i]);
            _tail = Copy(taillamp);
            _reverse = Copy(reverseLamp);
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    int h = headlamps != null ? System.Array.IndexOf(headlamps, mats[i]) : -1;
                    if (h >= 0 && mats[i] != null) { mats[i] = _heads[h]; changed = true; }
                    else if (taillamp != null && mats[i] == taillamp) { mats[i] = _tail; changed = true; }
                    else if (reverseLamp != null && mats[i] == reverseLamp) { mats[i] = _reverse; changed = true; }
                }
                if (changed)
                    r.sharedMaterials = mats;
            }
            Apply(false, false, false);
        }

        static Material Copy(Material m)
        {
            if (m == null)
                return null;
            var c = new Material(m);
            c.EnableKeyword("_EMISSION");
            return c;
        }

        void Update()
        {
            if (car == null)
                return;
            var sky = DayNightCycle.Instance;
            bool auto = sky != null && (sky.Night > nightThreshold || sky.Overcast > 0.75f);
            if (car.Driven && RigInput.LightsPressed)
            {
                _manual = !(_manual ?? auto);
                Message?.Invoke(_manual.Value ? "Headlights on." : "Headlights off.");
            }
            if (!car.Driven)
                _manual = null;
            bool on = car.Driven && (_manual ?? auto);
            Apply(on, car.Driven && car.BrakePedal > 0.05f, car.Driven && car.Reverse);
        }

        void Apply(bool head, bool braking, bool reversing)
        {
            _on = head;
            if (beam != null && beam.enabled != head)
                beam.enabled = head;
            if (_heads != null)
                foreach (var m in _heads)
                    if (m != null)
                        m.SetColor(Emission, head ? headColour * 3f : Color.black);
            if (_tail != null)
                _tail.SetColor(Emission, braking ? new Color(1f, 0.05f, 0.03f) * 4f : head ? new Color(1f, 0.05f, 0.03f) * 1.2f : Color.black);
            if (_reverse != null)
                _reverse.SetColor(Emission, reversing ? Color.white * 2.5f : Color.black);
        }
    }
}
