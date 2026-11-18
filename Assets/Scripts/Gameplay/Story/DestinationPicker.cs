using System.Collections.Generic;
using System.Text;
using KR.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace KR.Gameplay.Story
{
    // The satnav's destination list. N on a desktop, or "Choose a destination" in the VR info panel, opens it;
    // left/right picks a category (Story, Food & drink, Shops, Health...), up/down a place (nearest first), Enter / X sets it and the
    // route starts at once. N again closes it. Desktop: a panel on the left of the screen. VR: a panel on your left
    // hand (only while open). In the car it opens only when stopped.
    public sealed class DestinationPicker : MonoBehaviour
    {
        [SerializeField] Navigator nav;
        [SerializeField] PlayerRig rig;

        static readonly (string group, string title)[] Groups =
        {
            ("story", "Story places"), ("food", "Food and drink"), ("shop", "Shops and markets"), ("health", "Health"),
            ("finance", "Banks"), ("lodging", "Hotels and guest houses"), ("service", "Services"), ("education", "Schools and universities"),
            ("worship", "Places of worship"), ("leisure", "Parks and sport"), ("transport", "Bus stops and stations"),
            ("office", "Offices"), ("building", "Named buildings"),
        };

        bool _open;
        int _group, _row;
        readonly List<int> _items = new List<int>();   // place indices; -1 = follow the milestone
        GameObject _screen, _hand;
        Text _screenText, _handText;
        readonly StringBuilder _sb = new StringBuilder();

        public bool Open => _open;

        public void Configure(Navigator navigator, PlayerRig playerRig)
        {
            nav = navigator;
            rig = playerRig;
        }

        void Update()
        {
            if (nav == null || rig == null)
                return;
            if (!_open)
            {
                if (RigInput.DestinationPressed && !GamePause.Paused && !rig.Busy)
                    Show();
                return;
            }
            if (RigInput.DestinationPressed || RigInput.MenuPressed)
            {
                Close();
                return;
            }
            int dx = RigInput.NavX;
            if (dx != 0)
            {
                _group = (_group + dx + Groups.Length) % Groups.Length;
                Fill();
            }
            if (_items.Count > 0)
                _row = Mathf.Clamp(_row - RigInput.NavY, 0, _items.Count - 1);
            if (RigInput.ConfirmPressed && _items.Count > 0)
            {
                nav.ChooseDestination(_items[_row]);
                Close();
                return;
            }
            Draw();
        }

        public void Show()
        {
            if (rig.CurrentMode == PlayerRig.Mode.Driving && rig.Car != null && Mathf.Abs(rig.Car.Speed) > 0.5f)
            {
                rig.Toast("Stop the car to choose a destination.", 3f);
                return;
            }
            _open = true;
            rig.Busy = true;
            if (rig.CurrentMode == PlayerRig.Mode.Walking)
                rig.Player.Frozen = true;
            Fill();
            Draw();
        }

        void Close()
        {
            _open = false;
            rig.Busy = false;
            rig.Player.Frozen = false;
            if (_screen != null) _screen.SetActive(false);
            if (_hand != null) _hand.SetActive(false);
        }

        // The places of the current category, nearest first; skips empty categories.
        void Fill()
        {
            for (int tries = 0; tries < Groups.Length; tries++)
            {
                _items.Clear();
                string g = Groups[_group].group;
                if (g == "story")
                    _items.Add(-1);
                Vector3 you = nav.PlayerPosition;
                var found = new List<(int, float)>();
                for (int i = 0; i < nav.Places.Count; i++)
                    if (nav.GroupOf(i) == g)
                        found.Add((i, Flat(nav.Places[i].pos, you)));
                found.Sort((a, b) => a.Item2.CompareTo(b.Item2));
                foreach (var (i, _) in found)
                    _items.Add(i);
                if (_items.Count > 0)
                    break;
                _group = (_group + 1) % Groups.Length;
            }
            _row = 0;
        }

        void Draw()
        {
            Text text = VrRig.Active ? HandPanel() : ScreenPanel();
            string confirm = VrRig.Active ? "X" : "Enter";
            Vector3 you = nav.PlayerPosition;
            _sb.Clear();
            _sb.Append($"<b>Where to?</b>\n<size=24>< {Groups[_group].title} ({_items.Count}) ></size>\n\n");
            int first = Mathf.Clamp(_row - 4, 0, Mathf.Max(0, _items.Count - 9));
            for (int k = first; k < Mathf.Min(_items.Count, first + 9); k++)
            {
                int i = _items[k];
                string label = i < 0 ? "Follow the milestone" : $"{nav.Places[i].name}  <color=#9aa4ad>{Km(Flat(nav.Places[i].pos, you))}</color>";
                _sb.Append(k == _row ? "<b>> " : "   ").Append(label).Append(k == _row ? "</b>\n" : "\n");
            }
            _sb.Append($"\n<size=22>{(VrRig.Active ? "Left stick" : "Arrows")}: category and place. {confirm}: go. {(VrRig.Active ? "Menu" : "N")}: close.</size>");
            text.text = _sb.ToString();
        }

        Text ScreenPanel()
        {
            if (_screen == null)
            {
                _screen = new GameObject("Destination list", typeof(RectTransform));
                _screen.transform.SetParent(transform, false);
                var canvas = _screen.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 8;
                var scaler = _screen.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 1f;
                _screenText = Panel(_screen.transform, new Vector2(620f, 560f), new Vector2(0f, 0.5f), new Vector2(30f, 0f), 28);
            }
            if (_hand != null) _hand.SetActive(false);
            _screen.SetActive(true);
            return _screenText;
        }

        Text HandPanel()
        {
            var hand = rig.VrLeftHand;
            if (_hand == null)
            {
                _hand = new GameObject("Destination list (hand)", typeof(RectTransform));
                var canvas = _hand.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                ((RectTransform)_hand.transform).sizeDelta = new Vector2(620f, 560f);
                _hand.transform.localScale = Vector3.one * 0.0004f;
                _handText = Panel(_hand.transform, new Vector2(620f, 560f), new Vector2(0.5f, 0.5f), Vector2.zero, Comfort.LargeText ? 30 : 26);
            }
            if (hand != null && _hand.transform.parent != hand)
            {
                _hand.transform.SetParent(hand, false);
                _hand.transform.localPosition = new Vector3(0f, 0.08f, 0.12f);
                _hand.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            }
            if (_screen != null) _screen.SetActive(false);
            _hand.SetActive(true);
            return _handText;
        }

        static Text Panel(Transform parent, Vector2 size, Vector2 anchor, Vector2 offset, int font)
        {
            var bg = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
            bg.transform.SetParent(parent, false);
            bg.color = new Color(0.03f, 0.04f, 0.05f, 0.9f);
            var r = bg.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = anchor;
            r.sizeDelta = size;
            r.anchoredPosition = offset;
            var text = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Text>();
            text.transform.SetParent(bg.transform, false);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = font;
            text.supportRichText = true;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            var t = text.rectTransform;
            t.anchorMin = Vector2.zero;
            t.anchorMax = Vector2.one;
            t.offsetMin = new Vector2(20f, 16f);
            t.offsetMax = new Vector2(-20f, -16f);
            return text;
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        static string Km(float m) => m < 1000f ? $"{Mathf.Round(m / 10f) * 10f:0} m" : $"{m / 1000f:0.0} km";
    }
}
