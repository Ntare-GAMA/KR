using UnityEngine;
using UnityEngine.UI;

namespace KR.Gameplay.Story
{
    // A short line shown above someone's head out in the world (never in front of your face): a shouting driver,
    // a horn-happy taxi. One bubble per speaker; Say() replaces the line, which fades after a few seconds.
    public sealed class SpeechBubble : MonoBehaviour
    {
        Canvas _canvas;
        Text _text;
        Transform _anchor;
        float _height;
        float _hideAt;

        public static SpeechBubble Create(Transform parent)
        {
            var go = new GameObject("Speech bubble", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var bubble = go.AddComponent<SpeechBubble>();
            bubble.Build();
            return bubble;
        }

        void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(520f, 110f);
            rt.localScale = Vector3.one * 0.0045f;
            var bg = new GameObject("Back", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
            bg.transform.SetParent(transform, false);
            bg.color = new Color(1f, 1f, 1f, 0.92f);
            Stretch(bg.rectTransform, 0f);
            _text = new GameObject("Line", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Text>();
            _text.transform.SetParent(transform, false);
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.fontSize = 34;
            _text.color = new Color(0.08f, 0.08f, 0.1f);
            _text.alignment = TextAnchor.MiddleCenter;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            Stretch(_text.rectTransform, 12f);
            gameObject.SetActive(false);
        }

        static void Stretch(RectTransform r, float pad)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(pad, pad);
            r.offsetMax = new Vector2(-pad, -pad);
        }

        public void Say(Transform speaker, float height, string line, float seconds = 3.5f)
        {
            _anchor = speaker;
            _height = height;
            _text.text = line;
            _hideAt = Time.time + seconds;
            gameObject.SetActive(true);
            LateUpdate();
        }

        public void Hide() => gameObject.SetActive(false);

        void LateUpdate()
        {
            if (_anchor == null || Time.time > _hideAt)
            {
                gameObject.SetActive(false);
                return;
            }
            var cam = Camera.main;
            Vector3 pos = _anchor.position + Vector3.up * _height;
            transform.SetPositionAndRotation(pos, cam != null ? Quaternion.LookRotation(pos - cam.transform.position) : Quaternion.identity);
        }
    }
}
