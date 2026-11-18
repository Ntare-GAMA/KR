using UnityEngine;

namespace KR.Gameplay.World
{
    // Moves a person (or ball carrier) between random points inside an oriented rectangle, following the terrain.
    // Used for market shoppers, park strollers and football players.
    public sealed class Wanderer : MonoBehaviour
    {
        [SerializeField] Vector3 centre;
        [SerializeField] Vector2 halfSize = new Vector2(5f, 5f);
        [SerializeField] float yaw;
        [SerializeField] float speed = 1.2f;
        [SerializeField] float lift;
        [SerializeField, Range(0f, 1f), Tooltip("Chance of stopping for a while at each point (browsing, chatting, catching breath).")]
        float pauseChance;
        [SerializeField] Vector2 pauseSeconds = new Vector2(2f, 6f);

        Vector3 _target;
        Terrain _terrain;
        float _pauseUntil;

        public void Configure(Vector3 areaCentre, Vector2 areaHalfSize, float areaYaw, float moveSpeed, float heightLift = 0f,
                              float stopChance = 0f, Vector2 stopSeconds = default)
        {
            centre = areaCentre;
            halfSize = areaHalfSize;
            yaw = areaYaw;
            speed = moveSpeed;
            lift = heightLift;
            pauseChance = stopChance;
            if (stopSeconds != default)
                pauseSeconds = stopSeconds;
        }

        void Start()
        {
            _terrain = Terrain.activeTerrain;
            PickTarget();
        }

        void Update()
        {
            if (Time.time < _pauseUntil)
                return;
            Vector3 p = transform.position;
            Vector3 to = _target - p;
            to.y = 0f;
            if (to.magnitude < 0.4f)
            {
                if (Random.value < pauseChance)
                    _pauseUntil = Time.time + Random.Range(pauseSeconds.x, pauseSeconds.y);
                PickTarget();
                return;
            }
            p += to.normalized * (speed * Time.deltaTime);
            if (_terrain != null)
                p.y = _terrain.SampleHeight(p) + _terrain.transform.position.y + lift;
            transform.SetPositionAndRotation(p, Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 6f));
        }

        void PickTarget()
        {
            var local = new Vector3(Random.Range(-halfSize.x, halfSize.x), 0f, Random.Range(-halfSize.y, halfSize.y));
            _target = centre + Quaternion.Euler(0f, yaw, 0f) * local;
        }
    }
}
