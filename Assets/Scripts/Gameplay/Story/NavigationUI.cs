using KR.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace KR.Gameplay.Story
{
    // Where information appears (owner decision 2026-11-17: nothing in your personal space):
    //   in the car     one screen in the middle of the dashboard holds everything: satnav map, directions,
    //                  speed, limit, gear, belt, lights, the milestone clock, the coach and messages.
    //                  Nothing else is shown while driving.
    //   walking in VR  nothing, until the left menu button opens the info panel on your left hand (VrHud).
    //   on a desktop   walking: a corner minimap (M opens the full map); driving: the dashboard screen only.
    public sealed class NavigationUI : MonoBehaviour
    {
        [SerializeField] Navigator nav;
        [SerializeField] PlayerRig rig;
        [SerializeField] Transform dashMount;
        [SerializeField] Material ribbonMaterial;   // kept for scenes built with the route ribbon; unused

        StoryDirector _director;
        GameObject _carScreen, _mini, _bigScreen;
        Text _carText;
        bool _bigOpen;

        public void Configure(Navigator navigator, PlayerRig playerRig, Transform dashboardMount, Material ribbon)
        {
            nav = navigator;
            rig = playerRig;
            dashMount = dashboardMount;
            ribbonMaterial = ribbon;
        }

        void Start()
        {
            if (nav == null || rig == null)
                return;
            _director = FindFirstObjectByType<StoryDirector>();
            if (dashMount != null)
                _carScreen = CarScreen(dashMount);
            _mini = ScreenMap("Minimap", new Vector2(320f, 320f), new Vector2(1f, 0f), new Vector2(-20f, 60f), MapView.Mode.HeadingUp, 450f, 0, false);
            _bigScreen = ScreenMap("Full map", new Vector2(980f, 700f), new Vector2(0.5f, 0.5f), Vector2.zero, MapView.Mode.NorthUp, 0f, 24, true);
            _bigScreen.SetActive(false);
        }

        // The vehicle screen: map on the left, all the driving information on the right.
        GameObject CarScreen(Transform mount)
        {
            var go = new GameObject("Vehicle screen", typeof(RectTransform));
            go.transform.SetParent(mount, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(1100f, 600f);
            rt.localScale = Vector3.one * 0.00036f;      // about 0.40 m x 0.22 m

            var bg = new GameObject("Bezel", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
            bg.transform.SetParent(go.transform, false);
            bg.color = new Color(0.01f, 0.01f, 0.012f, 1f);
            Fill(bg.rectTransform, 0f, 0f, 1f, 1f, 0f);

            var mapArea = new GameObject("Map", typeof(RectTransform)).GetComponent<RectTransform>();
            mapArea.SetParent(go.transform, false);
            Fill(mapArea, 0f, 0f, 0.46f, 1f, 12f);
            MapView.Create(mapArea, nav, MapView.Mode.HeadingUp, 380f, 0, false);

            _carText = new GameObject("Info", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Text>();
            _carText.transform.SetParent(go.transform, false);
            _carText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _carText.fontSize = 30;
            _carText.supportRichText = true;
            _carText.color = Color.white;
            _carText.alignment = TextAnchor.UpperLeft;
            _carText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _carText.verticalOverflow = VerticalWrapMode.Truncate;
            Fill(_carText.rectTransform, 0.46f, 0f, 1f, 1f, 16f);
            go.SetActive(false);
            return go;
        }

        static void Fill(RectTransform r, float x0, float y0, float x1, float y1, float pad)
        {
            r.anchorMin = new Vector2(x0, y0);
            r.anchorMax = new Vector2(x1, y1);
            r.offsetMin = new Vector2(pad, pad);
            r.offsetMax = new Vector2(-pad, -pad);
        }

        GameObject ScreenMap(string name, Vector2 pixels, Vector2 anchor, Vector2 offset, MapView.Mode mode, float across, int font, bool labels)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var panel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(go.transform, false);
            panel.anchorMin = panel.anchorMax = panel.pivot = anchor;
            panel.sizeDelta = pixels;
            panel.anchoredPosition = offset;
            MapView.Create(panel, nav, mode, across, font, labels);
            return go;
        }

        void Update()
        {
            if (nav == null || rig == null)
                return;
            bool vr = VrRig.Active;
            bool driving = rig.CurrentMode == PlayerRig.Mode.Driving;
            if (RigInput.MapPressed && !vr && !driving)
                _bigOpen = !_bigOpen;
            if (driving)
                _bigOpen = false;

            Show(_carScreen, driving);
            Show(_mini, !vr && !driving && !_bigOpen);
            Show(_bigScreen, !vr && !driving && _bigOpen);
            if (driving && _carText != null)
            {
                _carText.fontSize = Comfort.LargeText ? 36 : 30;
                _carText.text = StoryInfo.Compose(_director, rig, nav, true);
            }
        }

        static void Show(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on)
                go.SetActive(on);
        }
    }
}
