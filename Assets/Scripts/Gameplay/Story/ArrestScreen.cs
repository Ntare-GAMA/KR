using System.Collections;
using KR.Gameplay.Player;
using KR.Gameplay.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KR.Gameplay.Story
{
    // The cut when you are taken away in the detainee van: the screen goes black, a short title and what happened
    // show for a few seconds, then it fades back in wherever the story has put you. In the headset it is the
    // headset fade plus the same words as a message (no screen overlay there).
    public sealed class ArrestScreen : MonoBehaviour
    {
        static ArrestScreen _instance;
        Canvas _canvas;
        Image _black;
        Text _title, _body;

        public static bool Showing { get; private set; }

        // Black for `hold` seconds with the title and text; `during` runs once the screen is fully black.
        public static void Show(string title, string text, float hold, System.Action during)
        {
            if (VrRig.Active)
            {
                VrRig.Fade(1.2f);
                during?.Invoke();
                Object.FindFirstObjectByType<PlayerRig>()?.Toast($"{title}. {text}", hold + 2f);
                return;
            }
            if (_instance == null)
                _instance = new GameObject("Arrest screen").AddComponent<ArrestScreen>();
            _instance.StopAllCoroutines();
            _instance.StartCoroutine(_instance.Run(title, text, hold, during));
        }

        void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 90;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            _black = KrTheme.Node("Black", transform).gameObject.AddComponent<Image>();
            _black.color = Color.black;
            KrTheme.Stretch(_black.rectTransform, 0f);
            _title = KrTheme.Label(transform, "Title", 64, KrTheme.Weight.Bold, KrTheme.Red, TextAnchor.MiddleCenter);
            KrTheme.Place(_title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(1400f, 90f));
            _body = KrTheme.Label(transform, "Text", 30, KrTheme.Weight.SemiBold, KrTheme.Text, TextAnchor.UpperCenter);
            KrTheme.Place(_body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, 10f), new Vector2(1200f, 220f));
        }

        IEnumerator Run(string title, string text, float hold, System.Action during)
        {
            if (_canvas == null)
                Build();
            Showing = true;
            _canvas.enabled = true;
            _title.text = title;
            _body.text = text;
            for (float a = 0f; a < 1f; a += Time.unscaledDeltaTime / 1.2f)
            {
                SetAlpha(a);
                yield return null;
            }
            SetAlpha(1f);
            during?.Invoke();
            yield return new WaitForSecondsRealtime(hold);
            for (float a = 1f; a > 0f; a -= Time.unscaledDeltaTime / 1.2f)
            {
                SetAlpha(a);
                yield return null;
            }
            _canvas.enabled = false;
            Showing = false;
        }

        void SetAlpha(float a)
        {
            _black.color = new Color(0f, 0f, 0f, a);
            _title.color = new Color(_title.color.r, _title.color.g, _title.color.b, Mathf.Clamp01(a * 2f - 1f));
            _body.color = new Color(_body.color.r, _body.color.g, _body.color.b, Mathf.Clamp01(a * 2f - 1f));
        }
    }
}
