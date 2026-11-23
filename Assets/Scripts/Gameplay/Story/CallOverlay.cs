using KR.Gameplay.Player;
using KR.Gameplay.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KR.Gameplay.Story
{
    // A phone call on a desktop: a small neon call card on the left of the screen instead of the whole phone (owner
    // 2026-11-23: "more of the cyber punk call feature, small and futuristic not the whole view of a phone").
    // It slides in with a short flicker; while ringing the caller's badge pulses, once connected a live timer runs and
    // a voice meter moves while they speak, with their words under the name. Slides out when the call ends.
    // In the headset calls stay as messages in view (the card is a screen overlay).
    // The same card carries what the police say to you face to face (Talk), in police blue (owner 2026-11-23: "the
    // police comments and speech with the player should ... replicate the call view of the phone"). The card grows
    // with the words, so a long line always stays inside it.
    public sealed class CallOverlay : MonoBehaviour
    {
        static CallOverlay _instance;
        static readonly Color Police = new Color32(70, 140, 255, 255);
        string _talkWho, _talkStatus, _talkLine;
        float _talkUntil, _talkStarted;
        Image _stripe, _badgeFill;
        float _fitHeight = 74f;
        string _fitted;

        // Someone speaking to you in person (the police): shown on the card for `seconds`, unless a call is on.
        public static void Talk(string who, string status, string line, float seconds)
        {
            if (_instance == null)
                return;
            if (_instance._talkUntil < Time.unscaledTime)
                _instance._talkStarted = Time.unscaledTime;
            _instance._talkWho = who;
            _instance._talkStatus = status;
            _instance._talkLine = line;
            _instance._talkUntil = Time.unscaledTime + seconds;
        }

        // True when the card can show (a desktop, not the headset).
        public static bool Available => _instance != null && !VrRig.Active;
        const float Width = 600f, Height = 196f, Margin = 36f;
        const int Bars = 16;
        static readonly Color Cyan = new Color32(62, 230, 255, 255);
        static readonly Color Magenta = new Color32(255, 46, 136, 255);
        static readonly Color Fill = new Color32(6, 9, 17, 228);

        Phone _phone;
        Canvas _canvas;
        RectTransform _card;
        CanvasGroup _group;
        Image _frame, _badgeFrame;
        Text _initial, _status, _name, _line;
        RectTransform[] _bars;
        float _shown;   // 0 hidden .. 1 in place
        float _appearedAt;
        string _lastLine;
        float _lineAt;

        void Start()
        {
            _instance = this;
            _phone = GetComponent<Phone>();
            Build();
        }

        void Build()
        {
            var go = new GameObject("Call card", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 56;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var card = KrTheme.Panel(go.transform, "Card", Fill);
            _card = card.rectTransform;
            KrTheme.Place(_card, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(Margin, 90f), new Vector2(Width, Height));
            _group = card.gameObject.AddComponent<CanvasGroup>();
            _frame = KrTheme.Outline(card.transform, Cyan);

            // A thin accent stripe down the left edge and a scan line across the top.
            var stripe = KrTheme.Node("Stripe", card.transform).gameObject.AddComponent<Image>();
            stripe.color = Magenta;
            _stripe = stripe;
            var sr = stripe.rectTransform;
            sr.anchorMin = new Vector2(0f, 0f);
            sr.anchorMax = new Vector2(0f, 1f);
            sr.pivot = new Vector2(0f, 0.5f);
            sr.offsetMin = new Vector2(6f, 20f);
            sr.offsetMax = new Vector2(10f, -20f);
            var scan = KrTheme.Node("Scan", card.transform).gameObject.AddComponent<Image>();
            scan.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f);
            KrTheme.Place(scan.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -14f), new Vector2(Width - 180f, 2f));

            // The caller's badge: their initial in a magenta frame.
            var badge = KrTheme.Panel(card.transform, "Badge", new Color32(30, 8, 26, 255));
            _badgeFill = badge;
            KrTheme.Place(badge.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(108f, 108f));
            _badgeFrame = KrTheme.Outline(badge.transform, Magenta);
            _initial = KrTheme.Label(badge.transform, "Initial", 64, KrTheme.Weight.Bold, Color.white, TextAnchor.MiddleCenter);
            KrTheme.Stretch(_initial.rectTransform, 0f);

            _status = KrTheme.Label(card.transform, "Status", 20, KrTheme.Weight.Bold, Cyan);
            KrTheme.Place(_status.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -22f), new Vector2(300f, 28f));
            _name = KrTheme.Label(card.transform, "Name", 40, KrTheme.Weight.Bold, Color.white);
            KrTheme.Place(_name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -50f), new Vector2(Width - 150f - 24f - Bars * 9f - 12f, 52f));
            _name.horizontalOverflow = HorizontalWrapMode.Wrap;
            _name.verticalOverflow = VerticalWrapMode.Truncate;
            _name.resizeTextForBestFit = true;
            _name.resizeTextMinSize = 22;
            _name.resizeTextMaxSize = 40;

            // Voice meter, top right.
            var meter = KrTheme.Node("Meter", card.transform);
            KrTheme.Place(meter, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -30f), new Vector2(Bars * 9f, 60f));
            _bars = new RectTransform[Bars];
            for (int i = 0; i < Bars; i++)
            {
                var bar = KrTheme.Node("Bar", meter).gameObject.AddComponent<Image>();
                bar.color = i % 4 == 0 ? Magenta : Cyan;
                var r = bar.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
                r.pivot = new Vector2(0f, 0.5f);
                r.anchoredPosition = new Vector2(i * 9f, 0f);
                r.sizeDelta = new Vector2(5f, 6f);
                _bars[i] = r;
            }

            // What they are saying.
            _line = KrTheme.Label(card.transform, "Line", 22, KrTheme.Weight.SemiBold, new Color32(225, 236, 245, 255));
            KrTheme.Place(_line.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(150f, 16f), new Vector2(Width - 172f, 74f));
            _line.horizontalOverflow = HorizontalWrapMode.Wrap;
            _line.verticalOverflow = VerticalWrapMode.Overflow;

            _canvas.enabled = false;
        }

        void Update()
        {
            if (_card == null)
                return;
            bool call = _phone != null && _phone.OnCall;
            bool talk = !call && Time.unscaledTime < _talkUntil;
            bool want = (call || talk) && !VrRig.Active;
            if (want && _shown <= 0f)
                _appearedAt = Time.unscaledTime;
            _shown = Mathf.MoveTowards(_shown, want ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            _canvas.enabled = _shown > 0f;
            if (!_canvas.enabled)
                return;

            // Slide in from the left with an ease, and a brief flicker as it appears.
            float k = 1f - Mathf.Pow(1f - _shown, 3f);
            _card.anchoredPosition = new Vector2(Mathf.Lerp(-Width - 20f, Margin, k), 90f);
            float age = Time.unscaledTime - _appearedAt;
            float flicker = age < 0.3f ? (Mathf.Repeat(age * 37f, 1f) < 0.5f ? 0.35f : 1f) : 1f;
            _group.alpha = _shown * flicker;

            string caller = call ? _phone.Caller : _talkWho;
            if (!string.IsNullOrEmpty(caller))
            {
                _name.text = caller.ToUpperInvariant();
                _initial.text = caller.Substring(0, 1).ToUpperInvariant();
            }
            float t = Time.unscaledTime;
            bool ringing = call && _phone.Ringing;
            string line = call ? _phone.CurrentLine : _talkLine;
            if (line != _lastLine)
            {
                _lastLine = line;
                _lineAt = t;
            }
            Fit(line);
            var accent = call ? Magenta : Police;
            _stripe.color = accent;
            _badgeFill.color = call ? new Color32(30, 8, 26, 255) : new Color32(8, 16, 40, 255);
            if (!call)
            {
                int secs = Mathf.Max(0, Mathf.FloorToInt(t - _talkStarted));
                _status.text = $"{_talkStatus}  {secs / 60:00}:{secs % 60:00}";
                _badgeFrame.color = accent;
                _frame.color = Color.Lerp(Cyan, accent, 0.5f);
                TypeOut(line, t);
            }
            else if (ringing)
            {
                _status.text = Mathf.Repeat(t, 0.8f) < 0.5f ? "/// INCOMING CALL" : "/// INCOMING CALL  ·";
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 9f);
                _badgeFrame.color = Color.Lerp(new Color(Magenta.r, Magenta.g, Magenta.b, 0.3f), Magenta, pulse);
                _frame.color = Color.Lerp(Cyan, Magenta, pulse * 0.5f);
                _line.text = "<color=#7f8ea3>Connecting hands-free...</color>";
            }
            else
            {
                int secs = Mathf.Max(0, Mathf.FloorToInt(Time.time - _phone.CallStarted));
                _status.text = $"• LIVE  {secs / 60:00}:{secs % 60:00}";
                _badgeFrame.color = Magenta;
                _frame.color = Cyan;
                TypeOut(line, t);
            }

            // Voice meter: lively while a line is being said, a low idle shimmer otherwise.
            bool speaking = !ringing && !string.IsNullOrEmpty(line) && t - _lineAt < 4.2f;
            for (int i = 0; i < Bars; i++)
            {
                float h;
                if (speaking)
                    h = 8f + 48f * Mathf.Abs(Mathf.Sin(t * (7f + i * 0.9f) + i * 1.7f) * Mathf.Sin(t * 3.1f + i));
                else if (ringing)
                    h = 6f + 26f * Mathf.Max(0f, Mathf.Sin(t * 9f - i * 0.5f));
                else
                    h = 4f + 3f * Mathf.Abs(Mathf.Sin(t * 2f + i));
                _bars[i].sizeDelta = new Vector2(5f, h);
            }
        }

        // The words type out quickly, like a live transcript.
        void TypeOut(string line, float t)
        {
            if (string.IsNullOrEmpty(line))
            {
                _line.text = "";
                return;
            }
            int n = Mathf.Clamp(Mathf.FloorToInt((t - _lineAt) * 60f), 0, line.Length);
            _line.text = n < line.Length ? line.Substring(0, n) + "<color=#3ee6ff>_</color>" : line;
        }

        // Card height from the whole line (measured once per line, before it types out), so it never spills over.
        void Fit(string line)
        {
            if (line == _fitted)
                return;
            _fitted = line;
            _line.text = string.IsNullOrEmpty(line) ? "" : line;
            Canvas.ForceUpdateCanvases();
            _fitHeight = Mathf.Clamp(_line.preferredHeight + 8f, 74f, 320f);
            var lr = _line.rectTransform;
            lr.sizeDelta = new Vector2(lr.sizeDelta.x, _fitHeight);
            _card.sizeDelta = new Vector2(Width, Height - 74f + _fitHeight);
        }
    }
}
