using System.Collections.Generic;
using KR.Gameplay.Player;
using KR.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Story
{
    // Every building in the city can be entered (owner request 2026-11-21: "make the insides of the buildings
    // accessible; one should open the ways and navigate through them"). There are thousands of buildings, so the
    // insides are made when you go in, sized to the building you chose:
    //   - doors appear on the buildings near you, on the side facing the street;
    //   - E (A on the controller) at a door opens it: you step into a furnished interior built inside that
    //     building's walls (a home with a living room, bedroom and kitchen; a shop or office; or a hall with a
    //     reception, seating and desks), lit by ceiling lamps and windows;
    //   - walk back out through the open door (or press E at it) and the street is as it was.
    // Restaurants and cafes keep their own dining room (RestaurantDoor). Added when the scene loads.
    public sealed class BuildingInteriors : MonoBehaviour
    {
        const float Cell = 32f, DoorReach = 2.3f, ShowDoors = 32f, WallT = 0.15f;
        const int DoorPool = 10;

        struct Building
        {
            public Transform t;
            public BoxCollider box;
        }

        sealed class DoorVisual
        {
            public Transform root, hinge;
            public int building = -1;
            public Vector3 at, outward;
        }

        readonly List<Building> _buildings = new List<Building>();
        readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        readonly DoorVisual[] _doors = new DoorVisual[DoorPool];
        PlayerRig _rig;
        Navigator _nav;
        SpeechBubble _prompt;
        Material _wood, _frame, _ceiling, _fabric, _dark, _metal, _window, _rug;
        Material[] _walls, _floors;
        float _assignTimer;
        DoorVisual _near;

        // Inside a building.
        int _inside = -1;
        GameObject _interior;
        Renderer[] _hidden;
        Collider[] _disabled;
        Vector3 _centre, _right, _fwd, _doorAt, _doorOut;
        float _hx, _hz, _floorY;
        Transform _doorLeaf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var nav = Object.FindFirstObjectByType<Navigator>();
            if (nav != null && nav.GetComponent<BuildingInteriors>() == null)
                nav.gameObject.AddComponent<BuildingInteriors>();
        }

        void Start()
        {
            _rig = Object.FindFirstObjectByType<PlayerRig>();
            _nav = GetComponent<Navigator>();
            var city = GameObject.Find("City buildings");
            if (_rig == null || city == null)
            {
                enabled = false;
                return;
            }
            // Restaurants and cafes already open into their own dining room.
            var restaurantDoors = new List<Vector3>();
            foreach (var d in Object.FindObjectsByType<RestaurantDoor>(FindObjectsSortMode.None))
                restaurantDoors.Add(d.transform.position);

            foreach (var box in city.GetComponentsInChildren<BoxCollider>())
            {
                var t = box.transform;
                if (t.name.StartsWith("Foundation"))
                    continue;
                var size = Vector3.Scale(box.size, Abs(t.lossyScale));
                float area = size.x * size.z;
                if (area < 14f || area > 3000f || size.y < 2.4f)
                    continue;
                var bounds = box.bounds;
                bounds.Expand(6f);
                bool restaurant = false;
                foreach (var p in restaurantDoors)
                    if (bounds.Contains(p))
                        restaurant = true;
                if (restaurant)
                    continue;
                int index = _buildings.Count;
                _buildings.Add(new Building { t = t, box = box });
                long key = Key(box.bounds.center);
                if (!_cells.TryGetValue(key, out var list))
                    _cells[key] = list = new List<int>();
                list.Add(index);
            }

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Material M(Color c, float smooth = 0.3f)
            {
                var m = new Material(lit);
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", smooth);
                return m;
            }
            _wood = M(new Color(0.42f, 0.27f, 0.16f), 0.4f);
            _frame = M(new Color(0.18f, 0.12f, 0.08f), 0.35f);
            _ceiling = M(new Color(0.96f, 0.95f, 0.92f), 0.05f);
            _fabric = M(new Color(0.55f, 0.18f, 0.16f), 0.1f);
            _dark = M(new Color(0.12f, 0.12f, 0.13f), 0.3f);
            _metal = M(new Color(0.6f, 0.62f, 0.64f), 0.6f);
            _rug = M(new Color(0.75f, 0.6f, 0.35f), 0.05f);
            _window = new Material(unlit);
            _window.SetColor("_BaseColor", new Color(0.8f, 0.9f, 1f));
            _walls = new[] { M(new Color(0.95f, 0.9f, 0.8f), 0.1f), M(new Color(0.83f, 0.9f, 0.95f), 0.1f),
                             M(new Color(0.86f, 0.93f, 0.84f), 0.1f), M(new Color(0.97f, 0.86f, 0.76f), 0.1f) };
            _floors = new[] { M(new Color(0.62f, 0.33f, 0.22f), 0.45f), M(new Color(0.7f, 0.7f, 0.68f), 0.5f),
                              M(new Color(0.5f, 0.36f, 0.24f), 0.4f) };

            for (int i = 0; i < DoorPool; i++)
                _doors[i] = MakeDoor(i);
            _prompt = SpeechBubble.Create(transform);
            Debug.Log($"[Interiors] {_buildings.Count} buildings can be entered.");
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        static long Key(Vector3 p) => ((long)Mathf.FloorToInt(p.x / Cell) << 32) ^ (uint)Mathf.FloorToInt(p.z / Cell);

        // ---------- doors outside ----------

        DoorVisual MakeDoor(int i)
        {
            var root = new GameObject($"Building door {i}").transform;
            root.SetParent(transform, false);
            Piece(root, _frame, new Vector3(-0.56f, 1.1f, 0f), new Vector3(0.12f, 2.2f, 0.1f), false);
            Piece(root, _frame, new Vector3(0.56f, 1.1f, 0f), new Vector3(0.12f, 2.2f, 0.1f), false);
            Piece(root, _frame, new Vector3(0f, 2.24f, 0f), new Vector3(1.24f, 0.12f, 0.1f), false);
            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(root, false);
            hinge.localPosition = new Vector3(-0.5f, 0f, 0f);
            Piece(hinge, _wood, new Vector3(0.5f, 1.07f, 0f), new Vector3(1.0f, 2.12f, 0.05f), false);
            Piece(hinge, _metal, new Vector3(0.88f, 1.0f, 0.05f), new Vector3(0.12f, 0.03f, 0.04f), false);
            root.gameObject.SetActive(false);
            return new DoorVisual { root = root, hinge = hinge };
        }

        // The door of building i: the middle of the face that looks towards the nearest street, on the ground.
        bool DoorOf(int i, out Vector3 at, out Vector3 outward)
        {
            var b = _buildings[i];
            Frame(b, out var c, out var right, out var fwd, out float hx, out float hz, out _);
            Vector3 street = c - fwd * 10f;
            var graph = _nav != null ? _nav.Graph : null;
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
            return Ground(at + outward * 0.6f, b.t, out at.y);
        }

        // The building's footprint as a centre, flat axes and half sizes (metres), and its height.
        static void Frame(Building b, out Vector3 centre, out Vector3 right, out Vector3 fwd, out float hx, out float hz, out float height)
        {
            var t = b.t;
            centre = t.TransformPoint(b.box.center);
            right = Vector3.ProjectOnPlane(t.right, Vector3.up).normalized;
            fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            var s = Vector3.Scale(b.box.size, Abs(t.lossyScale));
            hx = s.x * 0.5f;
            hz = s.z * 0.5f;
            height = s.y;
        }

        static readonly RaycastHit[] Hits = new RaycastHit[8];

        // Ground under p (not the building itself, not a vehicle).
        static bool Ground(Vector3 p, Transform building, out float y)
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
            // No collider found (out of physics range, or a gap): the terrain's own height there.
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
        static bool _warned;

        void Update()
        {
            if (_rig == null)
                return;
            if (_inside >= 0)
            {
                Inside();
                return;
            }
            bool walking = _rig.CurrentMode == PlayerRig.Mode.Walking && !_rig.Busy && !GamePause.Paused;
            _assignTimer -= Time.deltaTime;
            if (_assignTimer <= 0f)
            {
                _assignTimer = 0.5f;
                Assign(walking);
            }
            _near = null;
            if (!walking)
                return;
            var you = _rig.Player.transform.position;
            float best = DoorReach * DoorReach;
            foreach (var d in _doors)
                if (d.building >= 0)
                {
                    float dd = (d.at - you).sqrMagnitude;
                    if (dd < best)
                    {
                        best = dd;
                        _near = d;
                    }
                }
            if (_near == null)
                return;
            bool carNear = _rig.Car != null && !_rig.Car.Driven && (_rig.Car.transform.position - you).sqrMagnitude < 3.5f * 3.5f;
            if (carNear)
                return;
            _prompt.Say(_near.root, 2.5f, $"<b>{(VrRig.Active ? "A" : "E")}</b>  Go in", 0.2f);
            if (RigInput.UsePressed)
                Enter(_near);
        }

        // Doors on the nearest buildings.
        void Assign(bool walking)
        {
            var you = _rig.Player.transform.position;
            var picks = new List<(float d, int i)>();
            if (walking)
            {
                int cx = Mathf.FloorToInt(you.x / Cell), cz = Mathf.FloorToInt(you.z / Cell);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (_cells.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var list))
                            foreach (int i in list)
                            {
                                float d = (_buildings[i].box.bounds.ClosestPoint(you) - you).sqrMagnitude;
                                if (d < ShowDoors * ShowDoors)
                                    picks.Add((d, i));
                            }
                picks.Sort((a, b) => a.d.CompareTo(b.d));
            }
            var keep = new HashSet<int>();
            for (int k = 0; k < picks.Count && k < DoorPool; k++)
                keep.Add(picks[k].i);
            foreach (var d in _doors)
                if (d.building >= 0 && !keep.Contains(d.building))
                {
                    d.building = -1;
                    d.root.gameObject.SetActive(false);
                }
                else if (d.building >= 0)
                    keep.Remove(d.building);
            foreach (int i in keep)
                foreach (var d in _doors)
                    if (d.building < 0)
                    {
                        if (DoorOf(i, out d.at, out d.outward))
                        {
                            d.building = i;
                            d.root.SetPositionAndRotation(d.at + d.outward * 0.04f, Quaternion.LookRotation(d.outward));
                            d.hinge.localRotation = Quaternion.identity;
                            d.root.gameObject.SetActive(true);
                        }
                        break;
                    }
        }

        // ---------- going in and out ----------

        public int Count => _buildings.Count;
        public bool IsInside => _inside >= 0;

        // Goes into building i as if at its door (the Editor's play-mode checks).
        public bool TryEnter(int i)
        {
            if (_inside >= 0)
                Leave();
            if (i < 0 || i >= _buildings.Count || !DoorOf(i, out var at, out var outward))
                return false;
            var d = _doors[0];
            d.building = i;
            d.at = at;
            d.outward = outward;
            Enter(d);
            d.building = -1;
            return true;
        }

        public void ForceLeave()
        {
            if (_inside >= 0)
                Leave();
        }

        void Enter(DoorVisual door)
        {
            int i = door.building;
            var b = _buildings[i];
            Frame(b, out _centre, out _right, out _fwd, out _hx, out _hz, out float height);
            _doorAt = door.at;
            _doorOut = door.outward;
            VrRig.Fade(0.35f);
            _hidden = b.t.GetComponentsInChildren<Renderer>();
            foreach (var r in _hidden)
                r.enabled = false;
            _disabled = b.t.GetComponentsInChildren<Collider>();
            foreach (var c in _disabled)
                c.enabled = false;
            // The floor level with the highest ground inside, so nothing pokes through it.
            _floorY = door.at.y;
            for (int sx = -1; sx <= 1; sx++)
                for (int sz = -1; sz <= 1; sz++)
                    if (Ground(_centre + _right * (sx * (_hx - 0.5f)) + _fwd * (sz * (_hz - 0.5f)), b.t, out float g))
                        _floorY = Mathf.Max(_floorY, g);
            _floorY += 0.06f;
            _interior = BuildInterior(i, height);
            _inside = i;
            door.root.gameObject.SetActive(false);
            _rig.Player.Teleport(new Vector3(_doorAt.x, _floorY, _doorAt.z) - _doorOut * 1.3f,
                                 Quaternion.LookRotation(-_doorOut).eulerAngles.y);
            _rig.Toast("Walk back out through the door to leave.", 3f);
        }

        void Inside()
        {
            var you = _rig.Player.transform.position;
            var d = you - _centre;
            float x = Vector3.Dot(d, _right), z = Vector3.Dot(d, _fwd);
            bool out_ = Mathf.Abs(x) > _hx + 0.5f || Mathf.Abs(z) > _hz + 0.5f || Mathf.Abs(you.y - _floorY) > 6f;
            bool atDoor = (you - (_doorAt - _doorOut * 0.6f)).sqrMagnitude < 1.4f * 1.4f;
            if (atDoor && _rig.CurrentMode == PlayerRig.Mode.Walking && !_rig.Busy)
            {
                _prompt.Say(_doorLeaf != null ? _doorLeaf : _interior.transform, 2.4f, $"<b>{(VrRig.Active ? "A" : "E")}</b>  Go out", 0.2f);
                if (RigInput.UsePressed)
                {
                    VrRig.Fade(0.35f);
                    _rig.Player.Teleport(new Vector3(_doorAt.x, _doorAt.y, _doorAt.z) + _doorOut * 1.2f, Quaternion.LookRotation(_doorOut).eulerAngles.y);
                    out_ = true;
                }
            }
            if (out_)
                Leave();
        }

        void Leave()
        {
            if (_hidden != null)
                foreach (var r in _hidden)
                    if (r != null) r.enabled = true;
            if (_disabled != null)
                foreach (var c in _disabled)
                    if (c != null) c.enabled = true;
            if (_interior != null)
                Destroy(_interior);
            _interior = null;
            _inside = -1;
            _assignTimer = 0f;
        }

        void OnDisable()
        {
            if (_inside >= 0)
                Leave();
        }

        // ---------- the interior ----------

        Transform _room;
        System.Random _rng;

        // In "door space": x across the front, z from the front wall (0) to the back wall (depth), y up from the floor.
        // Origin: the middle of the front wall, on the floor.
        Vector3 W(float x, float y, float z) => _front + _across * x + Vector3.up * y - _doorOut * z;
        Vector3 _across, _front;
        float _width, _depth, _doorX;

        GameObject BuildInterior(int index, float height)
        {
            _rng = new System.Random(index * 7919 + 17);
            var go = new GameObject("Interior");
            _room = go.transform;
            _room.SetParent(transform, false);
            _across = Vector3.Cross(Vector3.up, -_doorOut).normalized;   // right, looking in
            // Size across and deep, from the face the door is on.
            bool doorOnX = Mathf.Abs(Vector3.Dot(_doorOut, _right)) > 0.7f;
            _width = (doorOnX ? _hz : _hx) * 2f - 0.25f;
            _depth = (doorOnX ? _hx : _hz) * 2f - 0.25f;
            // Door position across, measured from the building's middle.
            var mid = _centre + _doorOut * (doorOnX ? _hx : _hz);
            _doorX = Vector3.Dot(_doorAt - mid, _across);
            _front = new Vector3(mid.x, _floorY, mid.z);
            float h = Mathf.Clamp(height - 0.3f, 2.5f, 3.1f);
            var wall = _walls[_rng.Next(_walls.Length)];
            var floor = _floors[_rng.Next(_floors.Length)];
            float hw = _width * 0.5f;

            // Shell: floor, ceiling, four walls; the front wall has the doorway, the side walls have windows.
            Box(floor, 0f, -0.1f, _depth * 0.5f - 0.06f, _width + 0.3f, 0.2f, _depth + 0.3f, true);
            Box(_ceiling, 0f, h + 0.05f, _depth * 0.5f - 0.06f, _width + 0.3f, 0.1f, _depth + 0.3f, true);
            Box(wall, 0f, h * 0.5f, _depth + 0.02f, _width + 0.3f, h, WallT, true);                   // back
            float dl = _doorX - 0.55f, dr = _doorX + 0.55f;
            Box(wall, (-hw - 0.15f + dl) * 0.5f, h * 0.5f, 0.06f, dl + hw + 0.15f, h, WallT, true);    // front, left of door
            Box(wall, (dr + hw + 0.15f) * 0.5f, h * 0.5f, 0.06f, hw + 0.15f - dr, h, WallT, true);     // front, right of door
            Box(wall, _doorX, (2.2f + h) * 0.5f, 0.06f, 1.1f, h - 2.2f, WallT, true);                  // over the door
            foreach (float side in new[] { -1f, 1f })
            {
                Box(wall, side * (hw + 0.07f), h * 0.5f, _depth * 0.5f, WallT, h, _depth + 0.2f, true);
                for (float z = 1.8f; z < _depth - 1.2f; z += 3.5f)
                    Box(_window, side * (hw - 0.01f), 1.6f, z, 0.03f, 1.1f, 1.2f, false);
            }
            // The door, standing open into the room.
            var leafHinge = new GameObject("Door").transform;
            leafHinge.SetParent(_room, false);
            leafHinge.SetPositionAndRotation(W(_doorX - 0.5f, 0f, 0.08f), Quaternion.LookRotation(-_doorOut) * Quaternion.Euler(0f, -95f, 0f));
            var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(leaf.GetComponent<Collider>());
            leaf.transform.SetParent(leafHinge, false);
            leaf.transform.localPosition = new Vector3(0.5f, 1.06f, 0f);
            leaf.transform.localScale = new Vector3(1.0f, 2.12f, 0.05f);
            leaf.GetComponent<Renderer>().sharedMaterial = _wood;
            _doorLeaf = leafHinge;

            float area = _width * _depth;
            if (area < 140f)
                Home(h, wall);
            else if (area < 600f)
                Shop(h);
            else
                Hall(h);
            return go;
        }

        // A home: living room at the front; bedroom and kitchen behind, through doorways.
        void Home(float h, Material wall)
        {
            float hw = _width * 0.5f;
            bool rooms = _depth > 6.5f && _width > 5f;
            float back = rooms ? Mathf.Clamp(_depth * 0.55f, 3.2f, _depth - 3f) : _depth;
            if (rooms)
            {
                // Partition across with a doorway on each half; a wall between bedroom and kitchen.
                float gapL = -hw * 0.5f, gapR = hw * 0.5f;
                Box(wall, (-hw + gapL - 0.45f) * 0.5f, h * 0.5f, back, gapL - 0.45f + hw, h, 0.1f, true);
                Box(wall, 0f, h * 0.5f, back, (gapR - 0.45f) - (gapL + 0.45f), h, 0.1f, true);
                Box(wall, (gapR + 0.45f + hw) * 0.5f, h * 0.5f, back, hw - gapR - 0.45f, h, 0.1f, true);
                Box(wall, 0f, h * 0.5f, (back + _depth) * 0.5f, 0.1f, h, _depth - back, true);
                // Bedroom (left): bed, bedside table, wardrobe.
                float bz = (back + _depth) * 0.5f;
                Bed(-hw * 0.5f, _depth - 1.15f);
                Box(_wood, -hw + 0.3f, 0.3f, _depth - 0.35f, 0.45f, 0.6f, 0.45f, true);
                Box(_wood, -0.6f, 1.0f, bz, 0.6f, 2.0f, 1.2f, true);
                Lamp(-hw * 0.5f, bz, h);
                // Kitchen (right): counter along the back, a small table and two chairs.
                Box(_wood, hw * 0.5f, 0.45f, _depth - 0.35f, hw - 0.4f, 0.9f, 0.6f, true);
                Box(_metal, hw * 0.5f, 0.92f, _depth - 0.35f, hw - 0.4f, 0.04f, 0.62f, false);
                Table(hw * 0.5f, bz - 0.3f, 0.9f, 0.7f);
                Chair(hw * 0.5f - 0.6f, bz - 0.3f, 90f);
                Chair(hw * 0.5f + 0.6f, bz - 0.3f, -90f);
                Lamp(hw * 0.5f, bz, h);
            }
            // Living room: sofa against a side wall, coffee table, rug, TV unit opposite.
            float lz = Mathf.Min(back, _depth) * 0.5f + 0.4f;
            Box(_rug, 0f, 0.005f, lz, Mathf.Min(2.4f, _width - 1.5f), 0.01f, 1.6f, false);
            Sofa(-hw + 0.5f, lz, 90f);
            Box(_wood, 0f, 0.22f, lz, 1.0f, 0.44f, 0.55f, true);
            Box(_dark, hw - 0.3f, 0.3f, lz, 0.45f, 0.6f, 1.6f, true);
            Box(_dark, hw - 0.12f, 1.2f, lz, 0.05f, 0.7f, 1.2f, false);
            Lamp(0f, lz, h);
        }

        // A shop or office: a counter facing the door, shelves along the sides, two desks.
        void Shop(float h)
        {
            float hw = _width * 0.5f;
            Box(_wood, 0f, 0.5f, Mathf.Min(_depth - 1.5f, 4f), Mathf.Min(3f, _width - 2f), 1.0f, 0.7f, true);
            for (float z = 1.5f; z < _depth - 1f; z += 2.2f)
                foreach (float side in new[] { -1f, 1f })
                {
                    Box(_metal, side * (hw - 0.3f), 1.0f, z, 0.45f, 2.0f, 1.8f, true);
                    for (int k = 0; k < 4; k++)
                        Box(_rng.Next(2) == 0 ? _fabric : _rug, side * (hw - 0.3f), 0.35f + k * 0.45f, z, 0.35f, 0.25f, 1.5f, false);
                }
            for (float z = Mathf.Min(_depth - 1.5f, 4f) + 2.2f; z < _depth - 1.2f; z += 2.6f)
            {
                Table(-1.2f, z, 1.4f, 0.7f);
                Chair(-1.2f, z - 0.6f, 0f);
                Table(1.2f, z, 1.4f, 0.7f);
                Chair(1.2f, z - 0.6f, 0f);
            }
            for (float z = 2f; z < _depth; z += 5f)
                Lamp(0f, z, h);
        }

        // A large hall: reception desk, waiting seats, rows of desks, plants.
        void Hall(float h)
        {
            float hw = _width * 0.5f;
            Box(_wood, 0f, 0.53f, 4.5f, 3.2f, 1.06f, 0.75f, true);
            for (float x = -hw + 1.5f; x < -1.5f; x += 2.4f)
                Sofa(x, 2.2f, 0f);
            for (float z = 8f; z < _depth - 2f; z += 3.2f)
                for (float x = -hw + 2f; x < hw - 1.5f; x += 3.2f)
                {
                    Table(x, z, 1.6f, 0.8f);
                    Chair(x, z - 0.65f, 0f);
                }
            for (float z = 3f; z < _depth; z += 7f)
                for (float x = -hw + 4f; x < hw; x += 8f)
                    Lamp(x, z, h);
            foreach (float side in new[] { -1f, 1f })
            {
                Box(_dark, side * (hw - 0.6f), 0.25f, 1.2f, 0.5f, 0.5f, 0.5f, true);
                Box(_fabric, side * (hw - 0.6f), 0.9f, 1.2f, 0.7f, 0.8f, 0.7f, false);
            }
        }

        void Bed(float x, float z)
        {
            Box(_wood, x, 0.22f, z, 1.5f, 0.44f, 2.0f, true);
            Box(_ceiling, x, 0.5f, z + 0.1f, 1.45f, 0.14f, 1.75f, false);
            Box(_fabric, x, 0.58f, z + 0.25f, 1.45f, 0.04f, 1.35f, false);
            Box(_wood, x, 0.6f, z + 1.0f, 1.5f, 1.2f, 0.08f, true);
        }

        void Sofa(float x, float z, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            BoxR(_fabric, x, 0.22f, z, new Vector3(1.9f, 0.44f, 0.85f), rot, Vector3.zero, true);
            BoxR(_fabric, x, 0.62f, z, new Vector3(1.9f, 0.45f, 0.18f), rot, new Vector3(0f, 0f, 0.34f), true);
        }

        void Table(float x, float z, float w, float d)
        {
            Box(_wood, x, 0.74f, z, w, 0.05f, d, true);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    Box(_frame, x + sx * (w * 0.5f - 0.06f), 0.36f, z + sz * (d * 0.5f - 0.06f), 0.05f, 0.72f, 0.05f, false);
        }

        void Chair(float x, float z, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            BoxR(_frame, x, 0.45f, z, new Vector3(0.44f, 0.05f, 0.44f), rot, Vector3.zero, false);
            BoxR(_frame, x, 0.75f, z, new Vector3(0.44f, 0.55f, 0.04f), rot, new Vector3(0f, 0f, -0.2f), false);
            foreach (float sx in new[] { -0.18f, 0.18f })
                foreach (float sz in new[] { -0.18f, 0.18f })
                    BoxR(_frame, x, 0.22f, z, new Vector3(0.04f, 0.44f, 0.04f), rot, new Vector3(sx, 0f, sz), false);
        }

        void Lamp(float x, float z, float h)
        {
            Box(_window, x, h - 0.03f, z, 0.5f, 0.04f, 0.5f, false);
            var l = new GameObject("Lamp").AddComponent<Light>();
            l.transform.SetParent(_room, false);
            l.transform.position = W(x, h - 0.4f, z);
            l.type = LightType.Point;
            l.range = 7f;
            l.intensity = 1.6f;
            l.color = new Color(1f, 0.94f, 0.84f);
            l.shadows = LightShadows.None;
        }

        void Box(Material m, float x, float y, float z, float w, float h, float d, bool solid) =>
            BoxR(m, x, y, z, new Vector3(w, h, d), Quaternion.identity, Vector3.zero, solid);

        // A box in door space (x across, y up, z into the room), turned by rot about its own centre.
        void BoxR(Material m, float x, float y, float z, Vector3 size, Quaternion rot, Vector3 offset, bool solid)
        {
            if (size.x <= 0.01f || size.y <= 0.01f || size.z <= 0.01f)
                return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            if (!solid)
                Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_room, false);
            var look = Quaternion.LookRotation(-_doorOut) * rot;
            go.transform.SetPositionAndRotation(W(x, y, z) + look * offset, look);
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void Piece(Transform parent, Material m, Vector3 local, Vector3 size, bool solid)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            if (!solid)
                Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
        }
    }
}
