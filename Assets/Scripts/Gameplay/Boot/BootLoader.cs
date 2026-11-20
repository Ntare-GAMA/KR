using System.Collections;
using KR.Gameplay.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KR.Gameplay.Boot
{
    // First scene of the headset build: shows the KR card and a progress bar at once, then loads the next scene
    // (the main menu) in the background, so the headset never sits on a black screen.
    public sealed class BootLoader : MonoBehaviour
    {
        [SerializeField] int sceneIndex = 1;

        Image _fill;
        Text _label;

        public void Configure(int index) => sceneIndex = index;

        void Awake()
        {
            var root = KrTheme.Node("Loading card", transform);
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            root.sizeDelta = new Vector2(1000f, 1250f);
            root.position = new Vector3(0f, 1.6f, 2f);
            root.localScale = Vector3.one * 0.0011f;

            var panel = KrTheme.Panel(root, "Panel", KrTheme.Ink);
            KrTheme.Stretch(panel.rectTransform, -30f);
            KrTheme.Outline(panel.transform, new Color(KrTheme.Red.r, KrTheme.Red.g, KrTheme.Red.b, 0.6f));
            var icon = KrTheme.Node("Icon", root).gameObject.AddComponent<RawImage>();
            icon.texture = Resources.Load<Texture2D>("UI/kr_icon");
            KrTheme.Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(940f, 940f));

            var bar = KrTheme.Panel(root, "Bar", new Color(1f, 1f, 1f, 0.12f));
            KrTheme.Place(bar.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(900f, 34f));
            _fill = KrTheme.Node("Fill", bar.transform).gameObject.AddComponent<Image>();
            _fill.color = KrTheme.Red;
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Horizontal;
            _fill.fillAmount = 0f;
            KrTheme.Stretch(_fill.rectTransform, 0f);
            _label = KrTheme.Label(root, "Status", 54, KrTheme.Weight.SemiBold, KrTheme.Text, TextAnchor.MiddleCenter);
            KrTheme.Place(_label.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(1000f, 90f));
            _label.text = "LOADING…";
        }

        IEnumerator Start()
        {
            // Two frames so the card is on screen before loading takes the CPU.
            yield return null;
            yield return null;
            var op = SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Single);
            if (op == null)
            {
                _label.text = "Could not load the menu.";
                yield break;
            }
            while (!op.isDone)
            {
                float p = Mathf.Clamp01(op.progress / 0.9f);
                _fill.fillAmount = p;
                _label.text = $"LOADING…  {Mathf.RoundToInt(p * 100f)}%";
                yield return null;
            }
        }
    }
}
