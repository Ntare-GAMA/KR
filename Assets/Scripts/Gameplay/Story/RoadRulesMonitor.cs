using System;
using System.Collections.Generic;
using KR.Gameplay.Player;
using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // Watches the player while driving and reports broken road rules. What it checks, and the source:
    //   right-hand side (Law 014/2026 Art. 4), licence (Art. 40), red signals, STOP signs and speed (Art. 5; details
    //   in the Minister's Order, so limits are placeholders until supplied), seatbelt and helmet (Order, placeholders).
    // It also answers look-ahead questions (next signal, next STOP sign) for the driving coach.
    // It never punishes by itself: StoryDirector decides time penalties, fines and police stops.
    public sealed class RoadRulesMonitor : MonoBehaviour
    {
        [SerializeField] StoryWeek week;
        [SerializeField] Transform player;
        [SerializeField] RoadNetworkTraffic vehicles;
        [SerializeField] StopSigns stopSigns;
        [SerializeField] bool hasLicence = true;
        [SerializeField] float speedTolerance = 5f;
        [SerializeField] float repeatCooldown = 8f;

        public event Action<RoadRule, Vector3> Violation;

        public bool HasLicence { get => hasLicence; set => hasLicence = value; }
        public bool SeatbeltOn { get; set; } = true;
        public bool HelmetOn { get; set; } = true;
        public Travel Mode { get; private set; } = Travel.Walk;
        public float SpeedKmh { get; private set; }
        public float LimitKmh => week != null ? week.urbanSpeedLimitKmh : 40f;
        public bool WrongSideNow => _wrongSide > 0f;
        public DrivableCar Driven => _driven;

        DrivableCar[] _cars;
        DrivableCar _driven;
        Vector3 _lastPos;
        float _overSpeed, _wrongSide, _sideTimer;
        bool _licenceReported, _beltReported;
        readonly Dictionary<RuleId, float> _cooldown = new Dictionary<RuleId, float>();
        // STOP signs being approached: sign index -> lowest speed (km/h) in the last stretch before the line.
        readonly Dictionary<int, float> _stopApproach = new Dictionary<int, float>();
        readonly List<int> _stopDone = new List<int>();
        const float StopZone = 25f;      // the full stop must happen within this many metres before the line
        const float StoppedKmh = 3f;

        // Signal stop lines (both directions) and a coarse grid of road segments for the side-of-road check.
        struct Line { public Vector3 p, dir; public int junction, phase; }
        readonly List<Line> _lines = new List<Line>();
        readonly Dictionary<long, List<(Vector3 a, Vector3 b, bool oneway)>> _segments = new Dictionary<long, List<(Vector3, Vector3, bool)>>();
        const float Cell = 25f;

        public void Configure(StoryWeek content, Transform playerBody, RoadNetworkTraffic vehicleTraffic, StopSigns signs)
        {
            week = content;
            player = playerBody;
            vehicles = vehicleTraffic;
            stopSigns = signs;
        }

        void Start()
        {
            _cars = FindObjectsByType<DrivableCar>(FindObjectsSortMode.None);
            if (vehicles == null)
                return;
            for (int i = 0; i < vehicles.StopCount; i++)
                if (vehicles.StopLines(i, out var fp, out var fd, out var rp, out var rd, out int j, out int ph))
                {
                    _lines.Add(new Line { p = fp, dir = fd, junction = j, phase = ph });
                    _lines.Add(new Line { p = rp, dir = rd, junction = j, phase = ph });
                }
            var paths = vehicles.Paths;
            if (paths == null)
                return;
            foreach (var path in paths)
                for (int k = 1; k < path.points.Length; k++)
                {
                    Vector3 a = path.points[k - 1], b = path.points[k];
                    long key = Key((a + b) * 0.5f);
                    if (!_segments.TryGetValue(key, out var list))
                        _segments[key] = list = new List<(Vector3, Vector3, bool)>();
                    list.Add((a, b, path.oneway));
                }
            if (player != null)
                _lastPos = player.position;
        }

        static long Key(Vector3 p) => ((long)Mathf.FloorToInt(p.x / Cell) << 32) ^ (uint)Mathf.FloorToInt(p.z / Cell);

        void Update()
        {
            if (player == null)
                return;
            _driven = null;
            if (_cars != null)
                foreach (var c in _cars)
                    if (c != null && c.Driven)
                        _driven = c;
            Mode = _driven != null ? Travel.Car : Travel.Walk;
            Vector3 pos = player.position;
            float dt = Time.deltaTime;

            if (_driven == null)
            {
                SpeedKmh = 0f;
                _licenceReported = _beltReported = false;
                _lastPos = pos;
                return;
            }
            SpeedKmh = Mathf.Abs(_driven.Speed) * 3.6f;

            if (!hasLicence && !_licenceReported)
            {
                _licenceReported = true;
                Report(RuleId.NoLicence, pos);
            }
            if (!SeatbeltOn && !_beltReported && SpeedKmh > 5f)
            {
                _beltReported = true;
                Report(RuleId.Seatbelt, pos);
            }

            _overSpeed = SpeedKmh > LimitKmh + speedTolerance ? _overSpeed + dt : 0f;
            if (_overSpeed > 1f)
            {
                _overSpeed = 0f;
                Report(RuleId.Speeding, pos);
            }

            CheckSignals(_lastPos, pos);
            CheckStopSigns(_lastPos, pos);

            _sideTimer -= dt;
            if (_sideTimer <= 0f)
            {
                _sideTimer = 0.25f;
                _wrongSide = OnWrongSide(pos, _driven.transform.forward) ? _wrongSide + 0.25f : 0f;
                if (_wrongSide >= 1.5f)
                {
                    _wrongSide = 0f;
                    Report(RuleId.DriveOnRight, pos);
                }
            }
            _lastPos = pos;
        }

        void CheckSignals(Vector3 from, Vector3 to)
        {
            var signals = vehicles != null ? vehicles.Signals : null;
            if (signals == null || (to - from).sqrMagnitude < 0.0001f)
                return;
            foreach (var l in _lines)
            {
                if ((l.p - to).sqrMagnitude > 900f)
                    continue;
                float before = Vector3.Dot(from - l.p, l.dir), after = Vector3.Dot(to - l.p, l.dir);
                if (before >= 0f || after < 0f)
                    continue;   // not crossing this line in its direction of travel
                Vector3 lateral = Vector3.Cross(Vector3.up, l.dir);
                if (Mathf.Abs(Vector3.Dot(to - l.p, lateral)) > 7f)
                    continue;
                if (signals.State(l.junction, l.phase) == TrafficSignalSystem.Red)
                    Report(RuleId.RedSignal, to);
            }
        }

        void CheckStopSigns(Vector3 from, Vector3 to)
        {
            if (stopSigns == null)
                return;
            _stopDone.Clear();
            for (int i = 0; i < stopSigns.Count; i++)
            {
                stopSigns.Get(i, out var p, out var dir, out float width);
                if ((p - to).sqrMagnitude > 1600f)
                    continue;
                Vector3 lateral = Vector3.Cross(Vector3.up, dir);
                if (Mathf.Abs(Vector3.Dot(to - p, lateral)) > width * 0.5f + 3f || Vector3.Dot(_driven.transform.forward, dir) < 0.3f)
                {
                    _stopApproach.Remove(i);
                    continue;
                }
                float before = Vector3.Dot(from - p, dir), after = Vector3.Dot(to - p, dir);
                if (after < 0f && after > -StopZone)
                {
                    float min = _stopApproach.TryGetValue(i, out float m) ? m : float.MaxValue;
                    _stopApproach[i] = Mathf.Min(min, SpeedKmh);
                }
                else if (before < 0f && after >= 0f)
                {
                    // Crossed the line: fine only if the car came to a full stop on the way in.
                    if (!_stopApproach.TryGetValue(i, out float m) || m > StoppedKmh)
                        Report(RuleId.StopSign, to);
                    _stopDone.Add(i);
                }
            }
            foreach (int i in _stopDone)
                _stopApproach.Remove(i);
        }

        // ---------- look-ahead for the driving coach ----------

        // The nearest signal stop line ahead of the car in its direction of travel.
        public bool NextSignal(out float distance, out int state, out float secondsToChange)
        {
            distance = float.MaxValue;
            state = TrafficSignalSystem.Green;
            secondsToChange = 99f;
            var signals = vehicles != null ? vehicles.Signals : null;
            if (_driven == null || signals == null)
                return false;
            Vector3 pos = player.position, heading = _driven.transform.forward;
            bool found = false;
            foreach (var l in _lines)
            {
                if (Vector3.Dot(heading, l.dir) < 0.7f || (l.p - pos).sqrMagnitude > 6400f)
                    continue;
                float ahead = Vector3.Dot(l.p - pos, l.dir);
                if (ahead <= 0f || ahead >= distance)
                    continue;
                if (Mathf.Abs(Vector3.Dot(pos - l.p, Vector3.Cross(Vector3.up, l.dir))) > 7f)
                    continue;
                distance = ahead;
                state = signals.State(l.junction, l.phase);
                secondsToChange = signals.SecondsToChange(l.junction, l.phase);
                found = true;
            }
            return found;
        }

        // The nearest STOP sign ahead, and whether the car has already come to a full stop for it.
        public bool NextStopSign(out float distance, out bool stopped)
        {
            distance = float.MaxValue;
            stopped = false;
            if (_driven == null || stopSigns == null)
                return false;
            Vector3 pos = player.position, heading = _driven.transform.forward;
            bool found = false;
            for (int i = 0; i < stopSigns.Count; i++)
            {
                stopSigns.Get(i, out var p, out var dir, out float width);
                if (Vector3.Dot(heading, dir) < 0.5f || (p - pos).sqrMagnitude > 4900f)
                    continue;
                float ahead = Vector3.Dot(p - pos, dir);
                if (ahead <= 0f || ahead >= distance)
                    continue;
                if (Mathf.Abs(Vector3.Dot(pos - p, Vector3.Cross(Vector3.up, dir))) > width * 0.5f + 3f)
                    continue;
                distance = ahead;
                stopped = _stopApproach.TryGetValue(i, out float m) && m <= StoppedKmh;
                found = true;
            }
            return found;
        }

        // Left of the centre line of a two-way road, relative to the direction the car is heading.
        bool OnWrongSide(Vector3 pos, Vector3 heading)
        {
            float best = 12f;
            bool wrong = false;
            int cx = Mathf.FloorToInt(pos.x / Cell), cz = Mathf.FloorToInt(pos.z / Cell);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    long key = ((long)(cx + dx) << 32) ^ (uint)(cz + dz);
                    if (!_segments.TryGetValue(key, out var list))
                        continue;
                    foreach (var (a, b, oneway) in list)
                    {
                        Vector3 ab = b - a;
                        ab.y = 0f;
                        float len = ab.magnitude;
                        if (len < 0.5f)
                            continue;
                        Vector3 dir = ab / len;
                        Vector3 ap = pos - a;
                        ap.y = 0f;
                        float t = Mathf.Clamp(Vector3.Dot(ap, dir), 0f, len);
                        float dist = (ap - dir * t).magnitude;
                        if (dist >= best)
                            continue;
                        best = dist;
                        if (oneway)
                        {
                            wrong = false;
                            continue;
                        }
                        float along = Vector3.Dot(heading, dir);
                        if (Mathf.Abs(along) < 0.6f)
                        {
                            wrong = false;   // turning or crossing: not a side-of-road situation
                            continue;
                        }
                        float side = Vector3.Dot(ap, Vector3.Cross(Vector3.up, dir)) * Mathf.Sign(along);
                        wrong = side < -0.8f;
                    }
                }
            return wrong;
        }

        void Report(RuleId id, Vector3 where)
        {
            if (_cooldown.TryGetValue(id, out float until) && Time.time < until)
                return;
            _cooldown[id] = Time.time + repeatCooldown;
            var rule = week != null ? week.Rule(id) : null;
            if (rule != null)
                Violation?.Invoke(rule, where);
        }

        // Raised by StoryDirector when a stopped driver leaves before the officer has finished (Art. 39).
        public void ReportRefusedStop(Vector3 where) => Report(RuleId.RefuseStop, where);
    }
}
