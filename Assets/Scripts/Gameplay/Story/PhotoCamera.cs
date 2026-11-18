using System;
using System.IO;
using System.Threading.Tasks;
using KR.Gameplay.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace KR.Gameplay.Story
{
    // A camera you hold while walking. Right grip raises it (C on a desktop), the right trigger (left click) takes
    // the photo. The viewfinder is a small, low-resolution live view updated a few times a second; the photo
    // itself is drawn once at the shutter and read back and saved in the background, so taking a photo does not
    // stall the headset. Photos are saved only on the device (no upload), with place, mission and time.
    public sealed class PhotoCamera : MonoBehaviour
    {
        [SerializeField] PlayerRig rig;
        [SerializeField] StoryDirector director;
        [SerializeField] Material bodyMaterial;
        [SerializeField] int photoWidth = 1280, photoHeight = 960;

        Camera _lens;
        RenderTexture _viewfinder, _photo;
        GameObject _body;
        Renderer _screen;
        bool _raised, _desktopToggle, _saving;
        int _frame;
        float _flash;

        public static string Folder => Path.Combine(Application.persistentDataPath, "photos");
        public event Action<PhotoRecord, Camera> Taken;

        public void Configure(PlayerRig playerRig, StoryDirector story, Material body)
        {
            rig = playerRig;
            director = story;
            bodyMaterial = body;
        }

        void Start()
        {
            _body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(_body.GetComponent<Collider>());
            _body.name = "Photo camera";
            _body.transform.localScale = new Vector3(0.12f, 0.075f, 0.04f);
            _body.GetComponent<Renderer>().sharedMaterial = bodyMaterial;
            _body.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(screen.GetComponent<Collider>());
            screen.name = "Viewfinder";
            screen.transform.SetParent(_body.transform, false);
            screen.transform.localPosition = new Vector3(0f, 0f, -0.51f);       // the side facing you
            screen.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            screen.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
            _screen = screen.GetComponent<Renderer>();
            _screen.shadowCastingMode = ShadowCastingMode.Off;
            _screen.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));

            var lensGo = new GameObject("Lens");
            lensGo.transform.SetParent(_body.transform, false);
            lensGo.transform.localPosition = new Vector3(0f, 0f, 0.6f);
            _lens = lensGo.AddComponent<Camera>();
            _lens.enabled = false;                 // drawn by hand, only when needed
            _lens.fieldOfView = 55f;
            _lens.nearClipPlane = 0.1f;
            _lens.farClipPlane = 600f;
            _lens.stereoTargetEye = StereoTargetEyeMask.None;
            _viewfinder = new RenderTexture(256, 192, 16) { name = "Viewfinder" };
            _photo = new RenderTexture(photoWidth, photoHeight, 24) { name = "Photo", antiAliasing = 2 };
            _screen.material.SetTexture("_BaseMap", _viewfinder);
            _body.SetActive(false);
        }

        void Update()
        {
            if (rig == null)
                return;
            bool walking = rig.CurrentMode == PlayerRig.Mode.Walking && !GamePause.Paused;
            if (!VrRig.Active && RigInput.CameraKeyPressed)
                _desktopToggle = !_desktopToggle;
            bool want = walking && (VrRig.Active ? RigInput.RightGrip > 0.6f : _desktopToggle);
            if (want != _raised)
            {
                _raised = want;
                _body.SetActive(want);
                rig.Busy = want;
            }
            if (!_raised)
                return;

            // In the right hand (VR) or held out in front of you (desktop), level with the horizon.
            Transform hand = VrRig.Active ? rig.VrRightHand : Camera.main != null ? Camera.main.transform : null;
            if (hand == null)
                return;
            Vector3 fwd = hand.forward;
            Vector3 pos = VrRig.Active ? hand.position + fwd * 0.06f : hand.position + fwd * 0.45f - hand.up * 0.12f;
            _body.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up));

            // Live view at about 20 frames a second, small, so it costs little.
            if (++_frame % 3 == 0)
            {
                _lens.targetTexture = _viewfinder;
                _lens.Render();
            }
            _flash = Mathf.MoveTowards(_flash, 0f, Time.unscaledDeltaTime * 4f);
            _screen.material.SetColor("_BaseColor", Color.Lerp(Color.white, Color.white * 3f, _flash));

            if (RigInput.ShutterPressed && !_saving)
                Shoot();
        }

        void Shoot()
        {
            _flash = 1f;
            var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (device.isValid)
                device.SendHapticImpulse(0, 0.5f, 0.06f);
            _lens.targetTexture = _photo;
            _lens.Render();
            _lens.targetTexture = _viewfinder;

            var record = new PhotoRecord
            {
                file = $"photo_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png",
                missionId = director != null && director.Current != null ? director.Current.id : null,
                takenUtc = DateTime.UtcNow.ToString("o"),
            };
            Taken?.Invoke(record, _lens);   // missions and places check the shot now, while the lens is in place

            // Read back and encode off the main thread; the frame never waits for the file.
            _saving = true;
            string path = Path.Combine(Folder, record.file);
            int w = _photo.width, h = _photo.height;
            AsyncGPUReadback.Request(_photo, 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError)
                {
                    _saving = false;
                    return;
                }
                var pixels = request.GetData<byte>().ToArray();
                Task.Run(() =>
                {
                    try
                    {
                        Directory.CreateDirectory(Folder);
                        var png = ImageConversion.EncodeArrayToPNG(pixels, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, (uint)w, (uint)h);
                        File.WriteAllBytes(path, png);
                    }
                    catch (Exception)
                    {
                        // A failed write keeps the record; the gallery shows it as missing.
                    }
                    _saving = false;
                });
            });
        }

        void OnDestroy()
        {
            if (_viewfinder != null) _viewfinder.Release();
            if (_photo != null) _photo.Release();
        }
    }
}
