using System;
using System.Collections.Generic;
using UnityEngine;

namespace KR.Gameplay.World
{
    // Moves cars (or pedestrians) along real road polylines in their own lane (Rwanda drives on the right). Bodies
    // never pass through each other: each one looks down a corridor ahead and slows or stops for anything in it,
    // whatever road it is on (same lane, crossing traffic at junctions, people on a footpath). Where two bodies
    // block each other the lower index has right of way, so junctions never deadlock. Cars also stop at red
    // signals. Wheels spin with speed and bodies pitch under braking and acceleration; walkers' animation speed
    // follows their pace. Two-way paths turn bodies around at the end; one-way paths wrap to the start. Things
    // outside the traffic (the player's parked car, an accident's police car and ambulance, an officer or someone
    // lying on the road) are registered as obstacles: cars stop for them too, and after waiting behind one that is
    // not moving (or a halted car) they pass it slowly in the other lane once that is clear, indicating out and
    // back in.
    public sealed class RoadNetworkTraffic : MonoBehaviour
    {
        [Serializable]
        public sealed class RoadPath
        {
            public Vector3[] points;
            public bool oneway;
            [Tooltip("Half the carriageway width; the road surface is a plane between its two edges.")]
            public float halfWidth;
            [Tooltip("Route weight per metre when driving (main roads cheaper than residential lanes); 0 = 1.")]
            public float driveCost = 1f;
        }

        [Serializable]
        public sealed class Car
        {
            public Transform body;
            public int path;
            public float distance;
            public bool reverse;
            public float speed;
            [Tooltip("Lateral offset from the centreline, positive to the right of travel (its lane).")]
            public float offset;
            [Tooltip("Visual model under the body; pitched under braking and acceleration.")]
            public Transform model;
            [Tooltip("Wheel pivots aligned with the body; spun about their local X.")]
            public Transform[] wheels;
            public float wheelRadius = 0.35f;
            [Tooltip("Walkers: animation speed follows actual pace.")]
            public Animator animator;
        }

        // A signalised junction on a path: s is its distance from the path start.
        [Serializable]
        public sealed class Stop
        {
            public int path;
            public float s;
            public int junction;
            public int phase;
        }

        [SerializeField] RoadPath[] paths;
        [SerializeField] Car[] cars;
        [SerializeField] Stop[] stops;
        [SerializeField] TrafficSignalSystem signals;
        [SerializeField, Tooltip("Keep bodies vertical on slopes (pedestrians).")] bool upright;
        [SerializeField, Tooltip("Queue behind the vehicle ahead on the same path.")] bool follow = true;
        [SerializeField] float stopLineBack = 11f;
        [SerializeField] float gap = 8f;
        [Header("Avoidance")]
        [SerializeField, Tooltip("Distance kept to whatever is ahead in the corridor.")] float avoidGap = 7.5f;
        [SerializeField, Tooltip("How far ahead the corridor reaches.")] float lookAhead = 16f;
        [SerializeField, Tooltip("Half-width of the corridor.")] float corridor = 1.9f;
        [SerializeField] float acceleration = 2.5f;
        [SerializeField] float braking = 8f;

        float[][] _cumulative;
        float[] _current;
        float[] _haltUntil;
        float[] _pitch;
        Quaternion[] _modelBase;
        float[] _animBase;
        List<int>[] _carsOnPath;
        float[][] _forwardStopU, _reverseStopU;
        Stop[][] _forwardStop, _reverseStop;
        readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        Vector3[] _pos;
        Vector3[] _fwd;
        // Overtaking a stopped obstacle: lateral shift now and wanted (negative = towards the road's centre, left),
        // seconds waited behind it, and the indicator (-1 left, +1 right) with when it stops.
        float[] _shift, _shiftTarget, _waited, _blinkUntil;
        int[] _blink;
        readonly Dictionary<int, Renderer[]> _blinkers = new Dictionary<int, Renderer[]>();
        static Material _amber;
        const float PassShift = 3.3f, WaitBeforePass = 2.5f, PassSpeed = 5f;

        // ---------- Obstacles from outside the traffic ----------

        sealed class Obstacle
        {
            public Transform body;
            public float half;        // half its width across the road
            public Vector3 last;
            public bool still;
        }

        static readonly List<Obstacle> Obstacles = new List<Obstacle>();
        static int _obstacleFrame = -1;

        // Cars stop for this body (and pass it once it is not moving); halfWidth is half its width.
        public static void AddObstacle(Transform body, float halfWidth)
        {
            if (body == null)
                return;
            foreach (var o in Obstacles)
                if (o.body == body)
                {
                    o.half = halfWidth;
                    return;
                }
            Obstacles.Add(new Obstacle { body = body, half = halfWidth, last = body.position, still = true });
        }

        public static void RemoveObstacle(Transform body)
        {
            for (int i = Obstacles.Count - 1; i >= 0; i--)
                if (Obstacles[i].body == body || Obstacles[i].body == null)
                    Obstacles.RemoveAt(i);
        }

        static void UpdateObstacles(float dt)
        {
            if (_obstacleFrame == Time.frameCount)
                return;
            _obstacleFrame = Time.frameCount;
            for (int i = Obstacles.Count - 1; i >= 0; i--)
            {
                var o = Obstacles[i];
                if (o.body == null)
                {
                    Obstacles.RemoveAt(i);
                    continue;
                }
                var p = o.body.position;
                float limit = 0.4f * dt;
                o.still = (p - o.last).sqrMagnitude <= limit * limit + 1e-6f;
                o.last = p;
            }
        }

        const float CellSize = 16f;
        // Within this distance of the camera, bodies follow the real surface (lane cross-slope, pitch, roll).
        const float SnapRange = 400f;

        Terrain _terrain;
        Terrain[] _terrains;
        // Within this distance of the camera, wheels and feet sit on the real road surface (ray down onto the road and
        // footpath colliders); further out the carriageway plane over the terrain is close enough.
        const float RayRange = 150f;
        const int GroundMask = 1 << 0;   // Default layer: roads, footpaths, plates, terrain (not the traffic itself)
        float[] _lift;          // road or footpath surface height above the terrain, per path
        Vector2[] _half;        // half width / half length of each car
        Vector3 _cam;

        // The City Life player: every vehicle and walker gives way to them.
        public static Transform Player { get; set; }

        public int Count => cars?.Length ?? 0;

        // Story Mode hooks: a global pace factor for jams (1 = free flow), and read access for the road-rule monitor.
        public float SpeedScale { get; set; } = 1f;
        public bool IsWalkers => upright;
        public IReadOnlyList<RoadPath> Paths => paths;
        public TrafficSignalSystem Signals => signals;
        public int StopCount => stops?.Length ?? 0;
        public float StopLineBack => stopLineBack;

        // Both stop lines of a signal stop: traffic going forward along the path stops stopLineBack before the
        // junction point, traffic going the other way stops the same distance past it.
        public bool StopLines(int index, out Vector3 forwardLine, out Vector3 forwardDir, out Vector3 reverseLine,
                              out Vector3 reverseDir, out int junction, out int phase)
        {
            forwardLine = reverseLine = forwardDir = reverseDir = Vector3.zero;
            junction = phase = -1;
            if (stops == null || index < 0 || index >= stops.Length)
                return false;
            var stop = stops[index];
            var pts = paths[stop.path].points;
            forwardLine = PointAlong(pts, stop.s - stopLineBack, out forwardDir);
            reverseLine = PointAlong(pts, stop.s + stopLineBack, out reverseDir);
            reverseDir = -reverseDir;
            junction = stop.junction;
            phase = stop.phase;
            return true;
        }

        static Vector3 PointAlong(Vector3[] pts, float d, out Vector3 dir)
        {
            dir = Vector3.forward;
            if (pts == null || pts.Length == 0)
                return Vector3.zero;
            d = Mathf.Max(0f, d);
            for (int i = 1; i < pts.Length; i++)
            {
                float seg = Vector3.Distance(pts[i - 1], pts[i]);
                if (d <= seg || i == pts.Length - 1)
                {
                    dir = (pts[i] - pts[i - 1]).normalized;
                    return Vector3.Lerp(pts[i - 1], pts[i], seg > 0f ? Mathf.Clamp01(d / seg) : 0f);
                }
                d -= seg;
            }
            return pts[pts.Length - 1];
        }
        public Transform Body(int i) => cars != null && i >= 0 && i < cars.Length ? cars[i].body : null;

        // Index of the vehicle whose body is (or contains) this transform, or -1.
        public int IndexOf(Transform t)
        {
            if (cars == null || t == null)
                return -1;
            for (int i = 0; i < cars.Length; i++)
                if (cars[i].body != null && (t == cars[i].body || t.IsChildOf(cars[i].body)))
                    return i;
            return -1;
        }

        // Stops one vehicle where it is (accident, police stop) until the given time; 0 releases it.
        public void Halt(int i, float seconds)
        {
            if (_haltUntil != null && i >= 0 && i < _haltUntil.Length)
                _haltUntil[i] = seconds > 0f ? Time.time + seconds : 0f;
        }

        public bool Halted(int i) => _haltUntil != null && i >= 0 && i < _haltUntil.Length && Time.time < _haltUntil[i];
        public float CurrentSpeed(int i) => _current != null && i >= 0 && i < _current.Length ? _current[i] : 0f;

        public void Configure(RoadPath[] roadPaths, Car[] carList, bool keepUpright = false, bool queue = true,
                              Stop[] signalStops = null, TrafficSignalSystem signalSystem = null)
        {
            paths = roadPaths;
            cars = carList;
            upright = keepUpright;
            follow = queue;
            stops = signalStops ?? new Stop[0];
            signals = signalSystem;
        }

        public void ConfigureAvoidance(float keepGap, float reach, float halfWidth)
        {
            avoidGap = keepGap;
            lookAhead = reach;
            corridor = halfWidth;
        }

        void Awake()
        {
            // A group with no paths or vehicles has nothing to do; say which one once and switch it off.
            if (paths == null || paths.Length == 0 || cars == null)
            {
                Debug.LogWarning($"[Traffic] '{name}' has no paths or vehicles; disabled.");
                paths ??= new RoadPath[0];
                cars ??= new Car[0];
                enabled = false;
            }
            _cumulative = new float[paths.Length][];
            _carsOnPath = new List<int>[paths.Length];
            for (int p = 0; p < paths.Length; p++)
            {
                var pts = paths[p].points;
                var c = new float[pts.Length];
                for (int i = 1; i < pts.Length; i++)
                    c[i] = c[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
                _cumulative[p] = c;
                _carsOnPath[p] = new List<int>();
            }
            int n = cars.Length;
            _current = new float[n];
            _haltUntil = new float[n];
            _pitch = new float[n];
            _modelBase = new Quaternion[n];
            _animBase = new float[n];
            _pos = new Vector3[n];
            _fwd = new Vector3[n];
            _shift = new float[n];
            _shiftTarget = new float[n];
            _waited = new float[n];
            _blinkUntil = new float[n];
            _blink = new int[n];
            for (int i = 0; i < n; i++)
            {
                var car = cars[i];
                _current[i] = car.speed;
                _carsOnPath[car.path].Add(i);
                if (car.model != null)
                    _modelBase[i] = car.model.localRotation;
                // People with a PersonAnimator already match their walk to their pace; scaling it here too
                // made them slide.
                if (car.animator != null && car.body != null && car.body.GetComponent<PersonAnimator>() != null)
                    car.animator = null;
                if (car.animator != null)
                    _animBase[i] = car.animator.speed;
            }

            var fwd = new List<(float, Stop)>[paths.Length];
            var rev = new List<(float, Stop)>[paths.Length];
            for (int p = 0; p < paths.Length; p++)
            {
                fwd[p] = new List<(float, Stop)>();
                rev[p] = new List<(float, Stop)>();
            }
            if (stops != null)
                foreach (var s in stops)
                {
                    float total = _cumulative[s.path][_cumulative[s.path].Length - 1];
                    fwd[s.path].Add((s.s - stopLineBack, s));
                    rev[s.path].Add((total - (s.s + stopLineBack), s));
                }
            _forwardStopU = new float[paths.Length][];
            _reverseStopU = new float[paths.Length][];
            _forwardStop = new Stop[paths.Length][];
            _reverseStop = new Stop[paths.Length][];
            for (int p = 0; p < paths.Length; p++)
            {
                fwd[p].Sort((a, b) => a.Item1.CompareTo(b.Item1));
                rev[p].Sort((a, b) => a.Item1.CompareTo(b.Item1));
                _forwardStopU[p] = fwd[p].ConvertAll(x => x.Item1).ToArray();
                _forwardStop[p] = fwd[p].ConvertAll(x => x.Item2).ToArray();
                _reverseStopU[p] = rev[p].ConvertAll(x => x.Item1).ToArray();
                _reverseStop[p] = rev[p].ConvertAll(x => x.Item2).ToArray();
            }

            // The scene has the district terrain and the surround tiles; heights come from whichever is under the point.
            _terrains = Terrain.activeTerrains;
            _terrain = Terrain.activeTerrain;
            float best = 0f;
            foreach (var t in _terrains)
                if (t.terrainData != null && t.terrainData.size.x * t.terrainData.size.z > best)
                {
                    best = t.terrainData.size.x * t.terrainData.size.z;
                    _terrain = t;
                }
            _lift = new float[paths.Length];
            if (_terrain != null)
                for (int p = 0; p < paths.Length; p++)
                    _lift[p] = Mathf.Clamp(paths[p].points[0].y - Height(paths[p].points[0]), 0f, 1f);
            _half = new Vector2[n];
            for (int i = 0; i < n; i++)
                _half[i] = cars[i].body != null ? AddCollider(cars[i].body) : Vector2.one;
        }

        float Height(Vector3 p)
        {
            var t = _terrain;
            var o = t.transform.position;
            var size = t.terrainData.size;
            if (p.x < o.x || p.z < o.z || p.x > o.x + size.x || p.z > o.z + size.z)
                foreach (var other in _terrains)
                {
                    var oo = other.transform.position;
                    var ss = other.terrainData.size;
                    if (p.x >= oo.x && p.z >= oo.z && p.x <= oo.x + ss.x && p.z <= oo.z + ss.z)
                    {
                        t = other;
                        break;
                    }
                }
            return t.SampleHeight(p) + t.transform.position.y;
        }

        // The real surface under p (road, kerb, footpath or ground) near the expected height: the highest upward-facing
        // surface from 1.6 m above to 3 m below it. Walls (steep faces) and anything with a rigidbody (the player's
        // car, a knocked-over motorbike) are never ground: traffic and people used to climb onto the player's car.
        static readonly RaycastHit[] Hits = new RaycastHit[8];
        static bool SurfaceAt(Vector3 p, float expected, out float y)
        {
            var from = new Vector3(p.x, expected + 1.6f, p.z);
            int n = Physics.RaycastNonAlloc(from, Vector3.down, Hits, 4.6f, GroundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            y = 0f;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider.attachedRigidbody != null || h.normal.y < 0.75f || h.distance >= best)
                    continue;
                best = h.distance;
                y = h.point.y;
            }
            return best < float.MaxValue;
        }

        // Kinematic colliders so the City Life player (on foot or driving) cannot pass through traffic.
        Vector2 AddCollider(Transform body)
        {
            if (upright)
            {
                var capsule = body.gameObject.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 0.85f, 0f);
                capsule.height = 1.7f;
                capsule.radius = 0.28f;
                Kinematic(body);
                return new Vector2(0.3f, 0.3f);
            }
            var local = new Bounds();
            bool any = false;
            var toBody = body.worldToLocalMatrix;
            foreach (var mf in body.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null)
                    continue;
                var m = toBody * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var corner = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents,
                        new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f)));
                    if (!any) { local = new Bounds(corner, Vector3.zero); any = true; }
                    else local.Encapsulate(corner);
                }
            }
            if (!any)
                return new Vector2(0.9f, 2.2f);
            var box = body.gameObject.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;
            Kinematic(body);
            return new Vector2(local.extents.x, local.extents.z);
        }

        static void Kinematic(Transform body)
        {
            var rb = body.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        static long CellKey(Vector3 p) =>
            ((long)Mathf.FloorToInt(p.x / CellSize) << 32) ^ (uint)Mathf.FloorToInt(p.z / CellSize);

        static readonly Unity.Profiling.ProfilerMarker ProfileMarker = new Unity.Profiling.ProfilerMarker("KR.Traffic");

        void Update()
        {
            using (ProfileMarker.Auto())
                Tick();
        }

        void Tick()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _cumulative == null)
                return;
            var cam = Camera.main;
            _cam = cam != null ? cam.transform.position : Vector3.zero;
            UpdateObstacles(dt);

            // Snapshot positions and headings into a spatial hash.
            foreach (var list in _cells.Values)
                list.Clear();
            for (int i = 0; i < cars.Length; i++)
            {
                var b = cars[i].body;
                if (b == null)
                {
                    _pos[i] = new Vector3(float.MaxValue, 0f, float.MaxValue);
                    _fwd[i] = Vector3.forward;
                    continue;
                }
                _pos[i] = b.position;
                var f = b.forward;
                f.y = 0f;
                _fwd[i] = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
                long key = CellKey(_pos[i]);
                if (!_cells.TryGetValue(key, out var cell))
                    _cells[key] = cell = new List<int>();
                cell.Add(i);
            }

            for (int i = 0; i < cars.Length; i++)
            {
                var car = cars[i];
                var cum = _cumulative[car.path];
                float total = cum[cum.Length - 1];
                if (total < 1f)
                    continue;

                float u = car.distance;
                float room = float.MaxValue;

                if (signals != null)
                {
                    var us = car.reverse ? _reverseStopU[car.path] : _forwardStopU[car.path];
                    var ss = car.reverse ? _reverseStop[car.path] : _forwardStop[car.path];
                    for (int k = 0; k < us.Length; k++)
                    {
                        if (us[k] <= u + 0.3f)
                            continue;
                        float toLine = us[k] - u;
                        if (toLine < 45f)
                        {
                            int state = signals.State(ss[k].junction, ss[k].phase);
                            bool tooCloseForAmber = state == TrafficSignalSystem.Amber && toLine < 6f;
                            if (state != TrafficSignalSystem.Green && !tooCloseForAmber)
                                room = toLine;
                        }
                        break;
                    }
                }
                if (follow)
                    foreach (int j in _carsOnPath[car.path])
                    {
                        var other = cars[j];
                        if (j != i && other.reverse == car.reverse && Mathf.Abs(other.offset - car.offset) < 1f && other.distance > u &&
                            !(_shiftTarget[i] != 0f && Halted(j)))   // passing a halted car: the corridor decides
                            room = Mathf.Min(room, other.distance - u - gap);
                    }
                room = Mathf.Min(room, CorridorRoom(i, out bool stillAhead));
                if (!upright)
                    Overtake(i, room, stillAhead, dt);

                float target = Time.time < _haltUntil[i] ? 0f : car.speed * SpeedScale;
                if (_shiftTarget[i] != 0f || Mathf.Abs(_shift[i]) > 0.2f)
                    target = Mathf.Min(target, PassSpeed);
                if (room < 30f)
                    target = Mathf.Min(target, Mathf.Max(0f, room) * 0.6f);
                float before = _current[i];
                _current[i] = Mathf.MoveTowards(before, target, (target < before ? braking : acceleration) * dt);
                float accel = (_current[i] - before) / dt;

                car.distance += _current[i] * dt;
                if (car.distance >= total)
                {
                    car.distance -= total;
                    if (!paths[car.path].oneway)
                        car.reverse = !car.reverse;
                }
                Place(i, car, cum, total);
                Animate(i, car, accel, dt);
                if (_blink[i] != 0)
                    Blink(i);
            }
        }

        // ---------- Passing a stopped obstacle ----------

        void Overtake(int i, float room, bool stillAhead, float dt)
        {
            if (_shiftTarget[i] == 0f)
            {
                if (stillAhead && room < 8f && _current[i] < 1f && !Halted(i))
                    _waited[i] += dt;
                else
                    _waited[i] = 0f;
                if (_waited[i] > WaitBeforePass && Mathf.Abs(_shift[i]) < 0.2f && LaneClear(i, -PassShift))
                {
                    _shiftTarget[i] = -PassShift;
                    _waited[i] = 0f;
                    Signal(i, -1, 3.5f);
                }
            }
            else if (!StillInLane(i, -_shift[i]))
            {
                // Past it: back into the lane.
                _shiftTarget[i] = 0f;
                Signal(i, 1, 3f);
            }
            // Sideways a little faster than a third of the forward speed, so the car steers out rather than slides.
            float rate = 0.35f + _current[i] * 0.35f;
            _shift[i] = Mathf.MoveTowards(_shift[i], _shiftTarget[i], rate * dt);
        }

        // Where p is from car i: along its heading, and to its right (negative: left).
        void Relative(int i, Vector3 p, out float along, out float lateral)
        {
            Vector3 d = p - _pos[i];
            var f = _fwd[i];
            along = d.x * f.x + d.z * f.z;
            lateral = d.x * f.z - d.z * f.x;
        }

        // Nothing moving in the lane `shift` to the right (negative: left), from 12 m behind to 45 m ahead.
        bool LaneClear(int i, float shift)
        {
            for (int j = 0; j < cars.Length; j++)
            {
                if (j == i || Halted(j) || cars[j].body == null)
                    continue;
                Relative(i, _pos[j], out float along, out float lateral);
                if (along > -12f && along < 45f && Mathf.Abs(lateral - shift) < corridor + 0.4f)
                    return false;
            }
            if (Player != null && Player.gameObject.activeInHierarchy)
            {
                Relative(i, Player.position, out float along, out float lateral);
                if (along > -12f && along < 45f && Mathf.Abs(lateral - shift) < corridor + 0.4f)
                    return false;
            }
            return true;
        }

        // A halted car or a still obstacle in the lane `shift` to the right, from beside the car to the corridor's end.
        bool StillInLane(int i, float shift)
        {
            float back = -(_half[i].y + 3f);
            for (int j = 0; j < cars.Length; j++)
            {
                if (j == i || !Halted(j) || cars[j].body == null)
                    continue;
                Relative(i, _pos[j], out float along, out float lateral);
                if (along > back - _half[j].y && along < lookAhead && Mathf.Abs(lateral - shift) < _half[i].x + _half[j].x + 0.5f)
                    return true;
            }
            foreach (var o in Obstacles)
            {
                if (o.body == null || !o.body.gameObject.activeInHierarchy)
                    continue;
                Relative(i, o.body.position, out float along, out float lateral);
                if (along > back - o.half && along < lookAhead && Mathf.Abs(lateral - shift) < _half[i].x + o.half + 0.5f)
                    return true;
            }
            return false;
        }

        void Signal(int i, int side, float seconds)
        {
            _blink[i] = side;
            _blinkUntil[i] = Time.time + seconds;
        }

        // Small amber lamps on the signalling side, front and back corners, flashing about 80 times a minute.
        // Made the first time that car signals, then reused.
        void Blink(int i)
        {
            var body = cars[i].body;
            if (body == null)
                return;
            if (!_blinkers.TryGetValue(i, out var lamps))
            {
                if (_amber == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Unlit");
                    _amber = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { name = "Traffic indicator" };
                    var amber = new Color(1f, 0.5f, 0.05f);
                    _amber.color = amber;
                    if (_amber.HasProperty("_BaseColor"))
                        _amber.SetColor("_BaseColor", amber);
                }
                var h = _half[i];
                lamps = new Renderer[4];
                for (int k = 0; k < 4; k++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(go.GetComponent<Collider>());
                    go.name = "Indicator";
                    go.transform.SetParent(body, false);
                    float x = (k & 1) == 0 ? -h.x : h.x, z = (k & 2) == 0 ? h.y - 0.1f : -h.y + 0.1f;
                    go.transform.localPosition = new Vector3(x, 0.75f, z);
                    go.transform.localScale = new Vector3(0.12f, 0.08f, 0.12f);
                    var r = go.GetComponent<Renderer>();
                    r.sharedMaterial = _amber;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.enabled = false;
                    lamps[k] = r;
                }
                _blinkers[i] = lamps;
            }
            bool done = Time.time > _blinkUntil[i];
            bool on = !done && Mathf.Repeat(Time.time, 0.75f) < 0.38f;
            for (int k = 0; k < 4; k++)
                lamps[k].enabled = on && ((k & 1) == 0 ? _blink[i] < 0 : _blink[i] > 0);
            if (done)
                _blink[i] = 0;
        }

        // Free distance before the nearest body in the corridor ahead (crossing, oncoming in-lane, or same way).
        float CorridorRoom(int i, out bool still)
        {
            float room = float.MaxValue;
            still = false;
            // While passing, still things in the home lane no longer block: only what is in the lane being moved into.
            float towards = _shiftTarget[i] - _shift[i];
            bool passing = _shiftTarget[i] != 0f;
            Vector3 p = _pos[i], f = _fwd[i];
            int cx = Mathf.FloorToInt(p.x / CellSize), cz = Mathf.FloorToInt(p.z / CellSize);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    long key = ((long)(cx + dx) << 32) ^ (uint)(cz + dz);
                    if (!_cells.TryGetValue(key, out var cell))
                        continue;
                    foreach (int j in cell)
                    {
                        if (j == i)
                            continue;
                        if (passing && Halted(j))
                        {
                            Relative(i, _pos[j], out float a, out float lat);
                            if (a > 0.2f && a < lookAhead && Mathf.Abs(lat - towards) < corridor)
                                room = Mathf.Min(room, a - avoidGap);
                            continue;
                        }
                        if (!InCorridor(i, j, out float along))
                            continue;
                        if (Halted(j))
                            still = true;
                        // Mutual block (typically two cars meeting at a junction): the lower index goes first, but only
                        // while the other is still well ahead or off to the side; never into a body right in its path.
                        if (InCorridor(j, i, out _) && i < j)
                        {
                            Vector3 d = _pos[j] - _pos[i];
                            float lateral = Mathf.Abs(_fwd[i].x * d.z - _fwd[i].z * d.x);
                            if (lateral > 1.4f || along > avoidGap + 4f)
                                continue;
                        }
                        room = Mathf.Min(room, along - avoidGap);
                    }
                }
            if (Player != null && Player.gameObject.activeInHierarchy)
            {
                Vector3 d = Player.position - p;
                d.y = 0f;
                float along = Vector3.Dot(d, f);
                if (along > 0.2f && along < lookAhead && Mathf.Abs(f.x * d.z - f.z * d.x) < corridor + 0.4f)
                    room = Mathf.Min(room, along - avoidGap * 0.7f);
            }
            foreach (var o in Obstacles)
            {
                if (o.body == null || !o.body.gameObject.activeInHierarchy)
                    continue;
                var op = o.body.position;
                if ((op.y - p.y) * (op.y - p.y) > 9f)
                    continue;   // on a bridge above or a road below
                Relative(i, op, out float along, out float lateral);
                if (passing && o.still)
                    lateral -= towards;
                if (along < -o.half || along > lookAhead + o.half || Mathf.Abs(lateral) > _half[i].x + o.half + 0.3f)
                    continue;
                room = Mathf.Min(room, along - o.half - 3f);
                if (o.still)
                    still = true;
            }
            return room;
        }

        bool InCorridor(int i, int j, out float along)
        {
            Vector3 d = _pos[j] - _pos[i];
            d.y = 0f;
            along = Vector3.Dot(d, _fwd[i]);
            if (along <= 0.2f || along > lookAhead)
                return false;
            float lateral = Mathf.Abs(_fwd[i].x * d.z - _fwd[i].z * d.x);
            return lateral < corridor;
        }

        void Animate(int i, Car car, float accel, float dt)
        {
            if (car.wheels != null && car.wheels.Length > 0)
            {
                float degrees = _current[i] * dt / Mathf.Max(0.05f, car.wheelRadius) * Mathf.Rad2Deg;
                foreach (var w in car.wheels)
                    if (w != null)
                        w.Rotate(Vector3.right, degrees, Space.Self);
            }
            if (car.model != null)
            {
                // Nose dips under braking, lifts under acceleration; a degree or two at most.
                _pitch[i] = Mathf.Lerp(_pitch[i], Mathf.Clamp(-accel * 0.35f, -2f, 2f), dt * 6f);
                car.model.localRotation = Quaternion.Euler(_pitch[i], 0f, 0f) * _modelBase[i];
            }
            if (car.animator != null)
                car.animator.speed = _animBase[i] * Mathf.Clamp(_current[i] / Mathf.Max(0.1f, car.speed), 0.05f, 1.2f);
        }

        void Place(int index, Car car, float[] cum, float total)
        {
            var path = paths[car.path];
            var pts = path.points;
            float d = car.reverse ? total - car.distance : car.distance;
            int i = Array.BinarySearch(cum, d);
            if (i < 0)
                i = ~i - 1;
            i = Mathf.Clamp(i, 0, pts.Length - 2);

            float segment = cum[i + 1] - cum[i];
            float t = segment > 0f ? (d - cum[i]) / segment : 0f;
            Vector3 pos = Vector3.Lerp(pts[i], pts[i + 1], t);
            Vector3 dir = pts[i + 1] - pts[i];
            if (car.reverse)
                dir = -dir;
            if (dir.sqrMagnitude < 1e-6f)
                return;

            var flat = new Vector3(dir.x, 0f, dir.z).normalized;
            var side = new Vector3(flat.z, 0f, -flat.x);    // right of travel
            float offset = car.offset + (_shift != null ? _shift[index] : 0f);
            pos += side * offset;
            var rot = Quaternion.LookRotation(upright ? flat : dir.normalized);

            if (_terrain != null && (pos - _cam).sqrMagnitude < SnapRange * SnapRange)
            {
                float lift = _lift[car.path];
                bool close = (pos - _cam).sqrMagnitude < RayRange * RayRange;
                if (upright)
                    {
                    float expected = Height(pos) + lift;
                    pos.y = close && SurfaceAt(pos, expected, out float feet) ? feet : expected;
                }
                else
                {
                    // Height under each wheel on the carriageway plane, then pitch and roll to fit all four.
                    Vector3 centre = pos - side * offset;
                    float half = paths[car.path].halfWidth;
                    var h = _half[index];
                    float Surface(float along, float lateral)
                    {
                        Vector3 c = centre + flat * along;
                        float plane;
                        if (half <= 0.5f)
                            plane = Height(c + side * lateral) + lift;
                        else
                        {
                            float l = Height(c - side * half), r = Height(c + side * half);
                            plane = Mathf.Lerp(l, r, Mathf.Clamp01((lateral + half) / (2f * half))) + lift;
                        }
                        return close && SurfaceAt(c + side * lateral, plane, out float wheel) ? wheel : plane;
                    }
                    float fl = Surface(h.y, offset - h.x), fr = Surface(h.y, offset + h.x);
                    float bl = Surface(-h.y, offset - h.x), br = Surface(-h.y, offset + h.x);
                    // A wheel over a kerb or a verge never lifts the car into the air: it sinks into it a little.
                    float low = Mathf.Min(Mathf.Min(fl, fr), Mathf.Min(bl, br)) + 0.25f;
                    fl = Mathf.Min(fl, low); fr = Mathf.Min(fr, low); bl = Mathf.Min(bl, low); br = Mathf.Min(br, low);
                    pos.y = (fl + fr + bl + br) * 0.25f;
                    var forward = (flat * (2f * h.y) + Vector3.up * ((fl + fr) - (bl + br)) * 0.5f).normalized;
                    var right = (side * (2f * h.x) + Vector3.up * ((fr + br) - (fl + bl)) * 0.5f).normalized;
                    rot = Quaternion.LookRotation(forward, Vector3.Cross(forward, right));
                }
            }
            if (car.body != null)
                car.body.SetPositionAndRotation(pos, rot);
        }
    }
}
