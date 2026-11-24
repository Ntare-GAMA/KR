using System.Collections;
using System.Collections.Generic;
using System.Text;
using KR.Gameplay.Player;
using KR.Gameplay.Story;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

namespace KR.Gameplay.UI
{
    // The KR main menu: a large panel floating in front of you. A row of tiles along the top (Story Mode, the
    // three areas, your car, Settings, Credits); the selected tile's still fills the background behind its title,
    // a tagline, a short description, the Play / Continue button and "•••" (Settings, Credits, Quit).
    // Left stick (arrow keys): left/right picks a tile, down/up moves between the tiles and the buttons.
    // X (Enter) selects. The menu button (Tab) goes back.
    public sealed class MainMenu : MonoBehaviour
    {
        [SerializeField] string storyScene = "PC Only - Story Kimironko Kibagabaga";
        [SerializeField] float distance = 2.3f;

        enum Action { Play, Settings, Credits, Welcome }
        enum Focus { Tiles, Play, More }
        enum Modal { None, More, Settings, Credits, Loading, Welcome }

        sealed class TileDef
        {
            public string key, title, tag, subtitle, body;
            public Action action;
            public Texture2D still;
            public RectTransform rect;
            public Image frame;
        }

        readonly List<TileDef> _tiles = new List<TileDef>
        {
            new TileDef { key = "story", title = "Story Mode", tag = "VR", subtitle = "Rush Week in Kigali",
                body = "Live a week in Kimironko, Kibagabaga and the ALU campus. Meet people, drive the city and finish your group project before the deadline.", action = Action.Play },
            new TileDef { key = "howto", title = "How to Play", tag = "START HERE", subtitle = "The game and its controls",
                body = "What Rush Week is about, how to walk, talk, drive with your hands on the wheel and gear lever, and how to stay comfortable in VR.", action = Action.Welcome },
            new TileDef { key = "kimironko", title = "Kimironko", tag = "AREA", subtitle = "Streets, shops and the market",
                body = "Busy streets, shops, cafés and the market stall where Mzee Gakuba sells fabric. Your week starts here.", action = Action.Play },
            new TileDef { key = "kibagabaga", title = "Kibagabaga", tag = "AREA", subtitle = "Up the hill",
                body = "Quieter hillside roads, schools and homes, a short drive from Kimironko. Maman Fils lives this way.", action = Action.Play },
            new TileDef { key = "alu", title = "ALU Campus", tag = "AREA", subtitle = "Where your group meets",
                body = "The campus where you study: the library, the canteen, and Aline waiting by the gate.", action = Action.Play },
            new TileDef { key = "car", title = "Your Car", tag = "DRIVE", subtitle = "Automatic, ABS, lights and satnav",
                body = "Drive on the right, stop at the white line on red and keep to the speed limit. The police are watching.", action = Action.Play },
            new TileDef { key = "settings", title = "Settings", tag = "OPTIONS", subtitle = "Comfort and controls",
                body = "Turning, comfort vignette, walking, text size and seated play. The same settings are in the game's menu.", action = Action.Settings },
            new TileDef { key = "credits", title = "Credits", tag = "INFO", subtitle = "Assets and licences",
                body = "Everyone whose work is in KR: models, textures, sounds, voices and fonts, with their licences.", action = Action.Credits },
        };

        Canvas _canvas;
        RawImage _backdrop, _cardIcon;
        Text _title, _tagline, _body, _tileLabel, _tileTag, _playText, _progress, _clock, _hints, _modalText;
        Image _playFrame, _moreFrame, _playFill, _moreFill, _loadFill;
        RectTransform _modal;
        GameObject _dim;
        int _tile;
        Focus _focus = Focus.Play;
        Modal _modalState;
        int _row, _scroll;
        string[] _credits;
        StorySave _save;
        float _awayFor;
        readonly StringBuilder _sb = new StringBuilder();

        void Start()
        {
            GamePause.SetPanel(false);
            _save = StorySaveFile.Load();
            GamePause.Clear();
            foreach (var t in _tiles)
                t.still = Resources.Load<Texture2D>("UI/Stills/" + t.key);
            foreach (var t in _tiles)
                if (t.still == null)
                    t.still = _tiles[0].still;
            var creditsAsset = Resources.Load<TextAsset>("UI/credits");
            _credits = creditsAsset != null ? creditsAsset.text.Split('\n') : new[] { "Credits file missing." };
            Build();
            Select(0);
            SetFocus(Focus.Play);
            StartCoroutine(PlaceWhenTracked());
            // The FPS benchmark (-benchmark) goes straight into the city.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-benchmark") >= 0)
                StartCoroutine(LoadStory());
            // First launch on this device: the welcome screen (it stays on the How to Play tile afterwards).
            else if (PlayerPrefs.GetInt(WelcomeKey, 0) < WelcomeVersion)
                OpenModal(Modal.Welcome);
        }

        // ---------- layout ----------

        void Build()
        {
            var root = KrTheme.Node("Main menu", transform);
            _canvas = root.gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            root.sizeDelta = new Vector2(1920f, 1080f);
            root.localScale = Vector3.one * 0.00105f;   // about 2.0 m x 1.1 m

            // Background: the tile's still, washed with red from the left and darkened at the bottom.
            var frame = KrTheme.Panel(root, "Frame", KrTheme.RedDeep);
            KrTheme.Stretch(frame.rectTransform, -10f);
            _backdrop = KrTheme.Node("Backdrop", root).gameObject.AddComponent<RawImage>();
            KrTheme.Stretch(_backdrop.rectTransform, 0f);
            var wash = KrTheme.Node("Red wash", root).gameObject.AddComponent<RawImage>();
            wash.texture = KrTheme.FadeRight;
            wash.color = new Color(KrTheme.RedDeep.r, KrTheme.RedDeep.g, KrTheme.RedDeep.b, 0.97f);
            KrTheme.Stretch(wash.rectTransform, 0f);
            wash.rectTransform.anchorMax = new Vector2(0.78f, 1f);
            var tint = KrTheme.Node("Red tint", root).gameObject.AddComponent<Image>();
            tint.color = new Color(KrTheme.Red.r, KrTheme.Red.g, KrTheme.Red.b, 0.1f);
            KrTheme.Stretch(tint.rectTransform, 0f);
            var shade = KrTheme.Node("Bottom shade", root).gameObject.AddComponent<RawImage>();
            shade.texture = KrTheme.FadeUp;
            shade.color = new Color(0f, 0f, 0f, 0.75f);
            KrTheme.Stretch(shade.rectTransform, 0f);
            shade.rectTransform.anchorMax = new Vector2(1f, 0.45f);
            var top = KrTheme.Node("Top shade", root).gameObject.AddComponent<RawImage>();
            top.texture = KrTheme.FadeUp;
            top.color = new Color(0f, 0f, 0f, 0.6f);
            KrTheme.Stretch(top.rectTransform, 0f);
            top.rectTransform.anchorMin = new Vector2(0f, 0.7f);
            top.rectTransform.localScale = new Vector3(1f, -1f, 1f);

            // Top bar: name on the left, Kigali time on the right.
            var logo = KrTheme.Label(root, "Logo", 64, KrTheme.Weight.Bold, KrTheme.Red, TextAnchor.MiddleLeft);
            logo.text = "KR  <size=34><color=#f6eef0>KIGALI RUSH</color></size>";
            KrTheme.Place(logo.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(70f, -30f), new Vector2(700f, 80f));
            _clock = KrTheme.Label(root, "Clock", 46, KrTheme.Weight.SemiBold, KrTheme.Text, TextAnchor.MiddleRight);
            KrTheme.Place(_clock.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-70f, -30f), new Vector2(400f, 80f));

            // Tile row.
            float x = 70f;
            foreach (var t in _tiles)
            {
                var tile = KrTheme.Node("Tile " + t.key, root);
                KrTheme.Place(tile, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -130f), new Vector2(150f, 150f));
                var bg = KrTheme.Panel(tile, "Bg", KrTheme.Ink);
                KrTheme.Stretch(bg.rectTransform, 0f);
                var pic = KrTheme.Node("Still", tile).gameObject.AddComponent<RawImage>();
                pic.texture = t.still;
                pic.uvRect = new Rect(0.22f, 0f, 0.5625f, 1f);   // square from the middle of a 16:9 still
                KrTheme.Stretch(pic.rectTransform, 6f);
                var name = KrTheme.Label(tile, "Name", 21, KrTheme.Weight.Bold, KrTheme.Text, TextAnchor.LowerLeft);
                name.text = t.title.ToUpperInvariant();
                name.horizontalOverflow = HorizontalWrapMode.Overflow;
                KrTheme.Stretch(name.rectTransform, 10f);
                t.frame = KrTheme.Outline(tile, KrTheme.Accent);
                t.rect = tile;
                x += 166f;
            }
            _tileTag = KrTheme.Label(root, "Tile tag", 26, KrTheme.Weight.Bold, KrTheme.Accent, TextAnchor.MiddleLeft);
            _tileLabel = KrTheme.Label(root, "Tile label", 32, KrTheme.Weight.SemiBold, KrTheme.Pink, TextAnchor.MiddleLeft);

            // Title block.
            _title = KrTheme.Label(root, "Title", 130, KrTheme.Weight.Bold, KrTheme.Text, TextAnchor.LowerLeft);
            KrTheme.Place(_title.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(110f, 525f), new Vector2(1200f, 150f));
            _tagline = KrTheme.Label(root, "Tagline", 50, KrTheme.Weight.SemiBold, KrTheme.Pink, TextAnchor.UpperLeft);
            KrTheme.Place(_tagline.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(114f, 520f), new Vector2(1200f, 70f));
            _body = KrTheme.Label(root, "Body", 34, KrTheme.Weight.Medium, KrTheme.Text, TextAnchor.UpperLeft);
            KrTheme.Place(_body.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(114f, 450f), new Vector2(900f, 120f));

            // Buttons.
            var play = KrTheme.Node("Play", root);
            KrTheme.Place(play, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(110f, 235f), new Vector2(460f, 92f));
            _playFill = KrTheme.Panel(play, "Bg", KrTheme.Ink);
            KrTheme.Stretch(_playFill.rectTransform, 0f);
            _playText = KrTheme.Label(play, "Text", 46, KrTheme.Weight.SemiBold, KrTheme.Text, TextAnchor.MiddleCenter);
            KrTheme.Stretch(_playText.rectTransform, 0f);
            _playFrame = KrTheme.Outline(play, KrTheme.Accent);
            var more = KrTheme.Node("More", root);
            KrTheme.Place(more, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(586f, 235f), new Vector2(110f, 92f));
            _moreFill = KrTheme.Panel(more, "Bg", KrTheme.Ink);
            KrTheme.Stretch(_moreFill.rectTransform, 0f);
            var dots = KrTheme.Label(more, "Dots", 50, KrTheme.Weight.Bold, KrTheme.Text, TextAnchor.MiddleCenter);
            dots.text = "•••";
            KrTheme.Stretch(dots.rectTransform, 0f);
            _moreFrame = KrTheme.Outline(more, KrTheme.Accent);

            // Progress box (bottom left) and the KR card (right).
            var box = KrTheme.Panel(root, "Progress", KrTheme.Ink);
            KrTheme.Place(box.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(110f, 60f), new Vector2(700f, 140f));
            _progress = KrTheme.Label(box.transform, "Text", 30, KrTheme.Weight.Medium, KrTheme.Text, TextAnchor.MiddleLeft);
            KrTheme.Stretch(_progress.rectTransform, 22f);
            var card = KrTheme.Panel(root, "Card", KrTheme.Ink);
            KrTheme.Place(card.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-150f, 330f), new Vector2(330f, 360f));
            _cardIcon = KrTheme.Node("Icon", card.transform).gameObject.AddComponent<RawImage>();
            _cardIcon.texture = Resources.Load<Texture2D>("UI/kr_icon");
            KrTheme.Place(_cardIcon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(306f, 270f));
            _cardIcon.uvRect = new Rect(0.03f, 0.08f, 0.94f, 0.84f);
            var chip = KrTheme.Panel(card.transform, "Chip", KrTheme.Red);
            KrTheme.Place(chip.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 14f), new Vector2(200f, 56f));
            var chipText = KrTheme.Label(chip.transform, "Text", 28, KrTheme.Weight.Bold, KrTheme.Text, TextAnchor.MiddleCenter);
            chipText.text = "RUSH WEEK";
            KrTheme.Stretch(chipText.rectTransform, 0f);

            _hints = KrTheme.Label(root, "Hints", 26, KrTheme.Weight.Medium, KrTheme.Muted, TextAnchor.LowerRight);
            KrTheme.Place(_hints.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-70f, 40f), new Vector2(1000f, 40f));

            // Modal panel (••• options, Settings, Credits, Loading), centred over everything.
            var dim = KrTheme.Node("Modal dim", root).gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.8f);
            KrTheme.Stretch(dim.rectTransform, 0f);
            _dim = dim.gameObject;
            _dim.SetActive(false);
            var modal = KrTheme.Panel(root, "Modal", new Color(KrTheme.Ink.r, KrTheme.Ink.g, KrTheme.Ink.b, 1f));
            _modal = modal.rectTransform;
            KrTheme.Place(_modal, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 720f));
            KrTheme.Outline(_modal, KrTheme.Accent);
            _modalText = KrTheme.Label(_modal, "Text", 38, KrTheme.Weight.SemiBold, KrTheme.Text, TextAnchor.UpperLeft);
            KrTheme.Stretch(_modalText.rectTransform, 50f);
            _modalText.verticalOverflow = VerticalWrapMode.Truncate;
            var bar = KrTheme.Panel(_modal, "Bar", new Color(1f, 1f, 1f, 0.12f));
            KrTheme.Place(bar.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(880f, 30f));
            _loadFill = KrTheme.Node("Fill", bar.transform).gameObject.AddComponent<Image>();
            _loadFill.color = KrTheme.Red;
            _loadFill.type = Image.Type.Filled;
            _loadFill.fillMethod = Image.FillMethod.Horizontal;
            KrTheme.Stretch(_loadFill.rectTransform, 0f);
            _modal.gameObject.SetActive(false);
        }

        // ---------- placement: in front of where you look; follows lazily if you turn well away ----------

        IEnumerator PlaceWhenTracked()
        {
            yield return new WaitForSecondsRealtime(0.4f);
            Place(true);
        }

        void Place(bool snap)
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            var f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (f.sqrMagnitude < 0.01f)
                f = Vector3.forward;
            f.Normalize();
            var pos = cam.transform.position + f * distance;
            var rot = Quaternion.LookRotation(f, Vector3.up);
            if (snap)
                _canvas.transform.SetPositionAndRotation(pos, rot);
            else
                _canvas.transform.SetPositionAndRotation(Vector3.Lerp(_canvas.transform.position, pos, Time.unscaledDeltaTime * 3f),
                                                         Quaternion.Slerp(_canvas.transform.rotation, rot, Time.unscaledDeltaTime * 3f));
        }

        void KeepInView()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            var toMenu = _canvas.transform.position - cam.transform.position;
            float angle = Vector3.Angle(Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up), Vector3.ProjectOnPlane(toMenu, Vector3.up));
            _awayFor = angle > 55f ? _awayFor + Time.unscaledDeltaTime : (angle < 10f ? 0f : _awayFor);
            if (_awayFor > 1.2f)
                Place(false);
        }

        // ---------- input ----------

        void Update()
        {
            if (_canvas == null)
                return;
            KeepInView();
            _clock.text = System.DateTime.UtcNow.AddHours(2).ToString("HH:mm");   // Kigali, UTC+2
            if (_modalState == Modal.Loading)
                return;
            if (_modalState != Modal.None)
            {
                ModalInput();
                return;
            }
            int nx = RigInput.NavX, ny = RigInput.NavY;
            if (_focus == Focus.Tiles)
            {
                if (nx != 0)
                    Select(Mathf.Clamp(_tile + nx, 0, _tiles.Count - 1));
                if (ny < 0)
                    SetFocus(Focus.Play);
            }
            else
            {
                if (ny > 0)
                    SetFocus(Focus.Tiles);
                if (nx != 0)
                    SetFocus(nx > 0 ? Focus.More : Focus.Play);
            }
            if (RigInput.ConfirmPressed)
            {
                if (_focus == Focus.More)
                    OpenModal(Modal.More);
                else
                    // The area tiles start the story in that area; Story Mode and Your Car start at home beside the
                    // car.
                    KR.Gameplay.Story.StoryStart.Area = _tiles[_tile].key;
                    Activate(_tiles[_tile].action);
            }
        }

        void Select(int index)
        {
            if (index != _tile)
                Buzz();
            _tile = index;
            var t = _tiles[_tile];
            _backdrop.texture = t.still;
            _title.text = t.title.ToUpperInvariant();
            _tagline.text = t.subtitle;
            _body.text = t.body;
            _playText.text = t.action == Action.Play ? (_save.sessions > 0 ? "Continue" : "Play") : t.action == Action.Settings ? "Open settings"
                : t.action == Action.Welcome ? "How to play" : "Show credits";
            for (int i = 0; i < _tiles.Count; i++)
            {
                bool on = i == _tile;
                _tiles[i].rect.sizeDelta = on ? new Vector2(184f, 184f) : new Vector2(150f, 150f);
                _tiles[i].frame.enabled = on && _focus == Focus.Tiles;
                _tiles[i].rect.anchoredPosition = new Vector2(70f + i * 166f + (i > _tile ? 34f : 0f), -130f);
            }
            var sel = _tiles[_tile].rect;
            KrTheme.Place(_tileTag.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(70f, -330f), new Vector2(160f, 44f));
            KrTheme.Place(_tileLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(70f + 110f, -330f), new Vector2(900f, 44f));
            _tileTag.rectTransform.anchoredPosition = new Vector2(Mathf.Min(sel.anchoredPosition.x, 1300f), -330f);
            _tileLabel.rectTransform.anchoredPosition = new Vector2(Mathf.Min(sel.anchoredPosition.x, 1300f) + 120f, -330f);
            _tileTag.text = t.tag;
            _tileLabel.text = t.title;
            ShowProgress();
            Hints();
        }

        void SetFocus(Focus f)
        {
            if (f != _focus)
                Buzz();
            _focus = f;
            _tiles[_tile].frame.enabled = f == Focus.Tiles;
            _playFrame.enabled = f == Focus.Play;
            _moreFrame.enabled = f == Focus.More;
            _playFill.color = f == Focus.Play ? KrTheme.InkSoft : KrTheme.Ink;
            _moreFill.color = f == Focus.More ? KrTheme.InkSoft : KrTheme.Ink;
            Hints();
        }

        void Hints()
        {
            string x = VrRig.Active || XRSettings.isDeviceActive ? "X" : "Enter";
            string back = VrRig.Active || XRSettings.isDeviceActive ? "Menu button" : "Tab";
            _hints.text = _modalState == Modal.None
                ? $"Left stick: move    {x}: select"
                : $"Left stick: move    {x}: select    {back}: back";
        }

        void ShowProgress()
        {
            if (_save == null || _save.sessions == 0)
            {
                _progress.text = $"<color={KrTheme.AccentHex}><b>NEW PLAYER</b></color>\nYour Rush Week has not started yet.";
                return;
            }
            int found = _save.discovered != null ? _save.discovered.Length : 0;
            int badges = _save.badges != null ? _save.badges.Length : 0;
            _progress.text = $"<color={KrTheme.AccentHex}><b>YOUR PROGRESS</b></color>\nLevel {Badges.LevelFor(_save.xp)} · {_save.xp} XP · " +
                             $"{found} places found · {badges} badges";
        }

        void Activate(Action a)
        {
            switch (a)
            {
                case Action.Play: StartCoroutine(LoadStory()); break;
                case Action.Settings: OpenModal(Modal.Settings); break;
                case Action.Credits: OpenModal(Modal.Credits); break;
                case Action.Welcome: OpenModal(Modal.Welcome); break;
            }
        }

        // ---------- modal panels ----------

        static readonly string[] MoreRows = { "Settings", "Credits", "Quit KR", "Back" };

        void OpenModal(Modal m)
        {
            Buzz();
            _modalState = m;
            _row = 0;
            _scroll = 0;
            _modal.gameObject.SetActive(true);
            _dim.SetActive(true);
            _loadFill.transform.parent.gameObject.SetActive(m == Modal.Loading);
            _modal.sizeDelta = m == Modal.Credits || m == Modal.Welcome ? new Vector2(1500f, 920f)
                : new Vector2(1000f, m == Modal.Loading ? 360f : m == Modal.Settings ? 820f : 600f);
            RenderModal();
            Hints();
        }

        void CloseModal()
        {
            if (_modalState == Modal.Welcome)
            {
                PlayerPrefs.SetInt(WelcomeKey, WelcomeVersion);
                PlayerPrefs.Save();
            }
            _modalState = Modal.None;
            _modal.gameObject.SetActive(false);
            _dim.SetActive(false);
            Hints();
        }

        void ModalInput()
        {
            int ny = RigInput.NavY;
            if (RigInput.MenuPressed)
            {
                CloseModal();
                return;
            }
            switch (_modalState)
            {
                case Modal.More:
                    _row = (_row - ny + MoreRows.Length) % MoreRows.Length;
                    if (RigInput.ConfirmPressed)
                    {
                        switch (_row)
                        {
                            case 0: OpenModal(Modal.Settings); return;
                            case 1: OpenModal(Modal.Credits); return;
                            case 2: Application.Quit(); return;
                            default: CloseModal(); return;
                        }
                    }
                    break;
                case Modal.Settings:
                    var rows = SettingRows();
                    _row = (_row - ny + rows.Length) % rows.Length;
                    if (RigInput.ConfirmPressed)
                    {
                        if (rows[_row] == Setting.Back)
                        {
                            CloseModal();
                            return;
                        }
                        ChangeSetting(rows[_row]);
                    }
                    break;
                case Modal.Welcome:
                    int nx = RigInput.NavX;
                    if (nx != 0)
                        _scroll = Mathf.Clamp(_scroll + nx, 0, WelcomePages.Length - 1);
                    if (RigInput.ConfirmPressed)
                    {
                        if (_scroll >= WelcomePages.Length - 1)
                        {
                            CloseModal();
                            return;
                        }
                        _scroll++;
                    }
                    if (nx != 0)
                        Buzz();
                    break;
                case Modal.Credits:
                    _scroll = Mathf.Clamp(_scroll - ny * 6, 0, Mathf.Max(0, _credits.Length - 14));
                    if (RigInput.ConfirmPressed)
                    {
                        CloseModal();
                        return;
                    }
                    break;
            }
            if (ny != 0)
                Buzz();
            RenderModal();
        }

        enum Setting { Turning, Vignette, Walking, Steering, TextSize, Seated, Radio, Resolution, DisplayMode, Back }

        static Setting[] _settingRows;
        static Setting[] SettingRows()
        {
            if (_settingRows != null)
                return _settingRows;
            var list = new List<Setting>();
            foreach (Setting r in System.Enum.GetValues(typeof(Setting)))
                if (r != Setting.DisplayMode || DisplaySettings.HasMonitor)
                    list.Add(r);
            return _settingRows = list.ToArray();
        }

        static bool HeadsetResolution => VrRig.Active || XRSettings.isDeviceActive || !DisplaySettings.HasMonitor;

        void ChangeSetting(Setting row)
        {
            _save ??= new StorySave();
            _save.comfort ??= new ComfortSave();
            var c = _save.comfort;
            switch (row)
            {
                case Setting.Turning: c.turn = (c.turn + 1) % 3; break;
                case Setting.Vignette: c.vignette = (c.vignette + 1) % 3; break;
                case Setting.Walking: c.teleport = !c.teleport; break;
                case Setting.Steering: c.stickSteer = !c.stickSteer; break;
                case Setting.TextSize: c.largeText = !c.largeText; break;
                case Setting.Seated: c.seated = !c.seated; break;
                case Setting.Radio: CarRadio.Volume = CarRadio.Volume >= 0.95f ? 0f : CarRadio.Volume + 0.1f; return;
                case Setting.Resolution:
                    if (HeadsetResolution) DisplaySettings.NextVrScale();
                    else DisplaySettings.NextResolution();
                    return;
                case Setting.DisplayMode: DisplaySettings.NextMode(); return;
            }
            StorySaveFile.Write(_save);
        }

        string SettingText(Setting r, ComfortSave c)
        {
            string v(string x) => $"   <color={KrTheme.MutedHex}>{x}</color>";
            switch (r)
            {
                case Setting.Turning: return "Turning" + v(c.turn == 0 ? "snap 30°" : c.turn == 1 ? "snap 45°" : "smooth");
                case Setting.Vignette: return "Comfort vignette" + v(c.vignette == 0 ? "off" : c.vignette == 1 ? "low" : "high");
                case Setting.Walking: return "Walking" + v(c.teleport ? "teleport" : "stick");
                case Setting.Steering: return "Steering" + v(c.stickSteer ? "left stick" : "hands on the wheel and gear lever");
                case Setting.TextSize: return "Text size" + v(c.largeText ? "large" : "normal");
                case Setting.Seated: return "Seated play" + v(c.seated ? "on" : "off");
                case Setting.Radio: return "Car radio" + v(CarRadio.VolumeText);
                case Setting.Resolution:
                    return HeadsetResolution ? "Render resolution" + v(DisplaySettings.VrScaleText + " (lower is smoother)")
                                             : "Screen resolution" + v(DisplaySettings.ResolutionText);
                case Setting.DisplayMode: return "Display" + v(DisplaySettings.ModeText);
                default: return "Back";
            }
        }

        // ---------- welcome / how to play ----------

        const string WelcomeKey = "kr.welcome.seen";
        const int WelcomeVersion = 1;

        static readonly (string title, string vr, string desk)[] WelcomePages =
        {
            ("WELCOME TO KIGALI RUSH",
             "It is Rush Week. You are a student at ALU in Kigali, living in Kimironko and Kibagabaga.\n\n" +
             "Each milestone (visit someone, share a tea, read, learn) runs against the clock. Walk the streets, talk to people, " +
             "and drive across the city while keeping to Rwanda's road rules: the police are watching, and a crash brings the police and an ambulance.\n\n" +
             "Your phone reminds you of plans and rings with calls. Your wallet holds your student card, ID and driving licence.",
             null),
            ("ON FOOT",
             "Left stick: walk. With teleport on, push it forward, aim the arc and let go to step there.\n" +
             "Right stick: turn.\n" +
             "Grip: pick things up, open doors (pull the handle), hold your car's door handle to get in.\n" +
             "X: talk to someone (\"X Talk\" shows above them), choose in menus, start a milestone.\n" +
             "A: get in the car, sit down or stand up.\n" +
             "Menu button (☰): the info panel: status, journal, phone, wallet, settings. The game pauses while it is open.",
             "W A S D: walk    Shift: run    Mouse: look\nE: get in the car, sit or stand    G (or right click): grab\n" +
             "Enter: talk, choose, start a milestone    Tab: info panel    M: map    P: phone    I: wallet    C: camera"),
            ("DRIVING",
             "Steer with your hands: squeeze a grip with your hand on the rim and turn the wheel. Let go and it comes back to straight.\n" +
             "Gears: take the lever by your right knee with the right grip. Push it forward for P, the middle for R, pull it back for D (only when stopped).\n" +
             "Right trigger: accelerate.    Left trigger: brake.    B: handbrake.\n" +
             "Y: seatbelt.    Left stick click: headlights.    Right stick left/right: indicators.    Right stick up/down: radio.\n" +
             "Press the middle of the wheel: horn.    A: get out (in P).\n" +
             "Prefer the sticks? Settings, Steering: left stick (then the right stick click changes gear).",
             "W: accelerate    S: brake    A D: steer    R: gear P, D, R    Space: handbrake\n" +
             "B: seatbelt    L: lights    Z X: indicators    H: horn    - +: radio    E: get out (in P)"),
            ("STAY COMFORTABLE",
             "The first time you sit in the car, the Comfort check offers three presets. Gentle is the calmest.\n\n" +
             "Sit on a real chair while you drive, look ahead down the road, and brake and turn gently.\n" +
             "Teleport walking, snap turns and the comfort vignette all help. Change them any time in Settings.\n" +
             "If you feel unwell, stop and take the headset off: the game pauses until you put it back on.\n\n" +
             "To leave: menu button, Settings, Quit to main menu (your progress is saved). In the main menu, ••• then Quit KR.",
             null),
        };

        void RenderWelcome()
        {
            bool vr = VrRig.Active || XRSettings.isDeviceActive;
            var page = WelcomePages[Mathf.Clamp(_scroll, 0, WelcomePages.Length - 1)];
            _sb.Append($"<size=60><b>{page.title}</b></size>\n\n<size=33>");
            _sb.Append(page.vr);
            if (!vr && page.desk != null)
                _sb.Append($"\n\n<color={KrTheme.AccentHex}>On a keyboard</color>\n").Append(page.desk);
            _sb.Append("</size>\n\n<size=26>");
            for (int i = 0; i < WelcomePages.Length; i++)
                _sb.Append(i == _scroll ? $"<color={KrTheme.AccentHex}>●</color> " : $"<color={KrTheme.MutedHex}>○</color> ");
            string x = vr ? "X" : "Enter";
            _sb.Append($"   <color={KrTheme.MutedHex}>Left stick left/right: pages    {x}: {(_scroll >= WelcomePages.Length - 1 ? "start" : "next")}</color></size>");
        }

        void RenderModal()
        {
            _sb.Clear();
            switch (_modalState)
            {
                case Modal.More:
                    Header("OPTIONS");
                    Rows(MoreRows);
                    break;
                case Modal.Settings:
                    var c = _save.comfort ?? new ComfortSave();
                    Header("SETTINGS");
                    var rows = SettingRows();
                    var labels = new string[rows.Length];
                    for (int i = 0; i < rows.Length; i++)
                        labels[i] = SettingText(rows[i], c);
                    Rows(labels);
                    break;
                case Modal.Welcome:
                    RenderWelcome();
                    break;
                case Modal.Credits:
                    Header("CREDITS");
                    _sb.Append("<size=26>");
                    for (int i = _scroll; i < Mathf.Min(_credits.Length, _scroll + 18); i++)
                        _sb.Append(_credits[i]).Append('\n');
                    _sb.Append($"</size>\n<size=24><color={KrTheme.MutedHex}>Up/down to scroll · {_scroll + 1}-{Mathf.Min(_credits.Length, _scroll + 18)} of {_credits.Length}</color></size>");
                    break;
                case Modal.Loading:
                    Header("LOADING KIGALI…");
                    _sb.Append($"<size=32><color={KrTheme.MutedHex}>Getting the city ready. This can take a little while.</color></size>");
                    break;
            }
            _modalText.text = _sb.ToString();
        }

        void Header(string text) => _sb.Append($"<size=60><b>{text}</b></size>\n\n");

        void Rows(string[] rows)
        {
            for (int i = 0; i < rows.Length; i++)
                _sb.Append(i == _row ? $"<color={KrTheme.AccentHex}>›  </color><b>" : "    ").Append(rows[i]).Append(i == _row ? "</b>\n" : "\n");
        }

        IEnumerator LoadStory()
        {
            OpenModal(Modal.Loading);
            _loadFill.fillAmount = 0f;
            yield return null;
            var op = SceneManager.LoadSceneAsync(storyScene, LoadSceneMode.Single);
            if (op == null)
            {
                _modalText.text = "<size=60><b>COULD NOT LOAD</b></size>\n\nThe city scene is missing from this build.";
                yield break;
            }
            while (!op.isDone)
            {
                _loadFill.fillAmount = Mathf.Clamp01(op.progress / 0.9f);
                yield return null;
            }
        }

        static void Buzz()
        {
            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (device.isValid)
                device.SendHapticImpulse(0, 0.15f, 0.03f);
        }
    }
}
