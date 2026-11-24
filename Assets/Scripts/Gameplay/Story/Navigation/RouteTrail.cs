using System.Collections.Generic;
using KR.Gameplay.Player;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // Live guidance on foot: a thin glowing line on the ground along the next stretch of the satnav route, from where
    // you stand to about 60 m ahead. It lies on the street (never in front of your face) and is hidden while driving
    // (the car screen guides you then), when there is no destination, and once you have arrived.
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class RouteTrail : MonoBehaviour
    {
        [SerializeField] Navigator nav;
        [SerializeField] PlayerRig rig;
        [SerializeField] float ahead = 60f;
        [SerializeField] float width = 0.45f;
        [SerializeField] float lift = 0.08f;

        Mesh _mesh;
        MeshRenderer _renderer;
        float _timer;
        readonly List<Vector3> _line = new List<Vector3>();
        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector2> _uvs = new List<Vector2>();
        readonly List<int> _tris = new List<int>();

        public void Configure(Navigator navigator, PlayerRig playerRig, Material material)
        {
            nav = navigator;
            rig = playerRig;
            GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        void Awake()
        {
            _mesh = new Mesh { name = "Route trail" };
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = GetComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        void LateUpdate()
        {
            bool show = nav != null && rig != null && rig.CurrentMode == PlayerRig.Mode.Walking && nav.Destination != null &&
                        nav.Route.Count > 1 && nav.Remaining > 20f;
            if (_renderer.enabled != show)
                _renderer.enabled = show;
            if (!show)
                return;
            _timer -= Time.deltaTime;
            if (_timer > 0f)
                return;
            _timer = 0.2f;
            Build();
        }

        void Build()
        {
            var route = nav.Route;
            _line.Clear();
            Vector3 you = rig.Player.transform.position;
            int start = Mathf.Clamp(nav.Progress + 1, 1, route.Count - 1);
            // From your feet (at the route's height there) to the route, then along it.
            var first = route[start];
            _line.Add(new Vector3(you.x, Mathf.Min(you.y, first.y + 0.3f), you.z));
            float left = ahead;
            Vector3 prev = _line[0];
            for (int i = start; i < route.Count && left > 0f; i++)
            {
                Vector3 p = route[i];
                float d = Vector3.Distance(prev, p);
                if (d > left)
                {
                    p = Vector3.Lerp(prev, p, left / d);
                    d = left;
                }
                // Long straight pieces get intermediate points so the line can follow the slope.
                int steps = Mathf.Max(1, Mathf.CeilToInt(d / 4f));
                for (int k = 1; k <= steps; k++)
                    _line.Add(Vector3.Lerp(prev, p, (float)k / steps));
                left -= d;
                prev = p;
            }

            _verts.Clear();
            _uvs.Clear();
            _tris.Clear();
            float along = 0f;
            for (int i = 0; i < _line.Count; i++)
            {
                Vector3 a = _line[Mathf.Max(0, i - 1)], b = _line[Mathf.Min(_line.Count - 1, i + 1)];
                Vector3 dir = b - a;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-4f)
                    dir = Vector3.forward;
                var side = Vector3.Cross(Vector3.up, dir.normalized) * (width * 0.5f);
                Vector3 c = _line[i] + Vector3.up * lift;
                if (i > 0)
                    along += Vector3.Distance(_line[i - 1], _line[i]);
                _verts.Add(c - side);
                _verts.Add(c + side);
                _uvs.Add(new Vector2(0f, along));
                _uvs.Add(new Vector2(1f, along));
                if (i > 0)
                {
                    int v = _verts.Count - 4;
                    _tris.Add(v); _tris.Add(v + 2); _tris.Add(v + 1);
                    _tris.Add(v + 1); _tris.Add(v + 2); _tris.Add(v + 3);
                }
            }
            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
        }
    }
}
