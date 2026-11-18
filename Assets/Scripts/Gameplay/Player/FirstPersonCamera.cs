using UnityEngine;
using UnityEngine.InputSystem;

namespace KR.Gameplay.Player
{
    // Desktop first person (the headset replaces this with the tracked head). Walking: the camera sits at eye
    // height and the mouse turns the view. Driving: the camera sits at the driver's
    // eye point looking out over the bonnet, the mouse looks around (it drifts back to straight ahead), and the
    // view stays level when the car pitches and rolls.
    [RequireComponent(typeof(Camera))]
    public sealed class FirstPersonCamera : MonoBehaviour
    {
        [SerializeField] PlayerTuning settings;
        [SerializeField] PlayerRig rig;
        [SerializeField] float maxLookAside = 110f;
        [SerializeField] float recentre = 1.5f;
        [SerializeField] float eyeHeight = 1.62f;

        float _yaw, _pitch, _lookYaw, _idle;
        Camera _cam;

        public float Yaw => _yaw;

        public void Configure(PlayerTuning tuning, PlayerRig playerRig)
        {
            settings = tuning;
            rig = playerRig;
        }

        void OnEnable()
        {
            _cam = GetComponent<Camera>();
            _cam.nearClipPlane = 0.05f;   // inside the cabin the windscreen is close
            _cam.fieldOfView = 70f;
            if (rig != null && rig.Player != null)
                _yaw = rig.Player.transform.eulerAngles.y;
        }

        void LateUpdate()
        {
            if (rig == null || rig.Player == null)
                return;
            Vector2 d = Vector2.zero;
            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
                d = mouse.delta.ReadValue() * (settings != null ? settings.mouseSensitivity : 0.12f);
            _pitch = Mathf.Clamp(_pitch - d.y, -70f, 75f);

            var car = rig.CurrentMode == PlayerRig.Mode.Driving ? rig.Car : null;
            if (car != null)
            {
                // Look around inside the car; with the mouse idle the view settles back on the road ahead.
                _idle = d.sqrMagnitude > 0.01f ? 0f : _idle + Time.deltaTime;
                _lookYaw = Mathf.Clamp(_lookYaw + d.x, -maxLookAside, maxLookAside);
                if (_idle > 1f)
                {
                    float k = 1f - Mathf.Exp(-recentre * Time.deltaTime);
                    _lookYaw = Mathf.Lerp(_lookYaw, 0f, k);
                    _pitch = Mathf.Lerp(_pitch, 4f, k);
                }
                float carYaw = car.transform.eulerAngles.y;
                transform.SetPositionAndRotation(car.EyePoint, Quaternion.Euler(_pitch, carYaw + _lookYaw, 0f));
                _yaw = carYaw + _lookYaw;
                return;
            }

            _lookYaw = 0f;
            _yaw += d.x;
            Vector3 eye = rig.Player.transform.position + Vector3.up * eyeHeight;
            transform.SetPositionAndRotation(eye, Quaternion.Euler(_pitch, _yaw, 0f));
        }
    }
}
