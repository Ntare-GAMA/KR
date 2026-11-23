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
    public sealed class CallOverlay : MonoBehaviour
    {
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
            KrTheme.Place(stripe.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(4f, Height - 40f));
            var scan = KrTheme.Node("Scan", card.transform).gameObject.AddComponent<Image>();
            scan.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f);
            KrTheme.Place(scan.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -14f), new Vector2(Width - 180f, 2f));

            // The caller's badge: their initial in a magenta frame.
            var badge = KrTheme.Panel(card.transform, "Badge", new Color32(30, 8, 26, 255));
            KrTheme.Place(badge.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(108f, 108f));
            _badgeFrame = KrTheme.Outline(badge.transform, Magenta);
            _initial = KrTheme.Label(badge.transform, "Initial", 64, KrTheme.Weight.Bold, Color.white, TextAnchor.MiddleCenter);
            KrTheme.Stretch(_initial.rectTransform, 0f);

            _status = KrTheme.Label(card.transform, "Status", 20, KrTheme.Weight.Bold, Cyan);
            KrTheme.Place(_status.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -22f), new Vector2(300f, 28f));
            _name = KrTheme.Label(card.transform, "Name", 40, KrTheme.Weight.Bold, Color.white);
            KrTheme.Place(_name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -50f), new Vector2(300f, 52f));
            _name.horizontalOverflow = HorizontalWrapMode.Overflow;

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
            _line.verticalOverflow = VerticalWrapMode.Truncate;

            _canvas.enabled = false;
        }

        void Update()
        {
            if (_phone == null || _card == null)
                return;
            bool want = _phone.OnCall && !VrRig.Active;
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

            string caller = _phone.Caller;
            if (!string.IsNullOrEmpty(caller))
            {
                _name.text = caller.ToUpperInvariant();
                _initial.text = caller.Substring(0, 1).ToUpperInvariant();
            }
            float t = Time.unscaledTime;
            bool ringing = _phone.Ringing;
            string line = _phone.CurrentLine;
            if (line != _lastLine)
            {
                _lastLine = line;
                _lineAt = t;
            }
            if (ringing)
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
                // The words type out quickly, like a live transcript.
                if (string.IsNullOrEmpty(line))
                    _line.text = "";
                else
                {
                    int n = Mathf.Clamp(Mathf.FloorToInt((t - _lineAt) * 60f), 0, line.Length);
                    _line.text = n < line.Length ? line.Substring(0, n) + "<color=#3ee6ff>_</color>" : line;
                }
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
    }
}
