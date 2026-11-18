using UnityEngine;
using UnityEngine.InputSystem;

namespace KR.Gameplay.Player
{
    // Orbit camera behind a target (the player, or the car while driving); the mouse turns it, colliders pull it in.
    // While following a moving car it drifts back behind it when the mouse is idle.
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        [SerializeField] PlayerTuning settings;
        [SerializeField] Transform target;
        [SerializeField] LayerMask blockers = ~(1 << 2);

        float _yaw, _pitch = 12f, _distance, _wantDistance, _height;
        float _idle;
        Vector3 _last;
        Camera _cam;

        public bool Look { get; set; } = true;

        public void Configure(PlayerTuning tuning, Transform follow)
        {
            settings = tuning;
            target = follow;
        }

        public void Follow(Transform follow, float distance, float height)
        {
            target = follow;
            _wantDistance = distance;
            _height = height;
            SnapBehind();
            if (target != null)
                _last = target.position;
        }

        public void FollowDefault(Transform follow) => Follow(follow, settings.cameraDistance, settings.cameraHeight);

        void OnEnable()
        {
            _cam = GetComponent<Camera>();
            if (_cam != null)
                _cam.fieldOfView = 58f;
            if (settings == null)
                return;
            if (_wantDistance <= 0f)
            {
                _wantDistance = settings.cameraDistance;
                _height = settings.cameraHeight;
            }
            _distance = _wantDistance;
            SnapBehind();
        }

        public void SnapBehind()
        {
            if (target != null)
                _yaw = target.eulerAngles.y;
        }

        void LateUpdate()
        {
            if (target == null || settings == null)
                return;
            var mouse = Mouse.current;
            Vector2 d = Look && mouse != null ? mouse.delta.ReadValue() * settings.mouseSensitivity : Vector2.zero;
            _yaw += d.x;
            _pitch = Mathf.Clamp(_pitch - d.y, settings.minPitch, settings.maxPitch);

            float moved = (target.position - _last).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            _last = target.position;
            _idle = d.sqrMagnitude > 0.01f ? 0f : _idle + Time.deltaTime;
            if (_wantDistance > settings.cameraDistance + 0.5f && _idle > 1.2f && moved > 2f)
                _yaw = Mathf.LerpAngle(_yaw, target.eulerAngles.y, 1f - Mathf.Exp(-2.5f * Time.deltaTime));

            Vector3 pivot = target.position + Vector3.up * _height;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            float want = _wantDistance;
            // The player sits on Ignore Raycast, which the default mask leaves out.
            if (Physics.SphereCast(pivot, 0.25f, rot * Vector3.back, out var hit, want, blockers, QueryTriggerInteraction.Ignore) &&
                !hit.transform.IsChildOf(target))
                want = Mathf.Max(0.6f, hit.distance - 0.1f);
            _distance = want < _distance ? want : Mathf.Lerp(_distance, want, 1f - Mathf.Exp(-6f * Time.deltaTime));
            transform.SetPositionAndRotation(pivot + rot * new Vector3(0.45f, 0f, -_distance), rot);
        }
    }
}
