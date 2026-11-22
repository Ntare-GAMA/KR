using System.Collections.Generic;
using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // The city's enterable buildings (their box colliders) in a 32 m grid, with the geometry the interiors need:
    // footprint frame, the door on the side facing the street, and the ground under a point.
    public sealed class BuildingIndex
    {
        public const float Cell = 32f;

        public struct Building
        {
            public Transform t;
            public Collider col;
            public BoxCollider box;   // null for buildings with a mesh collider (their world bounds are used)
        }

        readonly List<Building> _buildings = new List<Building>();
        readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        static readonly RaycastHit[] Hits = new RaycastHit[8];
        static bool _warned;

        public int Count => _buildings.Count;
        public Building this[int i] => _buildings[i];

        // Houses, shops, halls, banks, clinics and hotels under root: 14 to 12000 m², at least 2.4 m tall, not
        // foundations, and not the restaurants (they open into their own dining room). Box colliders, and mesh
        // colliders for the bigger landmark buildings (one per building object).
        public BuildingIndex(GameObject root, IReadOnlyList<Vector3> restaurantDoors)
        {
            var seen = new HashSet<Transform>();
            foreach (var col in root.GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger || !(col is BoxCollider || col is MeshCollider))
                    continue;
                var t = col.transform;
                if (t.name.StartsWith("Foundation") || !seen.Add(t))
                    continue;
                var box = col as BoxCollider;
                var size = box != null ? Vector3.Scale(box.size, Abs(t.lossyScale)) : col.bounds.size;
                float area = size.x * size.z;
                if (area < 14f || area > 12000f || size.y < 2.4f)
                    continue;
                var bounds = col.bounds;
                bounds.Expand(6f);
                bool restaurant = false;
                foreach (var p in restaurantDoors)
                    if (bounds.Contains(p))
                        restaurant = true;
                if (restaurant)
                    continue;
                int index = _buildings.Count;
                _buildings.Add(new Building { t = t, col = col, box = box });
                long key = Key(bounds.center);
                if (!_cells.TryGetValue(key, out var list))
                    _cells[key] = list = new List<int>();
                list.Add(index);
            }
        }

        // Buildings whose walls are within radius of p, nearest first.
        public void Nearby(Vector3 p, float radius, List<(float d, int i)> into)
        {
            into.Clear();
            int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    if (_cells.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var list))
                        foreach (int i in list)
                        {
                            float d = (_buildings[i].col.bounds.ClosestPoint(p) - p).sqrMagnitude;
                            if (d < radius * radius)
                                into.Add((d, i));
                        }
            into.Sort((a, b) => a.d.CompareTo(b.d));
        }

        // The building's footprint as a centre, flat axes and half sizes (metres), and its height.
        public void Frame(int i, out Vector3 centre, out Vector3 right, out Vector3 fwd, out float hx, out float hz, out float height)
        {
            var b = _buildings[i];
            var t = b.t;
            if (b.box == null)
            {
                // Mesh collider: its world bounds, axis-aligned.
                var wb = b.col.bounds;
                centre = wb.center;
                right = Vector3.right;
                fwd = Vector3.forward;
                hx = wb.extents.x;
                hz = wb.extents.z;
                height = wb.size.y;
                return;
            }
            centre = t.TransformPoint(b.box.center);
            right = Vector3.ProjectOnPlane(t.right, Vector3.up).normalized;
            fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            var s = Vector3.Scale(b.box.size, Abs(t.lossyScale));
            hx = s.x * 0.5f;
            hz = s.z * 0.5f;
            height = s.y;
        }

        // The door of building i: the middle of the face that looks towards the nearest street, on the ground.
        public bool DoorOf(int i, RoadGraph graph, out Vector3 at, out Vector3 outward)
        {
            Frame(i, out var c, out var right, out var fwd, out float hx, out float hz, out _);
            Vector3 street = c - fwd * 10f;
            if (graph != null)
            {
                int n = graph.Nearest(c, 120f);
                if (n >= 0)
                    street = graph.Position(n);
            }
            var to = street - c;
            to.y = 0f;
            float dx = Vector3.Dot(to, right) / Mathf.Max(0.1f, hx), dz = Vector3.Dot(to, fwd) / Mathf.Max(0.1f, hz);
            if (Mathf.Abs(dx) > Mathf.Abs(dz))
            {
                outward = right * Mathf.Sign(dx);
                at = c + outward * hx;
            }
            else
            {
                outward = fwd * Mathf.Sign(dz);
                at = c + outward * hz;
            }
            return Ground(at + outward * 0.6f, _buildings[i].t, out at.y);
        }

        // Ground under p (not the building itself, not a vehicle); the terrain's height if no collider is found.
        public static bool Ground(Vector3 p, Transform building, out float y)
        {
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 6f, Vector3.down, Hits, 14f, 1 << 0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            y = p.y;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider.attachedRigidbody != null || h.normal.y < 0.7f || (building != null && h.collider.transform.IsChildOf(building)))
                    continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    y = h.point.y;
                }
            }
            if (best < float.MaxValue)
                return true;
            foreach (var t in Terrain.activeTerrains)
            {
                var o = t.transform.position;
                var size = t.terrainData.size;
                if (p.x >= o.x && p.z >= o.z && p.x <= o.x + size.x && p.z <= o.z + size.z)
                {
                    y = t.SampleHeight(p) + o.y;
                    return true;
                }
            }
            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning($"[Interiors] No ground found at {p:F1} ({n} hits).");
            }
            return false;
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        static long Key(Vector3 p) => ((long)Mathf.FloorToInt(p.x / Cell) << 32) ^ (uint)Mathf.FloorToInt(p.z / Cell);
    }
}
