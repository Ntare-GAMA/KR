using System.Collections.Generic;
using KR.Gameplay.Player;
using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // Satnav for the city. The destination is the current milestone's place; N / right grip cycles through every
    // place in the story (home, campus, market, ...) and back to "follow the milestone". The route follows the real
    // road network (one-way streets respected while driving) and is recalculated when you leave it. It also gives
    // turn-by-turn directions.
    public sealed class Navigator : MonoBehaviour
    {
        [SerializeField] StoryDirector director;
        [SerializeField] PlayerRig rig;
        [SerializeField] RoadNetworkTraffic roads;
        [SerializeField] float offRoute = 30f;
        [SerializeField] float arriveRadius = 25f;

        RoadGraph _graph;
        readonly List<(string name, Vector3 pos)> _places = new List<(string, Vector3)>();
        int _choice = -1;                 // -1 = follow the current milestone
        readonly List<Vector3> _route = new List<Vector3>();
        int _progress;                    // index of the route point the player has reached
        Vector3? _target;
        bool _drivingRoute;
        float _replan;

        public IReadOnlyList<Vector3> Route => _route;
        public int Progress => _progress;
        public int RouteVersion { get; private set; }
        public string DestinationName { get; private set; }
        public Vector3? Destination => _target;
        public string Instruction { get; private set; }
        public float Remaining { get; private set; }
        public IReadOnlyList<(string name, Vector3 pos)> Places => _places;
        public Vector3 PlayerPosition => rig == null ? transform.position
            : rig.CurrentMode == PlayerRig.Mode.Driving && rig.Car != null ? rig.Car.transform.position : rig.Player.transform.position;
        public float PlayerHeading
        {
            get
            {
                if (rig != null && rig.CurrentMode == PlayerRig.Mode.Driving && rig.Car != null)
                    return rig.Car.transform.eulerAngles.y;
                var cam = Camera.main;
                return cam != null ? cam.transform.eulerAngles.y : 0f;
            }
        }

        public void Configure(StoryDirector story, PlayerRig playerRig, RoadNetworkTraffic vehicles)
        {
            director = story;
            rig = playerRig;
            roads = vehicles;
        }

        void Start()
        {
            _graph = roads != null ? new RoadGraph(roads.Paths) : null;
            if (rig != null)
                _places.Add(("Home", rig.Home));
            var week = director != null ? director.Week : null;
            if (week != null && week.milestones != null)
                foreach (var m in week.milestones)
                    if (m != null && !string.IsNullOrEmpty(m.placeName) && !_places.Exists(p => p.name == m.placeName))
                        _places.Add((m.placeName, m.position));
        }

        void Update()
        {
            if (_graph == null || _graph.Count == 0 || rig == null)
                return;
            if (RigInput.DestinationPressed)
                NextDestination();

            // What to navigate to: the chosen place, or the milestone in play.
            string name = null;
            Vector3? target = null;
            if (_choice >= 0 && _choice < _places.Count)
            {
                name = _places[_choice].name;
                target = _places[_choice].pos;
            }
            else if (director != null && director.Current != null && director.State != StoryDirector.Phase.WeekDone)
            {
                name = director.Current.placeName;
                target = director.Current.position;
            }
            bool driving = rig.CurrentMode == PlayerRig.Mode.Driving;
            if (target != _target || driving != _drivingRoute || name != DestinationName)
            {
                _target = target;
                DestinationName = name;
                _drivingRoute = driving;
                Plan();
            }
            if (_target == null)
            {
                Instruction = null;
                return;
            }
            Advance();
            _replan -= Time.deltaTime;
        }

        void NextDestination()
        {
            _choice = _choice + 1 >= _places.Count ? -1 : _choice + 1;
            string label = _choice < 0
                ? (director != null && director.Current != null ? $"Satnav: following the milestone ({director.Current.placeName})." : "Satnav: following the milestone.")
                : $"Satnav: going to {_places[_choice].name}.";
            rig.Toast(label, 3f);
        }

        void Plan()
        {
            _route.Clear();
            _progress = 0;
            _replan = 2f;
            RouteVersion++;
            if (_target == null)
                return;
            int from = _graph.Nearest(PlayerPosition), to = _graph.Nearest(_target.Value);
            var path = _graph.Route(from, to, _drivingRoute) ?? (_drivingRoute ? _graph.Route(from, to, false) : null);
            if (path == null)
            {
                Instruction = "No road route found from here.";
                return;
            }
            _route.AddRange(path);
            _route.Add(_target.Value);
        }

        // Moves progress along the route, recalculates when the player has left it, and works out the next turn.
        void Advance()
        {
            if (_route.Count < 2)
                return;
            Vector3 p = PlayerPosition;
            float best = float.MaxValue;
            int bestIndex = _progress;
            int end = Mathf.Min(_route.Count - 1, _progress + 60);
            for (int i = _progress; i < end; i++)
            {
                float d = SegmentDistance(p, _route[i], _route[i + 1]);
                if (d < best)
                {
                    best = d;
                    bestIndex = i;
                }
            }
            _progress = bestIndex;
            if (best > offRoute && _replan <= 0f)
            {
                Plan();
                return;
            }

            float remaining = Flat(p, _route[_progress + 1]);
            for (int i = _progress + 1; i < _route.Count - 1; i++)
                remaining += Flat(_route[i], _route[i + 1]);
            Remaining = remaining;
            if (remaining < arriveRadius)
            {
                Instruction = $"You have arrived at {DestinationName}.";
                return;
            }
            Instruction = NextTurn(p);
        }

        // The first real turn ahead (direction change over about 15 m either side of a point).
        string NextTurn(Vector3 p)
        {
            float along = Flat(p, _route[_progress + 1]);
            for (int i = _progress + 1; i < _route.Count - 1; i++)
            {
                Vector3 inDir = Heading(i, -15f), outDir = Heading(i, 15f);
                float angle = Vector3.SignedAngle(inDir, outDir, Vector3.up);
                if (Mathf.Abs(angle) > 35f && along > 3f)
                {
                    string turn = Mathf.Abs(angle) > 135f ? "Turn around" : angle > 0f ? (angle > 70f ? "Turn right" : "Bear right")
                        : (angle < -70f ? "Turn left" : "Bear left");
                    return $"{turn} in {Round(along)} · {Km(Remaining)} to {DestinationName}";
                }
                along += Flat(_route[i], _route[i + 1]);
                if (along > 1500f)
                    break;
            }
            return $"Continue for {Round(along)} · {Km(Remaining)} to {DestinationName}";
        }

        Vector3 Heading(int i, float metres)
        {
            int step = metres > 0f ? 1 : -1;
            float left = Mathf.Abs(metres);
            int j = i;
            while (left > 0f && j + step >= 0 && j + step < _route.Count)
            {
                left -= Flat(_route[j], _route[j + step]);
                j += step;
            }
            var d = metres > 0f ? _route[j] - _route[i] : _route[i] - _route[j];
            d.y = 0f;
            return d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward;
        }

        static string Round(float m) => m < 100f ? $"{Mathf.Max(10f, Mathf.Round(m / 10f) * 10f):0} m" : m < 1000f ? $"{Mathf.Round(m / 50f) * 50f:0} m" : Km(m);
        static string Km(float m) => m < 1000f ? $"{Mathf.Round(m / 10f) * 10f:0} m" : $"{m / 1000f:0.0} km";
        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 pp = new Vector2(p.x, p.z), aa = new Vector2(a.x, a.z), bb = new Vector2(b.x, b.z);
            var ab = bb - aa;
            float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(pp - aa, ab) / ab.sqrMagnitude);
            return Vector2.Distance(pp, aa + ab * t);
        }
    }
}
