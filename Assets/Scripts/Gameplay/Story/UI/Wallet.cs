using System.Collections.Generic;
using KR.Gameplay.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KR.Gameplay.Story
{
    // The player's cards. Game props only: no real institution logo, no real national-ID or licence design or number.
    // Shown with I on a desktop (the Wallet page of the info panel on the headset), and handed over when staff or an
    // officer ask for them (Show). Tickets the police write are kept here too (Ticket).
    public sealed class Wallet : MonoBehaviour
    {
        public const string PlayerName = "NTARE Shiloh";

        public enum Card { Student, Identity, Licence }

        public static Wallet Instance { get; private set; }

        // True while the cards or a ticket are on screen (the briefing panel waits so they never overlap).

        public static bool Showing => Instance != null && Instance._canvas != null && Instance._canvas.gameObject.activeSelf;

        Canvas _canvas;
        readonly Dictionary<Card, RectTransform> _cards = new Dictionary<Card, RectTransform>();
        RectTransform _ticket;
        Text _ticketText, _licenceRole;
        float _showUntil;
        bool _open;
        readonly List<string> _tickets = new List<string>();

        public IReadOnlyList<string> Tickets => _tickets;

        static bool Learner
        {
            get
            {
                var d = FindFirstObjectByType<StoryDirector>();
                return d == null || d.Learner;
            }
        }

        public static string Describe(Card card)
        {
            switch (card)
            {
                case Card.Student: return $"Student card: {PlayerName}, student, African Leadership University (Kigali campus). Valid for this academic year.";
                case Card.Licence: return $"Driving licence: {PlayerName}, category B (cars), {(Learner ? "learner" : "full")}. (A game card, not a real document.)";
                default: return $"Identity card: {PlayerName}. Born in Kigali. (A game card, not a real document.)";
            }
        }

        void Awake() => Instance = this;

        void Start()
        {
            var go = new GameObject("Wallet cards", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 60;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            _cards[Card.Student] = MakeCard(go.transform, new Color32(150, 24, 40, 255), "STUDENT CARD",
                "African Leadership University\nKigali campus", "STUDENT", new Color32(255, 200, 87, 255), out _);
            _cards[Card.Identity] = MakeCard(go.transform, new Color32(18, 90, 140, 255), "IDENTITY CARD",
                "Kigali, Rwanda (game card)", "RESIDENT", new Color32(120, 220, 255, 255), out _);
            _cards[Card.Licence] = MakeCard(go.transform, new Color32(30, 120, 60, 255), "DRIVING LICENCE",
                "Category B: cars (game card)", "LEARNER", new Color32(190, 240, 120, 255), out _licenceRole);
            _ticket = MakeTicket(go.transform, out _ticketText);
            go.SetActive(false);
        }

        static RectTransform MakeCard(Transform parent, Color band, string title, string issuer, string role, Color accent, out Text roleText)
        {
            var card = UI.KrTheme.Panel(parent, title, new Color32(245, 242, 236, 255));
            var r = card.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(460f, 290f);
            var top = UI.KrTheme.Panel(card.transform, "Band", band);
            Band(top.rectTransform, 1f, 70f);
            var head = UI.KrTheme.Label(top.transform, "Title", 30, UI.KrTheme.Weight.Bold, Color.white, TextAnchor.MiddleLeft);
            UI.KrTheme.Stretch(head.rectTransform, 0f);
            head.rectTransform.offsetMin = new Vector2(18f, 0f);
            head.text = title;
            // The holder's photo (Resources/Story/Portraits/player_photo), or a plain square with an initial.
            var photo = UI.KrTheme.Panel(card.transform, "Photo", new Color32(200, 196, 188, 255));
            UI.KrTheme.Place(photo.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 22f), new Vector2(130f, 170f));
            var portrait = Resources.Load<Texture2D>("Story/Portraits/player_photo");
            if (portrait != null)
            {
                var img = UI.KrTheme.Node("Portrait", photo.transform).gameObject.AddComponent<RawImage>();
                img.texture = portrait;
                img.raycastTarget = false;
                UI.KrTheme.Stretch(img.rectTransform, 3f);
            }
            else
            {
                var silhouette = UI.KrTheme.Label(photo.transform, "Silhouette", 96, UI.KrTheme.Weight.Bold, new Color32(150, 146, 140, 255), TextAnchor.MiddleCenter);
                UI.KrTheme.Stretch(silhouette.rectTransform, 0f);
                silhouette.text = "N";
            }
            var body = UI.KrTheme.Label(card.transform, "Details", 24, UI.KrTheme.Weight.SemiBold, new Color32(30, 30, 34, 255), TextAnchor.UpperLeft);
            UI.KrTheme.Place(body.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(166f, 22f), new Vector2(280f, 180f));
            body.text = $"<size=20>NAME</size>\n<b><size=30>{PlayerName}</size></b>\n<size=20>{issuer}</size>\n<color=#{ColorUtility.ToHtmlStringRGB(band)}><b>{role}</b></color>";
            roleText = body;
            var stripe = UI.KrTheme.Panel(card.transform, "Stripe", accent);
            Band(stripe.rectTransform, 0f, 10f);
            return r;
        }

        // The officer's ticket: a white slip with a blue header.
        static RectTransform MakeTicket(Transform parent, out Text text)
        {
            var slip = UI.KrTheme.Panel(parent, "Ticket", new Color32(252, 252, 248, 255));
            var r = slip.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(520f, 420f);
            r.anchoredPosition = new Vector2(0f, 40f);
            var top = UI.KrTheme.Panel(slip.transform, "Header", new Color32(24, 52, 110, 255));
            Band(top.rectTransform, 1f, 64f);
            var head = UI.KrTheme.Label(top.transform, "Title", 28, UI.KrTheme.Weight.Bold, Color.white, TextAnchor.MiddleLeft);
            UI.KrTheme.Stretch(head.rectTransform, 0f);
            head.rectTransform.offsetMin = new Vector2(18f, 0f);
            head.text = "TRAFFIC FAULT NOTICE";
            text = UI.KrTheme.Label(slip.transform, "Details", 23, UI.KrTheme.Weight.SemiBold, new Color32(30, 30, 34, 255), TextAnchor.UpperLeft);
            UI.KrTheme.Stretch(text.rectTransform, 0f);
            text.rectTransform.offsetMin = new Vector2(22f, 18f);
            text.rectTransform.offsetMax = new Vector2(-22f, -78f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return r;
        }

        // A full-width strip along the top (edge 1) or bottom (edge 0) of the card, height h.
        static void Band(RectTransform r, float edge, float h)
        {
            r.anchorMin = new Vector2(0f, edge);
            r.anchorMax = new Vector2(1f, edge);
            r.pivot = new Vector2(0.5f, edge);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(0f, h);
        }

        // Holds cards up for a few seconds (staff or an officer asked for them), side by side.
        public void Show(float seconds, params Card[] cards)
        {
            Refresh();
            foreach (var c in _cards)
            {
                c.Value.localScale = Vector3.one;
                c.Value.gameObject.SetActive(System.Array.IndexOf(cards, c.Key) >= 0);
            }
            _ticket.gameObject.SetActive(false);
            Layout(cards);
            _showUntil = Time.unscaledTime + seconds;
            Present();
            var names = new List<string>();
            foreach (var c in cards)
                names.Add(c == Card.Student ? "student card" : c == Card.Licence ? "driving licence" : "identity card");
            FindFirstObjectByType<PlayerRig>()?.Toast("You hand over your " + string.Join(" and ", names) + ".", 3f);
        }

        public void Show(Card card, float seconds = 4f) => Show(seconds, card);

        // A ticket written to you: shown on screen, kept in the wallet, and listed on the headset's Wallet page.
        public void Ticket(string offence, string where, string fine)
        {
            string text = $"<size=20>DRIVER</size>\n<b>{PlayerName}</b>   <size=20>licence category B ({(Learner ? "learner" : "full")})</size>\n\n" +
                          $"<size=20>OFFENCE</size>\n<b>{offence}</b>\n\n<size=20>WHERE / WHEN</size>\n{where}, {KigaliTime.Clock}\n\n" +
                          $"<size=20>FINE</size>\n{fine}\n\n<size=18><color=#666666>Game notice, not a real document.</color></size>";
            _tickets.Add($"{KigaliTime.Clock}: {offence} ({fine})");
            _ticketText.text = text;
            foreach (var c in _cards)
                c.Value.gameObject.SetActive(false);
            _ticket.gameObject.SetActive(true);
            _showUntil = Time.unscaledTime + 7f;
            Present();
        }

        // On a desktop: over the screen. On the headset: a board 60 cm in front of you, a little below your eyes,
        // facing you, placed once where you are looking (it stays put in the world rather than moving with your
        // head, which is easier on the stomach).
        void Present()
        {
            var rt = (RectTransform)_canvas.transform;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            if (!VrRig.Active)
            {
                if (_canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    if (scaler != null) scaler.enabled = true;
                }
                _canvas.gameObject.SetActive(true);
                return;
            }
            if (_canvas.renderMode != RenderMode.WorldSpace)
            {
                if (scaler != null) scaler.enabled = false;
                _canvas.renderMode = RenderMode.WorldSpace;
                rt.sizeDelta = new Vector2(1600f, 700f);
                rt.localScale = Vector3.one * 0.00042f;   // a 460-unit card is about 19 cm wide
            }
            var cam = Camera.main;
            if (cam != null)
            {
                var fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                if (fwd.sqrMagnitude < 0.01f)
                    fwd = cam.transform.forward;
                var pos = cam.transform.position + fwd * 0.6f + Vector3.down * 0.15f;
                rt.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - cam.transform.position));
            }
            _canvas.gameObject.SetActive(true);
        }

        // The headset's Wallet page: all three cards (and the last ticket) in front of you while the page is open.
        public void ShowAll(bool on)
        {
            if (!on)
            {
                if (_open)
                {
                    _open = false;
                    _canvas.gameObject.SetActive(false);
                }
                return;
            }
            if (_open)
                return;
            _open = true;
            Refresh();
            var all = new[] { Card.Student, Card.Identity, Card.Licence };
            foreach (var c in _cards)
                c.Value.gameObject.SetActive(true);
            _ticket.gameObject.SetActive(false);
            Layout(all);
            foreach (var c in all)
                _cards[c].localScale = Vector3.one * 0.72f;
            Present();
        }

        void Refresh()
        {
            if (_licenceRole != null)
                _licenceRole.text = _licenceRole.text.Replace(Learner ? "FULL" : "LEARNER", Learner ? "LEARNER" : "FULL");
        }

        void Layout(Card[] shown)
        {
            float x = -(shown.Length - 1) * 250f;
            foreach (var c in shown)
            {
                _cards[c].anchoredPosition = new Vector2(x, 0f);
                x += 500f;
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.iKey.wasPressedThisFrame && !VrRig.Active)
            {
                _open = !_open;
                _showUntil = 0f;
                Refresh();
                var all = new[] { Card.Student, Card.Identity, Card.Licence };
                foreach (var c in _cards)
                    c.Value.gameObject.SetActive(true);
                _ticket.gameObject.SetActive(false);
                Layout(all);
                foreach (var c in all)
                    _cards[c].localScale = Vector3.one * 0.72f;   // three across
                if (_open)
                    Present();
                else
                    _canvas.gameObject.SetActive(false);
            }
            if (!_open && _showUntil > 0f && Time.unscaledTime > _showUntil)
            {
                _showUntil = 0f;
                _canvas.gameObject.SetActive(false);
                foreach (var c in _cards)
                    c.Value.localScale = Vector3.one;
            }
        }
    }
}
