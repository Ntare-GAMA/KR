using System.Collections.Generic;
using System.Text;
using KR.Gameplay.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KR.Gameplay.Story
{
    // The player's phone (owner, 2026-11-22: "the player should have a phone too, which acts as a reminder and in
    // case of calls"). Reminders: the current milestone, where to go and the time left, plus anything the story
    // adds (Remind). Calls: friends ring at set times of the game day; the phone rings and buzzes and the call
    // plays hands-free (no button needed, so it never takes a button another action uses). P opens the phone on a
    // desktop; on the headset it is the Phone page of the info panel. Added with the player when the scene loads.
    public sealed class Phone : MonoBehaviour
    {
        sealed class Call
        {
            public float hour;
            public string from;
            public string[] lines;
            public bool done;
        }

        public static Phone Instance { get; private set; }

        // Calls in game time (fictional, everyday, non-political).
        readonly List<Call> _calls = new List<Call>
        {
            new Call { hour = 8.25f, from = "Aline", lines = new[] { "Morning, Shiloh! Don't forget we have class at the ALU campus today.", "Drive carefully, the roads are busy at this hour." } },
            new Call { hour = 12.5f, from = "Maman Fils", lines = new[] { "Shiloh, it's Maman Fils. Come by for tea when you finish.", "And bring your student card, the library asked about it." } },
            new Call { hour = 16.75f, from = "Jean-Paul", lines = new[] { "Hey! Some of us are meeting at the pitch later.", "No rush. Finish your errands first." } },
        };
        readonly List<string> _reminders = new List<string>();
        readonly List<string> _log = new List<string>();
        readonly StringBuilder _sb = new StringBuilder();

        PlayerRig _rig;
        StoryDirector _director;
        Navigator _nav;
        Canvas _canvas;
        Text _screen;
        AudioSource _ring;
        bool _open;
        Call _active;
        int _line;
        float _ringUntil, _nextLine;

        public bool Ringing => _active != null && Time.time < _ringUntil;
        public bool OnCall => _active != null;

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
            if (rig == null || rig.GetComponent<Phone>() != null)
                return;
            rig.gameObject.AddComponent<Phone>();
            rig.gameObject.AddComponent<Wallet>();
        }

        // Something to remember (shown on the phone until the end of the session).
        public void Remind(string text)
        {
            if (!_reminders.Contains(text))
                _reminders.Add(text);
            _rig?.Toast("Phone reminder: " + text, 4f);
        }

        void Awake() => Instance = this;

        void Start()
        {
            _rig = GetComponent<PlayerRig>();
            _director = FindFirstObjectByType<StoryDirector>();
            _nav = FindFirstObjectByType<Navigator>();
            float now = KigaliTime.HourOfDay;
            foreach (var c in _calls)
                c.done = c.hour < now;   // calls already past when the session starts are not replayed
            BuildScreen();
            _ring = gameObject.AddComponent<AudioSource>();
            _ring.clip = RingTone();
            _ring.loop = true;
            _ring.playOnAwake = false;
            _ring.spatialBlend = 0f;
            _ring.volume = 0.5f;
        }

        void BuildScreen()
        {
            var go = new GameObject("Phone", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 55;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var body = UI.KrTheme.Panel(go.transform, "Body", new Color32(16, 16, 20, 245));
            UI.KrTheme.Place(body.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40f, 40f), new Vector2(380f, 700f));
            var glass = UI.KrTheme.Panel(body.transform, "Screen", new Color32(24, 34, 52, 255));
            UI.KrTheme.Stretch(glass.rectTransform, 14f);
            _screen = UI.KrTheme.Label(glass.transform, "Text", 24, UI.KrTheme.Weight.SemiBold, Color.white, TextAnchor.UpperLeft);
            UI.KrTheme.Stretch(_screen.rectTransform, 16f);
            _screen.horizontalOverflow = HorizontalWrapMode.Wrap;
            _screen.verticalOverflow = VerticalWrapMode.Truncate;
            go.SetActive(false);
        }

        // Two short beeps every two seconds, made here (no recorded ringtone).
        static AudioClip RingTone()
        {
            const int rate = 22050;
            var data = new float[rate * 2];
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                bool on = t < 0.35f || (t > 0.5f && t < 0.85f);
                data[i] = on ? Mathf.Sin(2f * Mathf.PI * (t < 0.4f ? 880f : 1046f) * t) * 0.4f : 0f;
            }
            var clip = AudioClip.Create("Phone ring", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.pKey.wasPressedThisFrame && !VrRig.Active)
                _open = !_open;
            UpdateCalls();
            bool show = (_open || _active != null) && !VrRig.Active;
            if (_canvas.gameObject.activeSelf != show)
                _canvas.gameObject.SetActive(show);
            if (show)
                _screen.text = ScreenText();
        }

        void UpdateCalls()
        {
            if (_active == null)
            {
                float now = KigaliTime.HourOfDay;
                foreach (var c in _calls)
                    if (!c.done && now >= c.hour && _rig != null && !_rig.Busy)
                    {
                        c.done = true;
                        _active = c;
                        _line = -1;
                        _ringUntil = Time.time + 3.5f;
                        _ring.Play();
                        Buzz();
                        _rig.Toast($"Your phone is ringing: {c.from}.", 3f);
                        break;
                    }
                return;
            }
            if (Time.time < _ringUntil)
                return;
            if (_ring.isPlaying)
                _ring.Stop();
            // Hands-free: one line every few seconds, shown on the phone and as a message.
            if (Time.time < _nextLine)
                return;
            _line++;
            if (_line >= _active.lines.Length)
            {
                _log.Insert(0, $"{KigaliTime.Clock}  {_active.from}");
                _active = null;
                return;
            }
            _rig?.Toast($"{_active.from} (phone): {_active.lines[_line]}", 4.5f);
            _nextLine = Time.time + 4.8f;
        }

        // What the phone shows: the time, a call in progress, reminders and recent calls.
        public string ScreenText()
        {
            _sb.Clear();
            _sb.Append("<size=44><b>").Append(KigaliTime.Clock).Append("</b></size>   <size=20>").Append(Wallet.PlayerName).Append("</size>\n\n");
            if (_active != null)
            {
                _sb.Append(Ringing ? "<color=#7CFC9A><b>Incoming call</b></color>\n" : "<color=#7CFC9A><b>On a call</b></color>\n")
                   .Append("<size=34>").Append(_active.from).Append("</size>\n");
                if (_line >= 0 && _line < _active.lines.Length)
                    _sb.Append("<i>").Append(_active.lines[_line]).Append("</i>\n");
                _sb.Append('\n');
            }
            _sb.Append("<b>REMINDERS</b>\n");
            var m = _director != null ? _director.Current : null;
            if (m != null)
            {
                _sb.Append("• ").Append(m.title);
                if (_director.State == StoryDirector.Phase.Rushing || _director.State == StoryDirector.Phase.Dwelling)
                    _sb.Append("  (").Append(StoryDirector.Mmss(_director.Remaining)).Append(" left)");
                _sb.Append('\n');
            }
            if (_nav != null && !string.IsNullOrEmpty(_nav.DestinationName))
                _sb.Append("• Go to ").Append(_nav.DestinationName).Append(_nav.Remaining > 0f ? $" ({_nav.Remaining / 1000f:0.0} km)" : "").Append('\n');
            foreach (var r in _reminders)
                _sb.Append("• ").Append(r).Append('\n');
            foreach (var c in _calls)
                if (!c.done)
                {
                    _sb.Append("<color=#9aa4b5>• ").Append(c.from).Append(" may call at ").Append(Hhmm(c.hour)).Append("</color>\n");
                    break;
                }
            _sb.Append("\n<b>RECENT CALLS</b>\n");
            if (_log.Count == 0)
                _sb.Append("<color=#9aa4b5>None yet</color>\n");
            for (int i = 0; i < _log.Count && i < 4; i++)
                _sb.Append(_log[i]).Append('\n');
            _sb.Append(VrRig.Active ? "" : "\n<size=18><color=#9aa4b5>P: close   I: your cards</color></size>");
            return _sb.ToString();
        }

        static string Hhmm(float hour) => $"{Mathf.FloorToInt(hour):00}:{Mathf.FloorToInt(hour % 1f * 60f):00}";

        static void Buzz()
        {
            var d = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            if (d.isValid)
                d.SendHapticImpulse(0, 0.6f, 0.4f);
        }
    }
}
