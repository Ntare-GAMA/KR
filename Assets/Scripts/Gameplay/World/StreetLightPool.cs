using UnityEngine;

namespace KR.Gameplay.World
{
    // Real point lights for the street lamps nearest the camera; a pool is moved between lamp heads so
    // thousands of lamps can light the road at a fixed cost. Wave() switches lamps on outward from a point,
    // street by street, for the dusk moment in Story Mode.
    public sealed class StreetLightPool : MonoBehaviour
    {
        [SerializeField] Vector3[] lamps;
        [SerializeField, Tooltip("Quest budget: the nearest lamps get a real light; the rest glow only.")] int poolSize = 12;
        [SerializeField] float range = 28f;
        [SerializeField] float intensity = 70f;
        [SerializeField] Color color = new Color(1f, 0.82f, 0.58f);
        [SerializeField] float refreshInterval = 0.2f;

        Light[] _lights;
        int[] _nearest;
        float[] _nearestDist;
        float _timer;
        float _night;
        Vector3 _waveOrigin;
        float _waveStart = -1f;
        float _waveSpeed;

        public void Configure(Vector3[] lampPositions) => lamps = lampPositions;

        public void SetNight(float night) => _night = night;

        public void Wave(Vector3 origin, float metresPerSecond = 350f)
        {
            _waveOrigin = origin;
            _waveStart = Time.time;
            _waveSpeed = metresPerSecond;
        }

        public void CancelWave() => _waveStart = -1f;

        void Awake()
        {
            _lights = new Light[poolSize];
            _nearest = new int[poolSize];
            _nearestDist = new float[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject($"Lamp light {i}");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = range;
                l.color = color;
                l.shadows = LightShadows.None;
                l.enabled = false;
                _lights[i] = l;
            }
        }

        void Update()
        {
            bool on = _night > 0.02f && lamps != null && lamps.Length > 0;
            if (!on)
            {
                foreach (var l in _lights)
                    l.enabled = false;
                return;
            }
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _timer = refreshInterval;
                Refresh();
            }
            float waveRadius = _waveStart >= 0f ? (Time.time - _waveStart) * _waveSpeed : float.MaxValue;
            for (int i = 0; i < _lights.Length; i++)
            {
                var l = _lights[i];
                if (!l.enabled)
                    continue;
                float d = Vector3.Distance(l.transform.position, _waveOrigin);
                float reveal = _waveStart >= 0f ? Mathf.Clamp01((waveRadius - d) / 40f) : 1f;
                l.intensity = intensity * _night * reveal;
            }
        }

        // Keeps the poolSize nearest lamps with an insertion into a sorted fixed-size list.
        void Refresh()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            Vector3 eye = cam.transform.position;
            int filled = 0;
            for (int i = 0; i < lamps.Length; i++)
            {
                float d = (lamps[i] - eye).sqrMagnitude;
                if (filled == poolSize && d >= _nearestDist[poolSize - 1])
                    continue;
                int j = filled < poolSize ? filled++ : poolSize - 1;
                while (j > 0 && _nearestDist[j - 1] > d)
                {
                    _nearestDist[j] = _nearestDist[j - 1];
                    _nearest[j] = _nearest[j - 1];
                    j--;
                }
                _nearestDist[j] = d;
                _nearest[j] = i;
            }
            for (int i = 0; i < _lights.Length; i++)
            {
                bool used = i < filled;
                _lights[i].enabled = used;
                if (used)
                    _lights[i].transform.position = lamps[_nearest[i]];
            }
        }
    }
}
