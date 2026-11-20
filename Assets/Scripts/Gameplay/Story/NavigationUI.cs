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
        Text _carText, _radioText;
        Image _radioFill;
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
            if (rig.Car != null)
                _carScreen = CarScreen(rig.Car);
            else if (dashMount != null)
                _carScreen = CarScreen(null);
            // Live directions on foot: the minimap carries the turn-by-turn bar too.
            _mini = ScreenMap("Minimap", new Vector2(380f, 400f), new Vector2(1f, 0f), new Vector2(-20f, 60f), MapView.Mode.HeadingUp, 450f, 20, false);
            _bigScreen = ScreenMap("Full map", new Vector2(980f, 700f), new Vector2(0.5f, 0.5f), Vector2.zero, MapView.Mode.NorthUp, 0f, 24, true);
            _bigScreen.SetActive(false);
        }

        // The vehicle screen: map on the left, all the driving information on the right. It stands at the foot of the
        // windscreen straight ahead of the driver, like a head-up display (owner decision 2026-11-18: off the
        // dashboard, where it was half hidden), tilted to face the driver's eyes, see-through at the edges.
        GameObject CarScreen(DrivableCar car)
        {
            var go = new GameObject("Vehicle screen", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(1100f, 600f);
            if (car != null)
            {
                go.transform.SetParent(car.transform, false);
                Vector3 eye = car.EyePoint;
                Vector3 at = eye + car.transform.forward * 0.62f - car.transform.up * 0.16f;
                go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - eye, car.transform.up));
                go.transform.localScale = Vector3.one * (0.27f / 1100f) / Mathf.Max(0.0001f, car.transform.lossyScale.x);   // 0.27 m wide
            }
            else
            {
                go.transform.SetParent(dashMount, false);
                rt.localScale = Vector3.one * 0.00036f;
            }

            var bg = new GameObject("Bezel", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
            bg.transform.SetParent(go.transform, false);
            bg.color = new Color(0.01f, 0.01f, 0.012f, 0.72f);
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
            Fill(_carText.rectTransform, 0.46f, 0.17f, 1f, 1f, 16f);
            Radio(go.transform);
            go.SetActive(false);
            return go;
        }

        // Radio strip along the bottom of the info side: label and level, a bar, and how to change it.
        void Radio(Transform screen)
        {
            var strip = new GameObject("Radio", typeof(RectTransform)).GetComponent<RectTransform>();
            strip.SetParent(screen, false);
            Fill(strip, 0.46f, 0f, 1f, 0.17f, 12f);
            _radioText = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Text>();
            _radioText.transform.SetParent(strip, false);
            _radioText.font = KR.Gameplay.UI.KrTheme.Font(KR.Gameplay.UI.KrTheme.Weight.SemiBold);
            _radioText.fontSize = 30;
            _radioText.supportRichText = true;
            _radioText.alignment = TextAnchor.MiddleLeft;
            _radioText.horizontalOverflow = HorizontalWrapMode.Overflow;
            Fill(_radioText.rectTransform, 0f, 0f, 0.42f, 1f, 4f);
            var bar = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
            bar.transform.SetParent(strip, false);
            bar.color = new Color(1f, 1f, 1f, 0.15f);
            Fill(bar.rectTransform, 0.42f, 0.36f, 0.98f, 0.64f, 0f);
            _radioFill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
            _radioFill.transform.SetParent(bar.transform, false);
            _radioFill.type = Image.Type.Filled;
            _radioFill.fillMethod = Image.FillMethod.Horizontal;
            Fill(_radioFill.rectTransform, 0f, 0f, 1f, 1f, 0f);
        }

        void ShowRadio()
        {
            if (_radioText == null)
                return;
            var radio = CarRadio.Instance;
            float v = CarRadio.Volume;
            bool lit = radio != null && Time.unscaledTime - radio.ChangedAt < 2f;
            string hint = VrRig.Active ? "right stick up/down" : "- / + keys";
            _radioText.text = radio == null ? "<color=#888888>RADIO  —</color>"
                : $"<b>RADIO</b>  {(v <= 0.001f ? "<color=#888888>OFF</color>" : $"{Mathf.RoundToInt(v * 100f)}%")}   <size=22><color=#999999>{hint}</color></size>";
            _radioFill.fillAmount = v;
            _radioFill.color = lit ? KR.Gameplay.UI.KrTheme.Accent : KR.Gameplay.UI.KrTheme.Red;
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
                ShowRadio();
            }
        }

        static void Show(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on)
                go.SetActive(on);
        }
    }
}
