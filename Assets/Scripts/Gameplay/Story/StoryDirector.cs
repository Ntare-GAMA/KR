using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KR.Gameplay.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KR.Gameplay.Story
{
    // Rush Week: runs the milestones in order. Each one is a race against its par time; staying inside the road
    // rules is always the faster way, because every broken rule adds time and an officer who sees it stops you.
    // Progress, best times, knowledge cards, fines and demerit points are saved to one JSON file (temp + replace).
    public sealed class StoryDirector : MonoBehaviour
    {
        public enum Phase { Waiting, Briefing, Rushing, Dwelling, PoliceStop, Results, WeekDone }

        [SerializeField] StoryWeek week;
        [SerializeField] Transform player;
        [SerializeField] PlayerMotor motor;
        [SerializeField] PlayerRig rig;
        [SerializeField] RoadRulesMonitor rules;
        [SerializeField] KigaliWeather weather;
        [SerializeField] TrafficRhythm rhythm;
        [SerializeField] Transform beacon;
        [SerializeField] DrivingCoach coach;
        [SerializeField, Tooltip("Clean driving (no broken rule) a learner needs for the full licence; fines apply after that.")]
        float licenceMetres = 3000f;

        public Phase State { get; private set; } = Phase.Waiting;
        public StoryWeek Week => week;
        public MilestoneDefinition Current => week != null && _save.next < week.milestones.Length ? week.milestones[_save.next] : null;
        public float Elapsed { get; private set; }
        public float Penalty { get; private set; }
        public float Remaining => Current != null ? Current.parSeconds - Elapsed - Penalty : 0f;
        public float DwellProgress => Current != null && Current.dwellSeconds > 0f ? Mathf.Clamp01(_dwell / Current.dwellSeconds) : 0f;
        public float Distance { get; private set; }
        public int LastStars { get; private set; }
        public float LastTime { get; private set; }
        public bool LastWasBest { get; private set; }
        public KnowledgeCard LastCard { get; private set; }
        public string Toast { get; private set; }
        public float ToastUntil { get; private set; }
        public string StopMessage { get; private set; }
        public int Demerits => _save.demeritPoints;
        public int Fines => _save.finesFrw;
        public int CardCount => _save.cards.Length;
        public string DayName => Current != null && week.dayNames.Length > Current.day ? week.dayNames[Current.day] : "";
        public KigaliWeather Weather => weather;
        public TrafficRhythm Rhythm => rhythm;
        public RoadRulesMonitor Rules => rules;
        public GhostTrack Ghost => _ghostBest;
        public DrivingCoach Coach => coach;
        public bool Learner => !_save.fullLicence;
        public float CleanMetres => _save.learnerCleanMetres;
        public float LicenceMetres => licenceMetres;
        public int Session => _save.sessions;

        StorySave _save = new StorySave();
        float _dwell;
        bool _dwellAnim;
        RoadRule _stopRule;
        Vector3 _stopAt;
        float _stopTimer;
        bool _stopHolding;
        readonly List<Vector3> _track = new List<Vector3>();
        float _trackTimer;
        GhostTrack _ghostBest;
        Transform _ghostMarker;
        Vector3 _lastDrivePos;
        bool _driving;

        static string SavePath => Path.Combine(Application.persistentDataPath, "story_save.json");
        static string GhostPath(string id) => Path.Combine(Application.persistentDataPath, "ghosts", id + ".json");

        public void Configure(StoryWeek content, Transform playerBody, PlayerMotor playerMotor, PlayerRig playerRig,
                              RoadRulesMonitor monitor, KigaliWeather kigaliWeather, TrafficRhythm traffic, Transform marker,
                              DrivingCoach drivingCoach)
        {
            week = content;
            player = playerBody;
            motor = playerMotor;
            rig = playerRig;
            rules = monitor;
            weather = kigaliWeather;
            rhythm = traffic;
            beacon = marker;
            coach = drivingCoach;
        }

        // Every launch is a new session: count it and give the weather its session number (it changes per session).
        void Awake()
        {
            Load();
            _save.sessions++;
            Save();
            if (weather != null)
                weather.BeginSession(_save.sessions);
        }

        void OnEnable()
        {
            if (rules != null)
                rules.Violation += OnViolation;
        }

        void OnDisable()
        {
            if (rules != null)
                rules.Violation -= OnViolation;
        }

        bool InControl => rig == null || rig.isActiveAndEnabled;

        void Update()
        {
            bool confirm = RigInput.ConfirmPressed;
            if (RigInput.BeltPressed && rules != null)
                Notify(rules.SeatbeltOn ? "Seatbelt off." : "Seatbelt on.", 2f, () => rules.SeatbeltOn = !rules.SeatbeltOn);
            float dt = Time.deltaTime;
            var m = Current;
            UpdateBeacon(m);
            UpdateLearner();

            switch (State)
            {
                case Phase.Waiting:
                    if (week == null || player == null)
                        return;
                    if (InControl)
                        State = m == null ? Phase.WeekDone : Phase.Briefing;
                    break;

                case Phase.Briefing:
                    if (confirm)
                        Begin();
                    break;

                case Phase.Rushing:
                case Phase.Dwelling:
                    if (!InControl)
                        break;
                    Elapsed += dt;
                    Record(dt);
                    Distance = Flat(player.position, m.position);
                    bool inside = Distance <= m.radius;
                    if (!inside)
                    {
                        if (State == Phase.Dwelling)
                            StopDwellAnimation();
                        State = Phase.Rushing;
                        break;
                    }
                    if (m.dwellSeconds <= 0f)
                    {
                        Complete();
                        break;
                    }
                    if (State == Phase.Rushing)
                    {
                        State = Phase.Dwelling;
                        if (m.arrivalLines != null && m.arrivalLines.Length > 0)
                            Notify($"{m.speaker}: {m.arrivalLines[0]}", 5f);
                    }
                    if (rules == null || rules.Mode == Travel.Walk)
                    {
                        _dwell += dt;
                        StartDwellAnimation(m);
                    }
                    if (_dwell >= m.dwellSeconds)
                        Complete();
                    break;

                case Phase.PoliceStop:
                    Elapsed += dt;
                    UpdateStop(dt);
                    break;

                case Phase.Results:
                    if (confirm)
                    {
                        State = Current == null ? Phase.WeekDone : Phase.Briefing;
                        LastCard = null;
                    }
                    break;
            }
        }

        void Begin()
        {
            Elapsed = Penalty = 0f;
            _dwell = 0f;
            _dwellAnim = false;
            _track.Clear();
            _trackTimer = 0f;
            State = Phase.Rushing;
            if (Current.startAtPoint && motor != null)
                motor.Teleport(Current.startPoint, 0f);
            Result(Current.id).attempts++;
            _ghostBest = LoadGhost(Current.id);
            Notify($"GO! {Current.title}: par {Mmss(Current.parSeconds)}", 3f);
        }

        void Complete()
        {
            var m = Current;
            StopDwellAnimation();
            float total = Elapsed + Penalty;
            float left = (m.parSeconds - total) / Mathf.Max(1f, m.parSeconds);
            LastStars = left >= 0.4f ? 3 : left >= 0.15f ? 2 : left >= 0f ? 1 : 0;
            LastTime = total;
            var r = Result(m.id);
            LastWasBest = r.bestSeconds < 0f || total < r.bestSeconds;
            if (LastWasBest)
            {
                r.bestSeconds = total;
                SaveGhost(m.id, total);
            }
            r.bestStars = Mathf.Max(r.bestStars, LastStars);
            LastCard = week.Card(m.knowledgeCardId);
            if (LastCard != null && !_save.cards.Contains(LastCard.id))
                _save.cards = _save.cards.Append(LastCard.id).ToArray();
            _save.next++;
            if (Current != null)
                _save.day = Current.day;
            Save();
            State = Phase.Results;
        }

        // ---------- road rules and police ----------

        // Learners earn the full licence by driving licenceMetres without breaking a rule; any broken rule restarts the count.
        void UpdateLearner()
        {
            if (coach != null)
                coach.Learner = Learner;
            bool driving = rules != null && rules.Mode == Travel.Car && player != null;
            if (!driving || !Learner)
            {
                _driving = false;
                return;
            }
            Vector3 pos = player.position;
            if (_driving)
            {
                float step = Flat(pos, _lastDrivePos);
                if (step < 20f)   // ignore teleports
                    _save.learnerCleanMetres += step;
            }
            _driving = true;
            _lastDrivePos = pos;
            if (_save.learnerCleanMetres >= licenceMetres)
            {
                _save.fullLicence = true;
                Save();
                Notify($"Full licence! You drove {licenceMetres / 1000f:0.#} km without breaking a rule. From now on police fines apply.", 8f);
            }
        }

        void OnViolation(RoadRule rule, Vector3 where)
        {
            bool racing = State == Phase.Rushing || State == Phase.Dwelling;
            if (Learner)
            {
                // A lesson, not a fine: explain the rule, cost only clock time, and restart the clean-driving count.
                _save.learnerCleanMetres = 0f;
                if (racing)
                    Penalty += rule.timePenalty;
                Notify($"Lesson: {rule.title}. {rule.explanation} Learners are not fined, but your clean driving starts again.", 7f);
                if (rule.endsRun && racing)
                    Fail(rule);
                Save();
                return;
            }
            if (!racing)
                return;
            Penalty += rule.timePenalty;
            _save.demeritPoints += rule.demeritPoints;
            var post = PolicePost.Watching(where, rule.id);
            string seen = post == null ? "" : post.CanStop ? " An officer saw it." : " Speed camera.";
            Notify($"{rule.title} (+{rule.timePenalty:0} s). {rule.citation}.{seen}", 5f);
            if (rule.endsRun)
            {
                Fail(rule);
                return;
            }
            if (post != null)
            {
                _save.finesFrw += rule.fineFrw;
                if (post.CanStop)
                    StartStop(rule, where);
            }
            Save();
        }

        // Pull over within 15 s and stay put until the officer is done (Art. 6: officers' instructions prevail).
        // Driving away more than 120 m is refusing to stop (Art. 39), which ends the milestone.
        void StartStop(RoadRule rule, Vector3 where)
        {
            _stopRule = rule;
            _stopAt = where;
            _stopTimer = 15f;
            _stopHolding = false;
            StopMessage = "An officer signals you to stop. Pull over now.";
            State = Phase.PoliceStop;
        }

        void UpdateStop(float dt)
        {
            float moved = Flat(player.position, _stopAt);
            if (moved > 120f)
            {
                var refuse = week.Rule(RuleId.RefuseStop);
                if (refuse != null)
                {
                    _save.finesFrw += refuse.fineFrw;
                    Fail(refuse);
                }
                else
                    State = Phase.Rushing;
                return;
            }
            bool stopped = rules == null || rules.SpeedKmh < 2f;
            _stopTimer -= dt;
            if (!_stopHolding)
            {
                if (stopped)
                {
                    _stopHolding = true;
                    _stopTimer = 10f;
                    StopMessage = "The officer checks your licence and insurance.";
                }
                else if (_stopTimer <= 0f)
                {
                    StopMessage = "You did not stop. The officer is following you.";
                    _stopTimer = 15f;
                }
                return;
            }
            if (!stopped)
                _stopTimer = Mathf.Max(_stopTimer, 6f);   // moving again restarts part of the check
            if (_stopTimer <= 0f)
            {
                string fine = _stopRule.fromOrder ? $"FRW {_stopRule.fineFrw:N0}" : "fine to be set by the Minister's Order";
                Notify($"Electronic fault notice (Law 014/2026, Art. 35): {_stopRule.title}, {fine}. You may continue.", 6f);
                StopMessage = null;
                State = Phase.Rushing;
            }
        }

        void Fail(RoadRule rule)
        {
            StopDwellAnimation();
            StopMessage = null;
            Notify($"Milestone failed: {rule.title}. {rule.explanation}", 8f);
            Save();
            State = Phase.Briefing;
        }

        // ---------- dwelling (tea, reading, class) ----------

        void StartDwellAnimation(MilestoneDefinition m)
        {
            if (_dwellAnim || motor == null || string.IsNullOrEmpty(m.dwellAnimation))
                return;
            _dwellAnim = motor.HasState(m.dwellAnimation) && motor.Play(m.dwellAnimation);
        }

        void StopDwellAnimation()
        {
            if (_dwellAnim && motor != null)
                motor.BackToLocomotion();
            _dwellAnim = false;
        }

        // ---------- ghosts: your best run, and later other players' runs from the leaderboard ----------

        [Serializable]
        public sealed class GhostTrack
        {
            public string milestone;
            public float seconds;
            public float interval = 0.25f;
            public Vector3[] points;

            public Vector3 At(float t)
            {
                if (points == null || points.Length == 0)
                    return Vector3.zero;
                float f = t / interval;
                int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, points.Length - 1);
                int j = Mathf.Min(i + 1, points.Length - 1);
                return Vector3.Lerp(points[i], points[j], f - i);
            }
        }

        void Record(float dt)
        {
            _trackTimer -= dt;
            if (_trackTimer > 0f)
                return;
            _trackTimer = 0.25f;
            _track.Add(player.position);
            if (_ghostMarker != null && _ghostBest != null && State != Phase.Results)
                _ghostMarker.position = _ghostBest.At(Elapsed) + Vector3.up;
        }

        void SaveGhost(string id, float seconds)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(GhostPath(id)));
                var g = new GhostTrack { milestone = id, seconds = seconds, points = _track.ToArray() };
                WriteAtomic(GhostPath(id), JsonUtility.ToJson(g));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Story] Ghost not saved: {e.Message}");
            }
        }

        GhostTrack LoadGhost(string id)
        {
            try
            {
                if (File.Exists(GhostPath(id)))
                {
                    var g = JsonUtility.FromJson<GhostTrack>(File.ReadAllText(GhostPath(id)));
                    EnsureGhostMarker();
                    return g;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Story] Ghost not loaded: {e.Message}");
            }
            return null;
        }

        void EnsureGhostMarker()
        {
            if (_ghostMarker != null)
                return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Ghost (your best run)";
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", new Color(0.3f, 0.9f, 1f));
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ghostMarker = go.transform;
        }

        // ---------- helpers ----------

        void UpdateBeacon(MilestoneDefinition m)
        {
            if (beacon == null)
                return;
            bool show = m != null && (State == Phase.Briefing || State == Phase.Rushing || State == Phase.Dwelling || State == Phase.PoliceStop);
            beacon.gameObject.SetActive(show);
            if (show)
                beacon.position = m.position;
        }

        void Notify(string text, float seconds, Action then = null)
        {
            then?.Invoke();
            Toast = text;
            ToastUntil = Time.time + seconds;
        }

        MilestoneResult Result(string id)
        {
            var r = _save.results.FirstOrDefault(x => x.id == id);
            if (r == null)
            {
                r = new MilestoneResult { id = id };
                _save.results = _save.results.Append(r).ToArray();
            }
            return r;
        }

        public MilestoneResult BestFor(string id) => _save.results.FirstOrDefault(x => x.id == id);

        public void RestartWeek()
        {
            _save = new StorySave();
            Save();
            State = Phase.Waiting;
        }

        void Load()
        {
            try
            {
                if (File.Exists(SavePath))
                    _save = JsonUtility.FromJson<StorySave>(File.ReadAllText(SavePath)) ?? new StorySave();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Story] Save unreadable, starting fresh: {e.Message}");
                _save = new StorySave();
            }
        }

        void Save()
        {
            try
            {
                WriteAtomic(SavePath, JsonUtility.ToJson(_save, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Story] Save failed: {e.Message}");
            }
        }

        static void WriteAtomic(string path, string json)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        public static string Mmss(float seconds)
        {
            bool neg = seconds < 0f;
            seconds = Mathf.Abs(seconds);
            return $"{(neg ? "-" : "")}{(int)(seconds / 60f)}:{(int)(seconds % 60f):00}";
        }
    }
}
