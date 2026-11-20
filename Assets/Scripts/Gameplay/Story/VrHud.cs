using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KR.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace KR.Gameplay.Story
{
    // The info panel. Nothing floats in view by default: the left controller's menu button (☰, Tab on a desktop)
    // opens it and closes it again, and the game pauses while it is open. Walking it is a tablet held above your
    // left hand; parked in the car it sits in front of you (while driving, the dashboard screen has everything).
    // Pages (left stick left/right): Status, Journal, Progress, Photos, Settings. Up/down picks a row, X uses it.
    // With the panel closed, the left controller buzzes when something new happens.
    public sealed class VrHud : MonoBehaviour
    {
        enum Page { Status, Journal, Progress, Photos, Settings }

        [SerializeField] StoryDirector director;
        [SerializeField] PlayerRig rig;
        [SerializeField] Camera head;
        [SerializeField] Vector3 offset = new Vector3(0f, 0.1f, 0.22f);   // from the left controller

        Canvas _canvas;
        Text _tabs, _text;
        RectTransform _mapArea;
        RawImage _photo;
        Navigator _nav;
        Discoveries _places;
        bool _open;
        Page _page;
        int _row;
        string _lastNews, _lastPrompt, _shownPhoto;
        StoryDirector.Phase _lastPhase;
        Texture2D _photoTex;
        readonly StringBuilder _sb = new StringBuilder();
        static readonly string[] Sectors = { "Kimironko", "Kibagabaga", "ALU campus" };

        public bool Open => _open;

        public void Configure(StoryDirector story, PlayerRig playerRig, Camera headCamera)
        {
            director = story;
            rig = playerRig;
            head = headCamera;
        }

        void Start()
        {
            _nav = FindFirstObjectByType<Navigator>();
            _places = FindFirstObjectByType<Discoveries>();
            var go = new GameObject("Info panel", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(860f, 1040f);
            go.transform.localScale = Vector3.one * 0.00036f;     // about 0.31 m x 0.37 m

            var bg = Child<Image>(go.transform, "Panel");
            Fill(bg.rectTransform, 0f, 0f, 1f, 1f, 0f);
            KR.Gameplay.UI.KrTheme.Skin(bg);
            _tabs = MakeText(go.transform, "Tabs", 0.93f, 1f, 26);
            _text = MakeText(go.transform, "Body", 0.42f, 0.93f, 30);
            _mapArea = new GameObject("Map area", typeof(RectTransform)).GetComponent<RectTransform>();
            _mapArea.SetParent(go.transform, false);
            Fill(_mapArea, 0f, 0f, 1f, 0.42f, 16f);
            if (_nav != null)
                MapView.Create(_mapArea, _nav, MapView.Mode.HeadingUp, 500f, 0, false);
            _photo = Child<RawImage>(go.transform, "Photo");
            Fill(_photo.rectTransform, 0.06f, 0.03f, 0.94f, 0.42f, 0f);
            _photo.gameObject.SetActive(false);
            go.SetActive(false);
        }

        Text MakeText(Transform parent, string name, float y0, float y1, int size)
        {
            var t = Child<Text>(parent, name);
            t.font = KR.Gameplay.UI.KrTheme.Font(KR.Gameplay.UI.KrTheme.Weight.SemiBold);
            t.fontSize = size + 4;
            t.supportRichText = true;
            t.color = Color.white;
            t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            Fill(t.rectTransform, 0f, y0, 1f, y1, 20f);
            return t;
        }

        static T Child<T>(Transform parent, string name) where T : Component
        {
            var g = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
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
            if (_canvas == null || rig == null || director == null)
                return;
            bool driving = rig.CurrentMode == PlayerRig.Mode.Driving;
            bool parked = driving && rig.Car != null && rig.Car.Parked;
            if (RigInput.MenuPressed && (!driving || parked) && (!rig.Busy || _open))
                _open = !_open;
            if (driving && !parked)
                _open = false;
            GamePause.SetPanel(_open);

            if (_canvas.gameObject.activeSelf != _open)
                _canvas.gameObject.SetActive(_open);
            BuzzOnNews(_open || driving);
            if (!_open)
                return;

            Place(driving);
            Navigate();
            int font = Comfort.LargeText ? 40 : 34;
            _text.fontSize = font;
            _tabs.text = Tabs();
            _text.text = Body();
            bool status = _page == Page.Status;
            _mapArea.gameObject.SetActive(status);
            _photo.gameObject.SetActive(_page == Page.Photos && _photo.texture != null);
            _text.rectTransform.anchorMin = new Vector2(0f, status || _page == Page.Photos ? 0.42f : 0f);
        }

        // Walking: above and a little ahead of the left hand. Parked: in front of you over the dashboard.
        void Place(bool inCar)
        {
            var cam = head != null ? head : Camera.main;
            if (cam == null)
                return;
            Vector3 pos;
            if (!inCar && VrRig.Active && rig.VrLeftHand != null)
            {
                var hand = rig.VrLeftHand;
                pos = hand.position + Vector3.up * offset.y + Vector3.ProjectOnPlane(hand.forward, Vector3.up).normalized * offset.z;
            }
            else
            {
                var f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                pos = cam.transform.position + f * 0.6f - Vector3.up * 0.12f;
            }
            _canvas.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - cam.transform.position, Vector3.up));
        }

        void Navigate()
        {
            int x = RigInput.NavX;
            if (x != 0)
            {
                _page = (Page)(((int)_page + x + 5) % 5);
                _row = 0;
            }
            int rows = Rows();
            if (rows > 0)
                _row = (_row - RigInput.NavY + rows) % rows;
            if (RigInput.ConfirmPressed)
                Use();
        }

        int Rows() => _page switch
        {
            Page.Journal => _places != null ? _places.Catalog.places.Length : 0,
            Page.Photos => director.Photos.Count,
            Page.Settings => 10,
            _ => 0,
        };

        string Tabs()
        {
            _sb.Clear();
            foreach (Page p in System.Enum.GetValues(typeof(Page)))
                _sb.Append(p == _page ? $"<b><color=#3ee6ff>{p.ToString().ToUpperInvariant()}</color></b>   " : $"<color=#beacb2>{p.ToString().ToUpperInvariant()}</color>   ");
            _sb.Append($"\n<size=20><color=#999999>Left stick: pages and rows   {(VrRig.Active ? "X" : "Enter")}: select   {(VrRig.Active ? "Menu button" : "Tab")}: close (game paused)</color></size>");
            return _sb.ToString();
        }

        string Body()
        {
            switch (_page)
            {
                case Page.Journal: return Journal();
                case Page.Progress: return Progress();
                case Page.Photos: return Gallery();
                case Page.Settings: return Settings();
                default: return StoryInfo.Compose(director, rig, _nav, false);
            }
        }

        string Journal()
        {
            _sb.Clear();
            if (_places == null || _places.Catalog == null || _places.Catalog.places.Length == 0)
                return "No places yet.";
            foreach (var s in Sectors)
            {
                var (found, total) = _places.Count(s);
                _sb.Append($"<b>{s}</b> {found}/{total}   ");
            }
            _sb.Append("\n\n");
            var all = _places.Catalog.places;
            int from = Mathf.Clamp(_row - 4, 0, Mathf.Max(0, all.Length - 9));
            for (int i = from; i < Mathf.Min(all.Length, from + 9); i++)
            {
                var p = all[i];
                bool known = director.IsDiscovered(p.id);
                string name = known ? p.name : "? ? ?";
                _sb.Append(i == _row ? "<b>> " : "   ").Append(known ? "✔ " : "· ").Append(name)
                   .Append($"  <color=#999999>{p.sector}</color>").Append(i == _row ? "</b>\n" : "\n");
            }
            var sel = all[Mathf.Clamp(_row, 0, all.Length - 1)];
            _sb.Append("\n");
            if (!director.IsDiscovered(sel.id))
                _sb.Append($"<i>Not discovered yet. It is a {sel.category} in {sel.sector}.</i>");
            else if (string.IsNullOrEmpty(sel.cardText))
                _sb.Append($"<b>{sel.name}</b> ({sel.category})\n<i>Card not written yet.</i>");
            else
                _sb.Append($"<b>{sel.name}</b> ({sel.category})\n{sel.cardText}\n<size=22><color=#999999>Source: {sel.cardSource}  [{sel.reviewStatus}]</color></size>");
            return _sb.ToString();
        }

        string Progress()
        {
            _sb.Clear();
            int level = director.Level, xp = director.Xp;
            int next = level < Badges.Levels.Length ? Badges.Levels[level] : -1;
            _sb.Append($"<b><size=40>Level {level}</size></b>   {xp} XP{(next > 0 ? $"   ({next - xp} to level {level + 1})" : "")}\n");
            _sb.Append($"{StoryHud.LicenceText(director)}\n\n<b>Badges</b>\n");
            foreach (var b in Badges.All)
            {
                bool got = director.BadgesEarned.Contains(b.id);
                _sb.Append(got ? "<color=#ffd966>★ " : "<color=#888888>☆ ").Append(b.title).Append("</color>  <size=22>").Append(b.how).Append("</size>\n");
            }
            return _sb.ToString();
        }

        string Gallery()
        {
            _sb.Clear();
            var photos = director.Photos;
            if (photos.Count == 0)
            {
                ShowPhoto(null);
                return VrRig.Active ? "No photos yet. Walking, hold the right grip to raise the camera and pull the right trigger."
                                    : "No photos yet. Press C to raise the camera and click to take a photo.";
            }
            // Newest first.
            int count = photos.Count;
            int from = Mathf.Clamp(_row - 3, 0, Mathf.Max(0, count - 6));
            for (int i = from; i < Mathf.Min(count, from + 6); i++)
            {
                var p = photos[count - 1 - i];
                _sb.Append(i == _row ? "<b>> " : "   ").Append(p.takenUtc.Length >= 16 ? p.takenUtc.Substring(0, 16).Replace('T', ' ') : p.takenUtc)
                   .Append(string.IsNullOrEmpty(p.missionId) ? "" : $"  ({p.missionId})").Append(i == _row ? "</b>\n" : "\n");
            }
            ShowPhoto(photos[count - 1 - Mathf.Clamp(_row, 0, count - 1)].file);
            return _sb.ToString();
        }

        // Loads only the selected photo (and frees the previous one).
        void ShowPhoto(string file)
        {
            if (file == _shownPhoto)
                return;
            _shownPhoto = file;
            if (_photoTex != null)
                Destroy(_photoTex);
            _photoTex = null;
            _photo.texture = null;
            if (file == null)
                return;
            string path = Path.Combine(PhotoCamera.Folder, file);
            if (!File.Exists(path))
                return;
            _photoTex = new Texture2D(2, 2);
            if (_photoTex.LoadImage(File.ReadAllBytes(path)))
                _photo.texture = _photoTex;
        }

        string Settings()
        {
            var c = director.ComfortSettings;
            string dest = _nav == null ? "-" : _nav.OnTour ? "on the Kimironko tour" : _nav.Choice < 0 ? "follow the milestone" : _nav.Places[_nav.Choice].name;
            string[] rows =
            {
                $"Turning: {(c.turn == 0 ? "snap 30°" : c.turn == 1 ? "snap 45°" : "smooth")}",
                $"Comfort vignette: {(c.vignette == 0 ? "off" : c.vignette == 1 ? "low" : "high")}",
                $"Walking: {(c.teleport ? "teleport (aim with the left stick)" : "stick")}",
                $"Text size: {(c.largeText ? "large" : "normal")}",
                $"Seated play: {(c.seated ? "on (view raised)" : "off")}",
                "Recentre view",
                $"Choose a destination (now: {dest})",
                "Start the Kimironko tour",
                $"Car radio: {CarRadio.VolumeText}  (plays all game; in the car: right stick up/down)",
                "Quit to main menu",
            };
            _sb.Clear();
            _sb.Append("<b>Settings</b>\n\n");
            for (int i = 0; i < rows.Length; i++)
                _sb.Append(i == _row ? "<color=#3ee6ff>›</color> <b>" : "   ").Append(rows[i]).Append(i == _row ? "</b>\n" : "\n");
            return _sb.ToString();
        }

        void Use()
        {
            if (_page != Page.Settings)
                return;
            var c = director.ComfortSettings;
            switch (_row)
            {
                case 0: c.turn = (c.turn + 1) % 3; break;
                case 1: c.vignette = (c.vignette + 1) % 3; break;
                case 2: c.teleport = !c.teleport; break;
                case 3: c.largeText = !c.largeText; break;
                case 4: c.seated = !c.seated; break;
                case 5: Comfort.RecenterRequested = true; break;
                case 6:
                    var picker = FindFirstObjectByType<DestinationPicker>();
                    if (picker != null)
                    {
                        _open = false;
                        GamePause.SetPanel(false);
                        picker.Show();
                    }
                    break;
                case 7: StartTour(); break;
                case 8: CarRadio.Volume = CarRadio.Volume >= 0.95f ? 0f : CarRadio.Volume + 0.1f; break;
                case 9: QuitToMenu(); return;
            }
            director.ApplyComfort();
        }

        // Back to the KR main menu (headset build; the PC build has no menu scene).
        void QuitToMenu()
        {
            const string menu = "Quest Menu";
            if (!Application.CanStreamedLevelBeLoaded(menu))
            {
                rig.Toast("The main menu is only in the headset build.", 3f);
                return;
            }
            _open = false;
            GamePause.SetPanel(false);
            UnityEngine.SceneManagement.SceneManager.LoadScene(menu);
        }

        void StartTour()
        {
            if (_nav == null || _places == null)
                return;
            var stops = _places.Catalog.tour.Select(id => _places.Catalog.Find(id)).Where(p => p != null)
                .Select(p => (p.name, p.position)).ToList();
            if (stops.Count == 0)
                return;
            _nav.TourFinished = () =>
            {
                director.Badge("tour");
                director.Reward("tour:kimironko", 150, "Kimironko tour");
            };
            _nav.StartTour(stops);
            _open = false;
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
            if (director.State != _lastPhase)
            {
                _lastPhase = director.State;
                if (!visible)
                    Pulse(XRNode.LeftHand, 0.4f, 0.15f);
            }
            string news = director.Toast != null && Time.time < director.ToastUntil ? director.Toast : null;
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
