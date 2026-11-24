using System.Collections.Generic;
using UnityEngine;

namespace KR.Gameplay.World
{
    // Every short sound effect by name ("plate_down", "sip", "police_whistle"...), each with one or more takes
    // picked at random so repeats don't sound identical. Played through a small pool of reused AudioSources
    // (nothing is created while playing). One in the scene; filled in the Editor.
    public sealed class SoundBank : MonoBehaviour
    {
        [System.Serializable]
        public struct Entry
        {
            public string key;
            public AudioClip[] clips;
            [Range(0f, 1f)] public float volume;
        }

        [SerializeField] Entry[] entries;
        [SerializeField] int voices = 12;

        static SoundBank _instance;
        readonly Dictionary<string, int> _index = new Dictionary<string, int>();
        AudioSource[] _pool;
        int _next;

        public void Configure(Entry[] sounds) => entries = sounds;

        void Awake()
        {
            _instance = this;
            for (int i = 0; i < entries.Length; i++)
                if (!string.IsNullOrEmpty(entries[i].key))
                    _index[entries[i].key] = i;
            _pool = new AudioSource[voices];
            for (int i = 0; i < voices; i++)
            {
                var s = new GameObject($"Sfx {i}").AddComponent<AudioSource>();
                s.transform.SetParent(transform, false);
                s.playOnAwake = false;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 2f;
                s.maxDistance = 40f;
                s.dopplerLevel = 0f;
                _pool[i] = s;
            }
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        // One take of a sound at a place (spatial) or in your ears (spatial false). Quietly does nothing if the
        // sound isn't in the bank.
        public static void Play(string key, Vector3 at, float volume = 1f, bool spatial = true, float range = 40f)
        {
            var clip = Get(key, out float v);
            if (clip == null)
                return;
            var b = _instance;
            var s = b._pool[b._next];
            b._next = (b._next + 1) % b._pool.Length;
            s.transform.position = at;
            s.spatialBlend = spatial ? 1f : 0f;
            s.maxDistance = range;
            s.pitch = Random.Range(0.95f, 1.05f);
            s.clip = clip;
            s.volume = Mathf.Clamp01(v * volume);
            s.Play();
        }

        public static AudioClip Get(string key) => Get(key, out _);

        static AudioClip Get(string key, out float volume)
        {
            volume = 1f;
            var b = _instance;
            if (b == null || !b._index.TryGetValue(key, out int i))
                return null;
            var e = b.entries[i];
            volume = e.volume > 0f ? e.volume : 1f;
            return e.clips == null || e.clips.Length == 0 ? null : e.clips[Random.Range(0, e.clips.Length)];
        }

        // A dedicated looping source (for beds and engines), set up the same way as the pool.
        public static AudioSource Loop(Transform parent, string key, bool spatial, float minDistance = 3f, float maxDistance = 40f)
        {
            var clip = Get(key);
            if (clip == null)
                return null;
            var s = new GameObject($"Loop {key}").AddComponent<AudioSource>();
            s.transform.SetParent(parent, false);
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = spatial ? 1f : 0f;
            s.minDistance = minDistance;
            s.maxDistance = maxDistance;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.dopplerLevel = 0f;
            s.volume = 0f;
            return s;
        }

        // Fades a loop toward a volume and starts or stops it at the ends.
        public static void Fade(AudioSource s, float target, float rate)
        {
            if (s == null)
                return;
            s.volume = Mathf.MoveTowards(s.volume, target, Time.deltaTime * rate);
            if (s.volume > 0.001f && !s.isPlaying)
            {
                s.time = s.clip != null ? Random.value * s.clip.length * 0.8f : 0f;
                s.Play();
            }
            else if (s.volume <= 0.001f && s.isPlaying)
                s.Stop();
        }
    }
}
