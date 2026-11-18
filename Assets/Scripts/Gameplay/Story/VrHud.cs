using KR.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace KR.Gameplay.Story
{
    // The headset's information panel outside the car. Nothing floats in view by default: the left controller's
    // menu button (☰) opens a small tablet held above your left hand (status, milestone, directions and a map)
    // and closes it again. Inside the car everything is on the dashboard screen instead, so the panel closes.
    // When something new happens while it is closed, the left controller gives a short buzz instead.
    public sealed class VrHud : MonoBehaviour
    {
        [SerializeField] StoryDirector director;
        [SerializeField] PlayerRig rig;
        [SerializeField] Camera head;
        [SerializeField] Vector3 offset = new Vector3(0f, 0.1f, 0.22f);   // from the left controller

        Canvas _canvas;
        Text _text;
        Navigator _nav;
        bool _open;
        string _lastNews, _lastPrompt;
        StoryDirector.Phase _lastPhase;

        public void Configure(StoryDirector story, PlayerRig playerRig, Camera headCamera)
        {
            director = story;
            rig = playerRig;
            head = headCamera;
        }

        void Start()
        {
            _nav = FindFirstObjectByType<Navigator>();
            var go = new GameObject("VR info panel", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(820f, 1000f);
            go.transform.localScale = Vector3.one * 0.00036f;     // about 0.30 m x 0.36 m

            var bg = Child<Image>(go.transform, "Panel");
            bg.color = new Color(0.03f, 0.04f, 0.05f, 0.9f);
            Fill(bg.rectTransform, 0f, 0f, 1f, 1f, 0f);

            _text = Child<Text>(go.transform, "Text");
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.fontSize = 30;
            _text.supportRichText = true;
            _text.color = Color.white;
            _text.alignment = TextAnchor.UpperLeft;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Truncate;
            Fill(_text.rectTransform, 0f, 0.45f, 1f, 1f, 22f);

            var mapArea = new GameObject("Map area", typeof(RectTransform)).GetComponent<RectTransform>();
            mapArea.SetParent(go.transform, false);
            Fill(mapArea, 0f, 0f, 1f, 0.45f, 16f);
            if (_nav != null)
                MapView.Create(mapArea, _nav, MapView.Mode.HeadingUp, 500f, 0, false);
            go.SetActive(false);
        }

        static T Child<T>(Transform parent, string name) where T : Component
        {
            var g = new GameObject(name, typeof(RectTransform));
            g.transform.SetParent(parent, false);
            return g.AddComponent<T>();
        }

        static void Fill(RectTransform r, float x0, float y0, float x1, float y1, float pad)
        {
            r.anchorMin = new Vector2(x0, y0);
            r.anchorMax = new Vector2(x1, y1);
            r.offsetMin = new Vector2(pad, pad);
            r.offsetMax = new Vector2(-pad, -pad);
        }

        void LateUpdate()
        {
            if (_canvas == null || rig == null)
                return;
            bool driving = rig.CurrentMode == PlayerRig.Mode.Driving;
            if (!VrRig.Active || driving)
                _open = false;
            else if (RigInput.MenuPressed)
                _open = !_open;

            var hand = rig.VrLeftHand;
            bool show = _open && hand != null && head != null && director != null;
            if (_canvas.gameObject.activeSelf != show)
                _canvas.gameObject.SetActive(show);

            BuzzOnNews(show || driving);
            if (!show)
                return;
            // Above and a little ahead of the left hand, facing your eyes.
            var pos = hand.position + Vector3.up * offset.y + Vector3.ProjectOnPlane(hand.forward, Vector3.up).normalized * offset.z;
            _canvas.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.transform.position, Vector3.up));
            _text.text = StoryInfo.Compose(director, rig, _nav, false);
        }

        // With the panel closed, buzzes stand in for messages: the left controller when there is news or the
        // milestone moves on (open the panel to read it), the right one when A can be pressed (get in the car).
        void BuzzOnNews(bool visible)
        {
            if (!VrRig.Active)
                return;
            string prompt = rig != null ? rig.Prompt : null;
            if (prompt != _lastPrompt)
            {
                _lastPrompt = prompt;
                if (prompt != null)
                    Pulse(XRNode.RightHand, 0.25f, 0.08f);
            }
            if (director != null && director.State != _lastPhase)
            {
                _lastPhase = director.State;
                if (!visible)
                    Pulse(XRNode.LeftHand, 0.4f, 0.15f);
            }
            string news = director != null && director.Toast != null && Time.time < director.ToastUntil ? director.Toast : null;
            if (rig != null && rig.Toasts.Count > 0)
                news = rig.Toasts[rig.Toasts.Count - 1].text;
            if (news == _lastNews)
                return;
            _lastNews = news;
            if (news == null || visible)
                return;
            Pulse(XRNode.LeftHand, 0.35f, 0.12f);
        }

        static void Pulse(XRNode node, float amplitude, float seconds)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (device.isValid)
                device.SendHapticImpulse(0, amplitude, seconds);
        }
    }
}
