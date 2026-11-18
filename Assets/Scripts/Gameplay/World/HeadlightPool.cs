using UnityEngine;

namespace KR.Gameplay.World
{
    // Real headlight spotlights for the vehicles nearest the camera at night.
    public sealed class HeadlightPool : MonoBehaviour
    {
        [SerializeField] RoadNetworkTraffic traffic;
        [SerializeField] int poolSize = 16;
        [SerializeField] float range = 35f;
        [SerializeField] float intensity = 150f;
        [SerializeField] Vector3 localOffset = new Vector3(0f, 0.75f, 2.3f);

        Light[] _lights;
        int[] _assigned;
        float _timer;

        public void Configure(RoadNetworkTraffic vehicles) => traffic = vehicles;

        void Awake()
        {
            _lights = new Light[poolSize];
            _assigned = new int[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject($"Headlight {i}");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.range = range;
                l.spotAngle = 70f;
                l.color = new Color(1f, 0.95f, 0.82f);
                l.shadows = LightShadows.None;
                l.enabled = false;
                _lights[i] = l;
                _assigned[i] = -1;
            }
        }

        void LateUpdate()
        {
            var cycle = DayNightCycle.Instance;
            float night = cycle != null ? cycle.Night : 0f;
            if (traffic == null || night < 0.05f)
            {
                foreach (var l in _lights)
                    l.enabled = false;
                return;
            }
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _timer = 0.5f;
                Assign();
            }
            for (int i = 0; i < _lights.Length; i++)
            {
                var body = traffic.Body(_assigned[i]);
                var l = _lights[i];
                l.enabled = body != null;
                if (body == null)
                    continue;
                l.transform.SetPositionAndRotation(body.TransformPoint(localOffset), body.rotation * Quaternion.Euler(8f, 0f, 0f));
                l.intensity = intensity * night;
            }
        }

        void Assign()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            Vector3 eye = cam.transform.position;
            for (int i = 0; i < _assigned.Length; i++)
                _assigned[i] = -1;
            var best = new float[_assigned.Length];
            for (int i = 0; i < best.Length; i++)
                best[i] = float.MaxValue;
            for (int c = 0; c < traffic.Count; c++)
            {
                var body = traffic.Body(c);
                if (body == null)
                    continue;
                float d = (body.position - eye).sqrMagnitude;
                int worst = 0;
                for (int i = 1; i < best.Length; i++)
                    if (best[i] > best[worst])
                        worst = i;
                if (d < best[worst])
                {
                    best[worst] = d;
                    _assigned[worst] = c;
                }
            }
        }
    }
}
