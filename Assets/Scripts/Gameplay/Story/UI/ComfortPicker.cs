using KR.Gameplay.Player;
using KR.Gameplay.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KR.Gameplay.Story
{
    // Comfort check on the driver's seat. The first time you sit in the car in the headset the game pauses and this
    // panel offers three comfort presets and the two choices that matter most in the car, each explained; pick one
    // with the left stick and X. It can be opened again from Settings ("Comfort check"). Every choice is saved and
    // can be changed one by one on the Settings page.
    public sealed class ComfortPicker : MonoBehaviour
    {
        static readonly (string name, string what)[] Presets =
        {
            ("Gentle  (recommended if you feel sick)", "Strong comfort vignette while moving, teleport walking, 45° snap turns. The calmest way to play."),
            ("Balanced", "Light vignette, teleport walking, 30° snap turns. For players who are mostly fine in VR."),
            ("Free movement", "No vignette, smooth stick walking and smooth turning. Only if you never feel sick in VR."),
        };
        const int Rows = 6;   // 3 presets, steering, seated, done

        StoryDirector _director;
        PlayerRig _rig;
        Canvas _canvas;
        Text _text;
        int _row;
        bool _wasDriving;

        public static bool Showing { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Showing = false;
            var d = Object.FindFirstObjectByType<StoryDirector>();
            if (d != null && d.GetComponent<ComfortPicker>() == null)
                d.gameObject.AddComponent<ComfortPicker>();
        }

        void Start()
        {
            _director = GetComponent<StoryDirector>();
            _rig = FindFirstObjectByType<PlayerRig>();
            var root = KrTheme.Node("Comfort check", transform);
            _canvas = root.gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 50;
            root.sizeDelta = new Vector2(1100f, 900f);
            root.localScale = Vector3.one * 0.0005f;     // about 0.55 m x 0.45 m at 0.7 m
            var bg = KrTheme.Panel(root, "Panel", KrTheme.Ink);
            KrTheme.Stretch(bg.rectTransform, 0f);
            KrTheme.Skin(bg);
            _text = KrTheme.Label(root, "Text", 34, KrTheme.Weight.SemiBold, KrTheme.Text, TextAnchor.UpperLeft);
            _text.supportRichText = true;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            KrTheme.Stretch(_text.rectTransform, 44f);
            root.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            if (Showing)
            {
                Showing = false;
                GamePause.SetModal(false);
            }
        }

        public void Show()
        {
            if (_canvas == null || _director == null)
                return;
            Showing = true;
            _row = Mathf.Clamp(_director.ComfortSettings.preset, 0, 2);
            GamePause.SetModal(true);
            _canvas.gameObject.SetActive(true);
            Place();
            Render();
        }

        void Hide()
        {
            Showing = false;
            var c = _director.ComfortSettings;
            c.asked = true;
            _director.ApplyComfort();
            GamePause.SetModal(false);
            _canvas.gameObject.SetActive(false);
            if (_rig != null)
                _rig.Toast("Comfort settings saved. Change them any time: menu button, Settings.", 4f);
        }

        void Update()
        {
            if (_director == null || _rig == null)
                return;
            bool driving = _rig.CurrentMode == PlayerRig.Mode.Driving;
            // The first time you sit in the driver's seat with the headset on.
            if (driving && !_wasDriving && VrRig.Active && !_director.ComfortSettings.asked && !Showing)
                Show();
            _wasDriving = driving;
            if (!Showing)
                return;

            _row = (_row - RigInput.NavY + Rows) % Rows;
            if (RigInput.ConfirmPressed)
            {
                var c = _director.ComfortSettings;
                switch (_row)
                {
                    case 0: Preset(c, 0, 1, 2, true); break;
                    case 1: Preset(c, 1, 0, 1, true); break;
                    case 2: Preset(c, 2, 2, 0, false); break;
                    case 3: c.stickSteer = !c.stickSteer; break;
                    case 4: c.seated = !c.seated; break;
                    default: Hide(); return;
                }
                _director.ApplyComfort();
                Buzz();
            }
            Render();
        }

        static void Preset(ComfortSave c, int preset, int turn, int vignette, bool teleport)
        {
            c.preset = preset;
            c.turn = turn;
            c.vignette = vignette;
            c.teleport = teleport;
        }

        void Place()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            var f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (f.sqrMagnitude < 0.01f)
                f = cam.transform.forward;
            var pos = cam.transform.position + f * 0.7f - Vector3.up * 0.05f;
            _canvas.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - cam.transform.position, Vector3.up));
        }

        void Render()
        {
            var c = _director.ComfortSettings;
            var sb = new System.Text.StringBuilder();
            sb.Append($"<size=50><b>COMFORT CHECK</b></size>\n<color={KrTheme.MutedHex}>Pick what feels easiest. The game is paused.</color>\n\n");
            for (int i = 0; i < Presets.Length; i++)
            {
                bool chosen = c.preset == i;
                Row(sb, i, Presets[i].name + (chosen ? $"   <color={KrTheme.AccentHex}>✔ chosen</color>" : ""));
                if (i == _row)
                    sb.Append($"<size=27><color={KrTheme.MutedHex}>      {Presets[i].what}</color></size>\n");
            }
            sb.Append('\n');
            Row(sb, 3, $"Steering: {(c.stickSteer ? "left stick (right stick click for gears)" : "your hands on the wheel and gear lever")}");
            if (_row == 3)
                sb.Append($"<size=27><color={KrTheme.MutedHex}>      Hands: squeeze a grip on the rim and turn the wheel; take the gear lever with the right grip. Some players find the stick calmer.</color></size>\n");
            Row(sb, 4, $"I play sitting down: {(c.seated ? "yes (view raised)" : "no")}");
            if (_row == 4)
                sb.Append($"<size=27><color={KrTheme.MutedHex}>      Sitting on a real chair while you drive is the most comfortable way to play.</color></size>\n");
            Row(sb, 5, "Done");
            sb.Append($"\n<size=26><color={KrTheme.MutedHex}>Tips: look ahead down the road, brake and turn gently, and take a break or take the headset off (the game pauses) if you feel unwell.\nLeft stick up/down: choose    X: select</color></size>");
            _text.text = sb.ToString();
        }

        void Row(System.Text.StringBuilder sb, int i, string label) =>
            sb.Append(i == _row ? $"<color={KrTheme.AccentHex}>›</color> <b>" : "   ").Append(label).Append(i == _row ? "</b>\n" : "\n");

        static void Buzz()
        {
            var d = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (d.isValid)
                d.SendHapticImpulse(0, 0.2f, 0.03f);
        }
    }
}
