using KR.Gameplay.Player;
using KR.Gameplay.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KR.Gameplay.Story
{
    // The car's instrument cluster. A digital cluster over the PolyDrive car's own gauge housing, seen through
    // the top of the steering wheel:
    //   left     speed (large), limit, gear P R D, indicator arrows, belt and lights, odometer and trip;
    //   middle   the satnav map, heading up;
    //   right    the clock or the milestone countdown, directions, coach and messages; the radio level.
    public sealed class CarCluster : MonoBehaviour
    {
        const float PixelsWide = 1000f, PixelsHigh = 450f;

        DrivableCar _car;
        CarIndicators _indicators;
        CarOdometer _odometer;
        Text _speed, _unit, _gear, _status, _odo, _info, _radioText, _left, _right;
        Image _radioFill;

        public static CarCluster Create(DrivableCar car, Transform fallbackMount, Navigator nav)
        {
            var go = new GameObject("Instrument cluster", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(PixelsWide, PixelsHigh);
            if (car != null)
            {
                var t = car.transform;
                go.transform.SetParent(t, false);
                Vector3 eye = car.EyePoint;
                var box = CarGeometry.Cluster;
                Vector3 at;
                float width;
                if (box != null)
                {
                    // In front of the gauge faces (the needles stand about 2.5 cm proud of them), level with the dials.
                    at = t.TransformPoint(new Vector3(box.centre.x, box.centre.y - box.size.y * 0.08f, box.centre.z));
                    at += (eye - at).normalized * 0.045f;
                    width = Mathf.Clamp(box.size.x * 1.2f, 0.24f, 0.32f);
                }
                else
                {
                    // Not measured: at the foot of the windscreen, straight ahead.
                    at = eye + t.forward * 0.62f - t.up * 0.16f;
                    width = 0.27f;
                }
                go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - eye, t.up));
                go.transform.localScale = Vector3.one * (width / PixelsWide) / Mathf.Max(0.0001f, t.lossyScale.x);
            }
            else if (fallbackMount != null)
            {
                go.transform.SetParent(fallbackMount, false);
                rt.localScale = Vector3.one * 0.0003f;
            }
            var cluster = go.AddComponent<CarCluster>();
            cluster.Build(car, nav);
            return cluster;
        }

        void Build(DrivableCar car, Navigator nav)
        {
            _car = car;
            var root = (RectTransform)transform;
            var bg = Img(root, "Screen", new Color(0.015f, 0.016f, 0.02f, 0.96f));
            Fill(bg.rectTransform, 0f, 0f, 1f, 1f, 0f);
            var edge = Img(root, "Accent", KrTheme.Red);
            Fill(edge.rectTransform, 0f, 0.985f, 1f, 1f, 0f);

            var digits = KrTheme.Font(KrTheme.Weight.Bold);
            var semi = KrTheme.Font(KrTheme.Weight.SemiBold);
            var plain = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Left: speed and the car's state. The steering wheel's rim crosses the top corners, so nothing sits there:
            // the indicator arrows flank the speed.
            _left = Txt(root, "Left arrow", plain, 50, TextAnchor.MiddleCenter);
            Fill(_left.rectTransform, 0f, 0.5f, 0.06f, 0.8f, 0f);
            _right = Txt(root, "Right arrow", plain, 50, TextAnchor.MiddleCenter);
            Fill(_right.rectTransform, 0.24f, 0.5f, 0.30f, 0.8f, 0f);
            _speed = Txt(root, "Speed", digits, 140, TextAnchor.MiddleCenter);
            Fill(_speed.rectTransform, 0.05f, 0.47f, 0.25f, 0.84f, 0f);
            _unit = Txt(root, "Unit and limit", semi, 28, TextAnchor.UpperCenter);
            Fill(_unit.rectTransform, 0f, 0.33f, 0.30f, 0.48f, 0f);
            _gear = Txt(root, "Gear", digits, 44, TextAnchor.MiddleCenter);
            Fill(_gear.rectTransform, 0f, 0.19f, 0.20f, 0.33f, 0f);
            _status = Txt(root, "Status", semi, 22, TextAnchor.MiddleCenter);
            Fill(_status.rectTransform, 0.17f, 0.19f, 0.31f, 0.33f, 0f);
            _odo = Txt(root, "Odometer", semi, 24, TextAnchor.MiddleCenter);
            Fill(_odo.rectTransform, 0f, 0.04f, 0.30f, 0.18f, 0f);

            // Middle: the map.
            var mapArea = new GameObject("Map", typeof(RectTransform)).GetComponent<RectTransform>();
            mapArea.SetParent(root, false);
            Fill(mapArea, 0.30f, 0f, 0.64f, 0.985f, 8f);
            if (nav != null)
                MapView.Create(mapArea, nav, MapView.Mode.HeadingUp, 320f, 0, false);

            // Right: the story and directions, and the radio.
            _info = Txt(root, "Info", plain, 29, TextAnchor.UpperLeft);
            _info.horizontalOverflow = HorizontalWrapMode.Wrap;
            _info.verticalOverflow = VerticalWrapMode.Truncate;
            Fill(_info.rectTransform, 0.64f, 0.17f, 0.97f, 0.84f, 10f);
            var strip = new GameObject("Radio", typeof(RectTransform)).GetComponent<RectTransform>();
            strip.SetParent(root, false);
            Fill(strip, 0.64f, 0f, 1f, 0.17f, 10f);
            _radioText = Txt(strip, "Label", semi, 26, TextAnchor.MiddleLeft);
            _radioText.horizontalOverflow = HorizontalWrapMode.Overflow;
            Fill(_radioText.rectTransform, 0f, 0f, 0.45f, 1f, 2f);
            var bar = Img(strip, "Bar", new Color(1f, 1f, 1f, 0.15f));
            Fill(bar.rectTransform, 0.47f, 0.38f, 0.98f, 0.62f, 0f);
            _radioFill = Img(bar.transform, "Fill", KrTheme.Red);
            _radioFill.type = Image.Type.Filled;
            _radioFill.fillMethod = Image.FillMethod.Horizontal;
            Fill(_radioFill.rectTransform, 0f, 0f, 1f, 1f, 0f);
        }

        public void Refresh(StoryDirector director, PlayerRig rig, Navigator nav)
        {
            if (_car == null)
                return;
            if (_indicators == null) _indicators = _car.GetComponent<CarIndicators>();
            if (_odometer == null) _odometer = _car.GetComponent<CarOdometer>();
            var rules = director != null ? director.Rules : null;
            float speed = rules != null ? rules.SpeedKmh : Mathf.Abs(_car.Speed) * 3.6f;
            float limit = rules != null ? rules.LimitKmh : 0f;
            bool over = limit > 0f && speed > limit + 1f;
            _speed.text = $"<color={(over ? "#ff5a5a" : "#ffffff")}>{speed:0}</color>";
            _unit.text = limit > 0f ? $"km/h   <color=#ff6278>LIMIT {limit:0}</color>" : "km/h";
            string g = _car.Gear;
            _gear.text = $"{Gear("P", g)}  {Gear("R", g)}  {Gear("D", g)}";
            var lights = _car.GetComponent<CarLights>();
            bool belt = rules == null || rules.SeatbeltOn;
            _status.text = $"{(belt ? "" : "<color=#ff5a5a>BELT</color>\n")}{(lights != null && lights.HeadlightsOn ? "<color=#5ab4ff>LIGHTS</color>" : "")}";
            int signal = _indicators != null ? _indicators.Signal : 0;
            bool lit = _indicators != null && _indicators.Lit;
            _left.text = signal < 0 ? (lit ? "<color=#33ff55>◄</color>" : "<color=#1c3a22>◄</color>") : "<color=#222222>◄</color>";
            _right.text = signal > 0 ? (lit ? "<color=#33ff55>►</color>" : "<color=#1c3a22>►</color>") : "<color=#222222>►</color>";
            _odo.text = _odometer != null
                ? $"<color=#9aa3ad>ODO</color> {_odometer.TotalKm:#,0.0} km   <color=#9aa3ad>TRIP</color> {_odometer.TripKm:0.0}"
                : "";

            _info.fontSize = Comfort.LargeText ? 34 : 29;
            _info.text = director != null ? StoryInfo.Compose(director, rig, nav, true, false) : "";
            Radio();
        }

        static string Gear(string gear, string current) =>
            gear == current ? $"<color=#3ee6ff>{gear}</color>" : $"<color=#4a4f57>{gear}</color>";

        void Radio()
        {
            var radio = CarRadio.Instance;
            float v = CarRadio.Volume;
            bool changed = radio != null && Time.unscaledTime - radio.ChangedAt < 2f;
            _radioText.text = radio == null ? "<color=#888888>RADIO —</color>"
                : $"RADIO  {(v <= 0.001f ? "<color=#888888>OFF</color>" : $"{Mathf.RoundToInt(v * 100f)}%")}";
            _radioFill.fillAmount = v;
            _radioFill.color = changed ? KrTheme.Accent : KrTheme.Red;
        }

        // Sample content for the Editor's renders of the driver's view.
        public void Preview()
        {
            _speed.text = "48";
            _unit.text = "km/h   <color=#ff6278>LIMIT 40</color>";
            _gear.text = $"{Gear("P", "D")}  {Gear("R", "D")}  {Gear("D", "D")}";
            _status.text = "<color=#5ab4ff>LIGHTS</color>";
            _left.text = "<color=#33ff55>◄</color>";
            _right.text = "<color=#222222>►</color>";
            _odo.text = "<color=#9aa3ad>ODO</color> 1,284.6 km   <color=#9aa3ad>TRIP</color> 3.2";
            _info.text = "<b><size=40>07:42</size></b>   Monday\n<color=#66ddff><b>Satnav:</b> In 200 m turn left onto KG 11 Ave</color>\n" +
                         "<color=#bfffcc><b>Coach:</b> Indicate before you turn.</color>";
            _radioText.text = "RADIO  50%";
            _radioFill.fillAmount = 0.5f;
        }

        static Image Img(Transform parent, string name, Color colour)
        {
            var img = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
            img.transform.SetParent(parent, false);
            img.color = colour;
            img.raycastTarget = false;
            return img;
        }

        static Text Txt(Transform parent, string name, Font font, int size, TextAnchor align)
        {
            var t = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Text>();
            t.transform.SetParent(parent, false);
            t.font = font;
            t.fontSize = size;
            t.supportRichText = true;
            t.color = Color.white;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static void Fill(RectTransform r, float x0, float y0, float x1, float y1, float pad)
        {
            r.anchorMin = new Vector2(x0, y0);
            r.anchorMax = new Vector2(x1, y1);
            r.offsetMin = new Vector2(pad, pad);
            r.offsetMax = new Vector2(-pad, -pad);
        }
    }
}
