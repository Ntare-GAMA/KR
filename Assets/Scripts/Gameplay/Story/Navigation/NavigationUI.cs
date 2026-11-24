using KR.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace KR.Gameplay.Story
{
    // Where information appears:
    //   in the car     the instrument cluster behind the steering wheel holds everything (CarCluster): speed,
    //                  limit, gear, indicators, mileage, satnav map, directions, the milestone clock, the coach
    //                  and messages. Nothing else is shown while driving.
    //   walking in VR  nothing, until the left menu button opens the info panel on your left hand (VrHud).
    //   on a desktop   walking: a corner minimap (M opens the full map); driving: the dashboard screen only.
    public sealed class NavigationUI : MonoBehaviour
    {
        [SerializeField] Navigator nav;
        [SerializeField] PlayerRig rig;
        [SerializeField] Transform dashMount;
        [SerializeField] Material ribbonMaterial;   // kept for scenes built with the route ribbon; unused

        StoryDirector _director;
        CarCluster _cluster;
        GameObject _carScreen, _mini, _bigScreen;
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
            if (rig.Car != null || dashMount != null)
            {
                _cluster = CarCluster.Create(rig.Car, dashMount, nav);
                _carScreen = _cluster.gameObject;
                _carScreen.SetActive(false);
            }
            // Live directions on foot: the minimap carries the turn-by-turn bar too.
            _mini = ScreenMap("Minimap", new Vector2(380f, 400f), new Vector2(1f, 0f), new Vector2(-20f, 60f), MapView.Mode.HeadingUp, 450f, 20, false);
            _bigScreen = ScreenMap("Full map", new Vector2(980f, 700f), new Vector2(0.5f, 0.5f), Vector2.zero, MapView.Mode.NorthUp, 0f, 24, true);
            _bigScreen.SetActive(false);
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
            if (driving && _cluster != null)
                _cluster.Refresh(_director, rig, nav);
        }

        static void Show(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on)
                go.SetActive(on);
        }
    }
}
