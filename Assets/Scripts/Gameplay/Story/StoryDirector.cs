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
        public string StopMessage => _stop.Message;
        public int Demerits => _save.demeritPoints;
        public int Fines => _save.finesFrw;
        public int CardCount => _save.cards.Length;
        public string DayName => Current != null && week.dayNames.Length > Current.day ? week.dayNames[Current.day] : "";
        public KigaliWeather Weather => weather;
        public TrafficRhythm Rhythm => rhythm;
        public RoadRulesMonitor Rules => rules;
        public GhostTrack Ghost => _ghosts?.Best;
        public DrivingCoach Coach => coach;
        public bool Learner => !_save.fullLicence;
        public float CleanMetres => _save.learnerCleanMetres;
        public float LicenceMetres => licenceMetres;
        public int Session => _save.sessions;

        StorySave _save = new StorySave();
        float _dwell;
        bool _dwellAnim;
        readonly TrafficStop _stop = new TrafficStop();
        GhostRecorder _ghosts;
        Vector3 _lastDrivePos;
        bool _driving;

        static string SavePath => Path.Combine(Application.persistentDataPath, "story_save.json");

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
            _ghosts = new GhostRecorder(Path.Combine(Application.persistentDataPath, "ghosts"));
            Load();
            _save.sessions++;
            Save();
            if (weather != null)
                weather.BeginSession(_save.sessions);
            ApplyComfort();
        }

        // ---------- progression: XP, badges, discoveries, flags (every reward has a unique key) ----------

        public int Xp => _save.xp;
        public int Level => Badges.LevelFor(_save.xp);
        public IReadOnlyList<string> BadgesEarned => _save.badges;
        public IReadOnlyList<PhotoRecord> Photos => _save.photos;
        public ComfortSave ComfortSettings => _save.comfort;
        public bool IsDiscovered(string placeId) => _save.discovered.Contains(placeId);
        public bool HasFlag(string flag) => _save.flags.Contains(flag);
        public event Action<string> BadgeEarned;

        // Gives XP once per key; false if this reward was already given.
        public bool Reward(string key, int xp, string label)
        {
            if (string.IsNullOrEmpty(key) || _save.rewardKeys.Contains(key))
                return false;
            int before = Level;
            _save.rewardKeys = _save.rewardKeys.Append(key).ToArray();
            _save.xp += xp;
            Notify(Level > before ? $"{label}  +{xp} XP. Level {Level}!" : $"{label}  +{xp} XP", 4f);
            Save();
            return true;
        }

        public void Badge(string id)
        {
            if (_save.badges.Contains(id))
                return;
            _save.badges = _save.badges.Append(id).ToArray();
            foreach (var b in Badges.All)
                if (b.id == id)
                    Notify($"Badge: {b.title}! {b.how}", 6f);
            Save();
            BadgeEarned?.Invoke(id);
        }

        public void SetFlag(string flag)
        {
            if (string.IsNullOrEmpty(flag) || _save.flags.Contains(flag))
                return;
            _save.flags = _save.flags.Append(flag).ToArray();
            Save();
        }

        public void Discover(PlaceRecord place)
        {
            if (place == null || IsDiscovered(place.id))
                return;
            _save.discovered = _save.discovered.Append(place.id).ToArray();
            Save();
            Reward("discovery:" + place.id, place.xp, $"Discovered: {place.name}");
            Badge("first_discovery");
        }

        public void AddPhoto(PhotoRecord photo)
        {
            _save.photos = _save.photos.Append(photo).ToArray();
            Save();
            Badge("first_photo");
        }

        // Pushes the saved comfort choices to the player and saves them.
        public void ApplyComfort()
        {
            var c = _save.comfort ??= new ComfortSave();
            // Saves made before 2026-11-22 had a weak vignette and stick walking; move them once to the comfortable
            // defaults (both can be changed back on the Settings page).
            if (c.version < 1)
            {
                c.version = 1;
                c.vignette = 2;
                c.teleport = true;
                if (c.turn == 2)
                    c.turn = 0;
            }
            Comfort.TurnMode = (Comfort.Turn)Mathf.Clamp(c.turn, 0, 2);
            Comfort.VignetteStrength = c.vignette == 0 ? 0f : c.vignette == 1 ? 0.5f : 0.85f;
            Comfort.Teleport = c.teleport;
            Comfort.LargeText = c.largeText;
            Comfort.Seated = c.seated;
            Comfort.HandSteering = !c.stickSteer;
            Save();
        }

        public void SaveNow() => Save();
        // Leaving Story Mode for the main menu: the run in progress is kept, as when the app is closed.
        public void SaveRun() => PersistRun();

        // ---------- mission results from other systems ----------

        bool _talkDone, _photoDone;

        public void OnConversationFinished(string conversationId, int quizCorrect, int quizTotal)
        {
            Reward("talk:" + conversationId, 30, "Conversation");
            bool passed = quizTotal == 0 || quizCorrect * 3 >= quizTotal * 2;
            if (quizTotal > 0)
            {
                if (quizCorrect == quizTotal)
                    Badge("road_scholar");
                if (passed)
                    Reward("quiz:" + conversationId, 20 * quizCorrect, $"Quiz {quizCorrect}/{quizTotal}");
                else
                    Notify($"Quiz {quizCorrect}/{quizTotal}: talk again to retry (you need 2 of 3).", 6f);
            }
            var m = Current;
            if (m != null && m.kind == MissionKind.Talk && m.conversationId == conversationId && passed &&
                (State == Phase.Rushing || State == Phase.Dwelling))
                _talkDone = true;
        }

        public void OnPhotoTaken(PhotoRecord photo, Camera lens)
        {
            AddPhoto(photo);
            var m = Current;
            if (m == null || m.kind != MissionKind.Photo || (State != Phase.Rushing && State != Phase.Dwelling) || lens == null)
                return;
            // The place must be near and in the middle of the frame.
            Vector3 target = m.position + Vector3.up * 4f;
            Vector3 vp = lens.WorldToViewportPoint(target);
            bool framed = vp.z > 0f && vp.x > 0.15f && vp.x < 0.85f && vp.y > 0.1f && vp.y < 0.9f;
            bool near = Flat(lens.transform.position, m.position) < 150f;
            if (framed && near)
                _photoDone = true;
            else
                Notify(near ? $"Not quite: get {m.placeName} in the middle of the frame." : $"Get closer to {m.placeName} first.", 4f);
        }

        // Parked in P inside the bay, facing either way along it.
        bool BayParked(MilestoneDefinition m)
        {
            var car = rig != null ? rig.Car : null;
            if (car == null || !car.Driven || !car.Parked)
                return false;
            float d = Flat(car.transform.position, m.position);
            float yaw = Mathf.Abs(Mathf.DeltaAngle(car.transform.eulerAngles.y, m.bayYaw));
            return d < 2.5f && (yaw < 20f || yaw > 160f);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                PersistRun();
        }

        void OnApplicationQuit() => PersistRun();

        void PersistRun()
        {
            _save.inRun = State == Phase.Rushing || State == Phase.Dwelling || State == Phase.PoliceStop;
            _save.runElapsed = Elapsed;
            _save.runPenalty = Penalty;
            Save();
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

        static readonly Unity.Profiling.ProfilerMarker ProfileMarker = new Unity.Profiling.ProfilerMarker("KR.Story");

        void Update()
        {
            using (ProfileMarker.Auto())
                Tick();
        }

        void Tick()
        {
            bool free = !GamePause.Paused && (rig == null || !rig.Busy) && (_talk == null || !_talk.WantsConfirm);
            bool confirm = free && RigInput.ConfirmPressed;
            if (free && RigInput.BeltPressed && rules != null)
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
                    {
                        State = m == null ? Phase.WeekDone : Phase.Briefing;
                        if (m != null && _save.inRun)
                        {
                            Begin();
                            Elapsed = _save.runElapsed;
                            Penalty = _save.runPenalty;
                            Notify($"Picking up where you left off: {m.title}.", 5f);
                        }
                    }
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
                    _ghosts.Tick(dt, player.position, Elapsed, State != Phase.Results);
                    Distance = Flat(player.position, m.position);
                    _persistTimer -= dt;
                    if (_persistTimer <= 0f)
                    {
                        _persistTimer = 10f;
                        PersistRun();
                    }
                    if (m.kind != MissionKind.Arrive)
                    {
                        if (m.kind == MissionKind.ParkInBay ? BayParked(m) : m.kind == MissionKind.Talk ? _talkDone : _photoDone)
                            Complete();
                        break;
                    }
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

        float _persistTimer;
        ConversationRunner _talk;

        void Start() => _talk = FindFirstObjectByType<ConversationRunner>();

        void Begin()
        {
            _talkDone = _photoDone = false;
            Elapsed = Penalty = 0f;
            _dwell = 0f;
            _dwellAnim = false;
            State = Phase.Rushing;
            if (Current.startAtPoint && motor != null)
            {
                VrRig.Fade(0.4f);   // a cut, never a glide, when the milestone moves you
                // The first milestone starts at home: that is now the spot beside your car.
                if (rig != null && (Current.startPoint - rig.Home).sqrMagnitude < 4f)
                    motor.Teleport(rig.StartSpot, rig.StartYaw);
                else
                    motor.Teleport(Current.startPoint, 0f);
            }
            Result(Current.id).attempts++;
            _ghosts.Begin(Current.id);
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
                _ghosts.SaveBest(m.id, total);
            }
            r.bestStars = Mathf.Max(r.bestStars, LastStars);
            LastCard = week.Card(m.knowledgeCardId);
            if (LastCard != null && !_save.cards.Contains(LastCard.id))
                _save.cards = _save.cards.Append(LastCard.id).ToArray();
            _save.next++;
            if (Current != null)
                _save.day = Current.day;
            _save.inRun = false;
            Save();
            State = Phase.Results;
            Reward("milestone:" + m.id, 100 + 25 * LastStars, $"{m.title} done");
            if (m.kind == MissionKind.ParkInBay)
                Badge("perfect_park");
            if (Current == null && _save.finesFrw == 0)
                Badge("clean_week");
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
                Badge("full_licence");
                Reward("licence", 200, "Full licence");
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

        // The officer's stop itself is TrafficStop; this decides what its outcome means for the run.
        void StartStop(RoadRule rule, Vector3 where)
        {
            _stop.Start(rule, where);
            State = Phase.PoliceStop;
        }

        void UpdateStop(float dt)
        {
            var rule = _stop.Rule;
            switch (_stop.Update(dt, player.position, rules != null ? rules.SpeedKmh : 0f))
            {
                case TrafficStop.Outcome.Refused:
                    var refuse = week.Rule(RuleId.RefuseStop);
                    if (refuse != null)
                    {
                        _save.finesFrw += refuse.fineFrw;
                        Fail(refuse);
                    }
                    else
                        State = Phase.Rushing;
                    break;
                case TrafficStop.Outcome.Cleared:
                    string fine = rule.fromOrder ? $"FRW {rule.fineFrw:N0}" : "fine to be set by the Minister's Order";
                    Notify($"Electronic fault notice (Law 014/2026, Art. 35): {rule.title}, {fine}. You may continue.", 6f);
                    State = Phase.Rushing;
                    break;
            }
        }

        void Fail(RoadRule rule)
        {
            StopDwellAnimation();
            _stop.Cancel();
            Notify($"Milestone failed: {rule.title}. {rule.explanation}", 8f);
            _save.inRun = false;
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
                Debug.LogWarning($"[Story] Save unreadable ({e.Message}); trying the backup.");
                _save = null;
                try
                {
                    if (File.Exists(SavePath + ".bak"))
                        _save = JsonUtility.FromJson<StorySave>(File.ReadAllText(SavePath + ".bak"));
                }
                catch (Exception)
                {
                    _save = null;
                }
                Notify(_save != null ? "Your save was damaged; the last good copy was restored." : "Your save was damaged and could not be restored.", 8f);
                _save ??= new StorySave();
            }
        }

        void Save()
        {
            try
            {
                KR.Core.AtomicFile.Write(SavePath, JsonUtility.ToJson(_save, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Story] Save failed: {e.Message}");
            }
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
