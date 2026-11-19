using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Player
{
    // Footsteps while you walk: looped recordings of steps whose volume follows your speed and whose pitch follows
    // your pace. The ground decides the sound: gravel on bare ground (the terrain), boots on pavement and roads,
    // wet steps when it rains. Silent when seated, in the car or standing still.
    [RequireComponent(typeof(CharacterController))]
    public sealed class Footsteps : MonoBehaviour
    {
        [SerializeField] AudioClip steps;                 // fallback when the SoundBank has no step sounds
        [SerializeField] float volume = 0.45f, walkSpeed = 1.4f;

        CharacterController _cc;
        AudioSource _src;
        Vector3 _last;
        float _speed, _nextGround;
        WeatherCycle _weather;
        AudioClip _gravel, _pavement, _wet;

        public void Configure(AudioClip clip) => steps = clip;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _src = gameObject.AddComponent<AudioSource>();
            _src.clip = steps;
            _src.loop = true;
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
            _src.volume = 0f;
            _last = transform.position;
        }

        void Start()
        {
            _weather = FindFirstObjectByType<WeatherCycle>();
            _gravel = SoundBank.Get("steps_gravel");
            _pavement = SoundBank.Get("steps_pavement");
            _wet = SoundBank.Get("steps_wet");
        }

        void OnDisable()
        {
            if (_src != null)
                _src.Stop();
        }

        void Update()
        {
            if (Time.deltaTime <= 0f)
                return;
            var d = transform.position - _last;
            _last = transform.position;
            d.y = 0f;
            // Teleports and seat jumps are not steps.
            float v = d.magnitude / Time.deltaTime;
            if (v > 12f)
                v = 0f;
            _speed = Mathf.Lerp(_speed, v, Time.deltaTime * 8f);
            bool walking = _cc.enabled && _cc.isGrounded && _speed > 0.35f;

            if (walking && Time.time >= _nextGround)
            {
                _nextGround = Time.time + 0.4f;
                var clip = Ground();
                if (clip != null && clip != _src.clip)
                {
                    _src.clip = clip;
                    _src.time = Random.value * clip.length * 0.8f;
                    if (_src.volume > 0.001f)
                        _src.Play();
                }
            }
            if (_src.clip == null)
                return;
            _src.volume = Mathf.MoveTowards(_src.volume, walking ? volume * Mathf.Clamp01(_speed / walkSpeed) : 0f, Time.deltaTime * 5f);
            _src.pitch = Mathf.Clamp(_speed / walkSpeed, 0.8f, 1.5f);
            if (_src.volume > 0.001f && !_src.isPlaying)
                _src.Play();
            else if (_src.volume <= 0.001f && _src.isPlaying)
                _src.Pause();
        }

        AudioClip Ground()
        {
            if (_weather != null && _weather.Rain01 > 0.3f && _wet != null)
                return _wet;
            bool bare = Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out var hit, 1.5f, ~(1 << 2), QueryTriggerInteraction.Ignore)
                        && hit.collider is TerrainCollider;
            var pick = bare ? _gravel : _pavement;
            return pick != null ? pick : steps;
        }
    }
}
