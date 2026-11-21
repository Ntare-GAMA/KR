using System.Collections.Generic;
using KR.Gameplay.Player;
using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // Satnav for the city. The destination is the current milestone's place unless you pick one: N / right grip opens
    // the destination list (DestinationPicker) with the story's places and every named place in OpenStreetMap
    // (cafes, pharmacies, banks, schools...). The route follows every real street (one-way streets respected while
    // driving), is recalculated when you leave it, and gives live turn-by-turn directions on foot and in the car.
    public sealed class Navigator : MonoBehaviour
    {
        [SerializeField] StoryDirector director;
        [SerializeField] PlayerRig rig;
        [SerializeField] RoadNetworkTraffic roads;
        [SerializeField] float offRoute = 30f;
        [SerializeField] float arriveRadius = 25f;
        [SerializeField, Tooltip("All streets (walking and driving routes, street names). Falls back to the traffic paths.")]
        StreetNetwork streets;
        [SerializeField] PlaceCatalog catalog;

        RoadGraph _graph;
        readonly List<(string name, Vector3 pos)> _places = new List<(string, Vector3)>();
        readonly List<string> _groups = new List<string>();
        float _streetTimer;
        bool _arrivedSaid;
        int _choice = -1;                 // -1 = follow the current milestone
        readonly List<Vector3> _route = new List<Vector3>();
        int _progress;                    // index of the route point the player has reached
        Vector3? _target;
        bool _drivingRoute;
        float _replan;

        public IReadOnlyList<Vector3> Route => _route;
        public RoadGraph Graph => _graph;
        public int Progress => _progress;
        public int RouteVersion { get; private set; }
        public string DestinationName { get; private set; }
        public Vector3? Destination => _target;
        public string Instruction { get; private set; }
        public float Remaining { get; private set; }
        public IReadOnlyList<(string name, Vector3 pos)> Places => _places;
        public int Choice => _choice;
        public string GroupOf(int i) => i >= 0 && i < _groups.Count ? _groups[i] : "story";
        public string StreetName { get; private set; }
        public bool OnTour => _tour.Count > 0;
        public string TourLabel => _tour.Count > 0 ? $"Tour stop {_tourIndex + 1}/{_tour.Count}: {_tour[_tourIndex].name}" : null;

        // -1 follows the milestone; otherwise a place from Places. Ends any tour.
        public void ChooseDestination(int index)
        {
            _tour.Clear();
            _choice = Mathf.Clamp(index, -1, _places.Count - 1);
            if (rig != null)
                rig.Toast(_choice < 0 ? "Satnav: following the milestone." : $"Satnav: going to {_places[_choice].name}.", 3f);
        }

        // A guided route through several places, in order (the Kimironko tour).
        readonly List<(string name, Vector3 pos)> _tour = new List<(string, Vector3)>();
        int _tourIndex;
        public System.Action TourFinished;

        public void StartTour(IEnumerable<(string name, Vector3 pos)> stops)
        {
            _tour.Clear();
            _tour.AddRange(stops);
            _tourIndex = 0;
            if (_tour.Count > 0 && rig != null)
                rig.Toast($"Kimironko tour: {_tour.Count} stops. First: {_tour[0].name}.", 4f);
        }
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

        public void SetDirectory(PlaceCatalog places, StreetNetwork network)
        {
            catalog = places;
            streets = network;
        }

        void Start()
        {
            _graph = streets != null && streets.streets.Length > 0 ? new RoadGraph(streets.Paths())
                   : roads != null ? new RoadGraph(roads.Paths) : null;
            if (rig != null)
                Add("Home", rig.Home, "story");
            var week = director != null ? director.Week : null;
            if (week != null && week.milestones != null)
                foreach (var m in week.milestones)
                    if (m != null && !string.IsNullOrEmpty(m.placeName) && !_places.Exists(p => p.name == m.placeName))
                        Add(m.placeName, m.position, "story");
            if (catalog != null && catalog.directory != null)
                foreach (var e in catalog.directory)
                    if (!string.IsNullOrEmpty(e.name))
                        Add(e.name, e.position, e.group);
        }

        void Add(string name, Vector3 pos, string group)
        {
            _places.Add((name, pos));
            _groups.Add(string.IsNullOrEmpty(group) ? "other" : group);
        }

        void Update()
        {
            if (_graph == null || _graph.Count == 0 || rig == null)
                return;
            _streetTimer -= Time.deltaTime;
            if (_streetTimer <= 0f)
            {
                _streetTimer = 0.5f;
                StreetName = streets != null ? streets.NameAt(PlayerPosition) : null;
            }

            // What to navigate to: the chosen place, or the milestone in play.
            string name = null;
            Vector3? target = null;
            if (_tour.Count > 0)
            {
                var stop = _tour[_tourIndex];
                if (Flat(PlayerPosition, stop.pos) < arriveRadius + 10f)
                {
                    _tourIndex++;
                    if (_tourIndex >= _tour.Count)
                    {
                        _tour.Clear();
                        rig.Toast("Tour complete!", 4f);
                        TourFinished?.Invoke();
                    }
                    else
                        rig.Toast($"Next stop: {_tour[_tourIndex].name}.", 4f);
                }
            }
            if (_tour.Count > 0)
            {
                name = _tour[_tourIndex].name;
                target = _tour[_tourIndex].pos;
            }
            else if (_choice >= 0 && _choice < _places.Count)
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
                _arrivedSaid = false;
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

        public void NextDestination()
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
            // A place you picked yourself (a cafe, a pharmacy) is reached at its door; milestones keep their wider radius.
            if (remaining < (_choice >= 0 && _tour.Count == 0 ? 8f : arriveRadius))
            {
                Instruction = $"You have arrived at {DestinationName}.";
                if (!_arrivedSaid)
                {
                    _arrivedSaid = true;
                    rig.Toast(Instruction, 4f);
                }
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
