using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Player
{
    // Working mirrors on the player's car: the rear-view mirror and both door mirrors.
    // One small camera at the back of the car, looking back, renders the road behind into a texture (about 36 times
    // a second, only while you drive). Each mirror glass gets a quad whose texture coordinates are worked out every
    // frame by reflecting the ray from your eyes to each corner of the glass, so each mirror shows its own slice of
    // the view, mirrored, and the view shifts when you move your head, as in a real mirror. The mirrors are set the
    // way a driver adjusts them: the rear-view straight back through the rear window, the door mirrors a little
    // outward to cover the lanes beside you.
    // The glass positions come from CarGeometry (measured in the Editor). Added to the car when the scene loads.
    public sealed class CarMirrors : MonoBehaviour
    {
        // The three mirrors share one image, so each shows only a slice of it: the image must be large enough that a
        // slice is still sharp. Smaller on the headset, where it costs more.
        static int Width => VrRig.Active ? 1280 : 1792;
        static int Height => VrRig.Active ? 480 : 672;
        const float VerticalFov = 22f, Far = 160f, QuestFar = 110f;
        static int Every => KR.Gameplay.Diagnostics.QuestPerformance.Applies ? 3 : 2;

        sealed class Glass
        {
            public Vector3[] corners;   // car-local
            public Vector3 normal;      // car-local, as adjusted by the driver
            public Mesh mesh;
            public GameObject go;
        }

        DrivableCar _car;
        Camera _cam;
        RenderTexture _rt;
        Material _mat;
        readonly List<Glass> _glass = new List<Glass>();
        readonly List<Vector2> _uv = new List<Vector2>(4) { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero };
        bool _shown;
        int _frame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var rig = Object.FindFirstObjectByType<PlayerRig>();
            if (rig != null && rig.Car != null)
                Attach(rig.Car);
        }

        // Also used by the Editor's car renders.
        public static CarMirrors Attach(DrivableCar car)
        {
            if (car == null || CarGeometry.Mirrors.Length == 0)
                return null;
            var m = car.GetComponent<CarMirrors>();
            if (m == null)
            {
                m = car.gameObject.AddComponent<CarMirrors>();
                m.Build(car);
            }
            return m;
        }

        void Build(DrivableCar car)
        {
            _car = car;
            var t = car.transform;

            // Car length from its meshes (car-local), to put the camera just behind the rear bumper.
            float rear = -2.3f, top = 1.4f;
            bool any = false;
            foreach (var f in GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null || !f.name.Contains("LOD0"))
                    continue;
                var b = f.sharedMesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var p = t.InverseTransformPoint(f.transform.TransformPoint(b.center + Vector3.Scale(b.extents,
                        new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f))));
                    rear = any ? Mathf.Min(rear, p.z) : p.z;
                    top = any ? Mathf.Max(top, p.y) : p.y;
                    any = true;
                }
            }

            _rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                name = "Car mirrors", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                antiAliasing = 4,   // MSAA; no mipmaps (an MSAA texture cannot have them, and the glass magnifies it)
            };
            var camGo = new GameObject("Mirror camera (looking back)");
            camGo.transform.SetParent(t, false);
            Vector3 eye = t.InverseTransformPoint(car.EyePoint);
            camGo.transform.localPosition = new Vector3(0f, Mathf.Min(top, eye.y + 0.1f), rear - 0.15f);
            camGo.transform.localRotation = Quaternion.Euler(2f, 180f, 0f);
            _cam = camGo.AddComponent<Camera>();
            _cam.enabled = false;                       // drawn by hand
            _cam.stereoTargetEye = StereoTargetEyeMask.None;
            _cam.fieldOfView = VerticalFov;
            _cam.aspect = (float)Width / Height;
            _cam.nearClipPlane = 0.2f;
            _cam.farClipPlane = Far;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
            _cam.targetTexture = _rt;
            var data = _cam.GetUniversalAdditionalCameraData();
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.requiresColorOption = CameraOverrideOption.Off;
            // Standalone headset: the mirror draws only the road, cars, people and buildings, nearer, and every third
            // frame (every second on a desktop). It was a second full city draw, a large share of the Quest's
            // 1,009 draw calls (Logs/QuestBench benchmark, 2026-11-22).
            if (KR.Gameplay.Diagnostics.QuestPerformance.Applies)
            {
                foreach (var layer in new[] { "PcDetail", "PcTrees", "StreetDressing" })
                {
                    int l = LayerMask.NameToLayer(layer);
                    if (l >= 0)
                        _cam.cullingMask &= ~(1 << l);
                }
                _cam.farClipPlane = QuestFar;
            }

            _mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Car mirror glass" };
            _mat.SetTexture("_BaseMap", _rt);
            _mat.SetColor("_BaseColor", new Color(0.88f, 0.9f, 0.92f));
            _mat.SetFloat("_Cull", 0f);

            // Where each mirror should point (car-local, as reflected): straight back, or a little outward.
            foreach (var g in CarGeometry.Mirrors)
            {
                float side = g.centre.x;
                Vector3 aim = Mathf.Abs(side) < 0.3f ? new Vector3(0f, -0.02f, -1f)
                            : new Vector3(Mathf.Sign(side) * (side > 0f ? 0.26f : 0.2f), -0.05f, -1f);
                Vector3 incoming = (g.centre - eye).normalized;
                Vector3 normal = (aim.normalized - incoming).normalized;   // the glass angle that reflects the eye ray onto aim
                if (Vector3.Dot(normal, eye - g.centre) < 0f)
                    normal = -normal;

                Vector3 c = g.centre + g.normal * 0.004f;
                Vector3 r = g.right * (g.width * 0.48f), u = g.up * (g.height * 0.46f);
                var glass = new Glass
                {
                    corners = new[] { c - r - u, c + r - u, c + r + u, c - r + u },
                    normal = normal,
                    mesh = new Mesh { name = "Mirror glass" },
                };
                glass.mesh.SetVertices(glass.corners);
                glass.mesh.SetUVs(0, _uv);
                glass.mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
                glass.mesh.RecalculateNormals();
                glass.mesh.bounds = new Bounds(g.centre, Vector3.one * 0.5f);
                glass.go = new GameObject(Mathf.Abs(side) < 0.3f ? "Rear-view mirror" : side < 0f ? "Left door mirror" : "Right door mirror");
                glass.go.transform.SetParent(t, false);
                glass.go.AddComponent<MeshFilter>().sharedMesh = glass.mesh;
                var mr = glass.go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                glass.go.SetActive(false);
                _glass.Add(glass);
            }
        }

        void LateUpdate()
        {
            bool show = _car != null && _car.Driven;
            if (show != _shown)
            {
                _shown = show;
                foreach (var g in _glass)
                    g.go.SetActive(show);
            }
            if (!show)
                return;
            var main = Camera.main;
            Refresh(main != null ? main.transform.position : _car.EyePoint, ++_frame % Every == 0);
        }

        // Points each mirror at the right part of the view from this eye position; optionally draws the view.
        public void Refresh(Vector3 eyeWorld, bool render)
        {
            if (_cam == null)
                return;
            if (render)
                _cam.Render();
            var t = _car.transform;
            Vector3 eye = t.InverseTransformPoint(eyeWorld);
            Vector3 from = _cam.transform.position;
            foreach (var g in _glass)
            {
                for (int i = 0; i < 4; i++)
                {
                    Vector3 reflected = Vector3.Reflect(g.corners[i] - eye, g.normal);
                    Vector3 vp = _cam.WorldToViewportPoint(from + t.TransformDirection(reflected.normalized) * 100f);
                    _uv[i] = vp.z > 0f ? new Vector2(vp.x, vp.y) : _uv[i];
                }
                g.mesh.SetUVs(0, _uv);
            }
        }

        public RenderTexture View => _rt;
        public Camera ViewCamera => _cam;

        // Shows the mirrors outside play (the Editor's car renders).
        public void ShowForCapture(Vector3 eyeWorld)
        {
            foreach (var g in _glass)
                g.go.SetActive(true);
            Refresh(eyeWorld, true);
        }

        void OnDestroy()
        {
            if (_rt != null)
                _rt.Release();
        }
    }
}
