using UnityEngine;

namespace KR.Gameplay.World
{
    // Walks a person back and forth along a fixed route (points already on the footpath surface), stopping at each
    // end for a while: police on foot patrol, guards pacing a gate, students between campus buildings.
    // PersonAnimator on the same object turns the movement into the walk clip.
    public sealed class StreetWalker : MonoBehaviour
    {
        [SerializeField] Vector3[] points;
        [SerializeField] float speed = 1.2f;
        [SerializeField] Vector2 pauseSeconds = new Vector2(4f, 12f);

        int _index;
        int _step = 1;
        float _pauseUntil;

        public void Configure(Vector3[] route, float walkSpeed, Vector2 pause)
        {
            points = route;
            speed = walkSpeed;
            pauseSeconds = pause;
        }

        void OnEnable()
        {
            if (points == null || points.Length < 2)
            {
                enabled = false;
                return;
            }
            // Start somewhere along the route so walkers sharing a route are spread out.
            _index = Random.Range(0, points.Length - 1);
            transform.position = points[_index];
        }

        void Update()
        {
            if (Time.time < _pauseUntil)
                return;
            int next = _index + _step;
            if (next < 0 || next >= points.Length)
            {
                _step = -_step;
                _pauseUntil = Time.time + Random.Range(pauseSeconds.x, pauseSeconds.y);
                return;
            }
            Vector3 p = transform.position, target = points[next];
            Vector3 to = target - p;
            float move = speed * Time.deltaTime;
            if (to.magnitude <= move)
            {
                transform.position = target;
                _index = next;
                return;
            }
            transform.position = p + to.normalized * move;
            to.y = 0f;
            if (to.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 6f);
        }
    }
}
