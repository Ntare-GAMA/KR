using UnityEngine;

namespace KR.Gameplay.World
{
    // Outdoor TV screen: news-studio colours shift on the panel and a headline ticker scrolls along the bottom.
    // The ticker is generic text only; no real broadcaster branding.
    public sealed class ScreenAnimator : MonoBehaviour
    {
        [SerializeField] Renderer screen;
        [SerializeField] TextMesh ticker;
        [SerializeField] float tickerWidth = 3f;
        [SerializeField] float tickerSpeed = 0.6f;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly Color[] Palette =
        {
            new Color(0.1f, 0.25f, 0.8f), new Color(0.85f, 0.9f, 1f), new Color(0.7f, 0.1f, 0.12f), new Color(0.15f, 0.5f, 0.85f),
        };
        MaterialPropertyBlock _block;
        float _x;

        public void Configure(Renderer panel, TextMesh text, float width)
        {
            screen = panel;
            ticker = text;
            tickerWidth = width;
        }

        void Awake() => _block = new MaterialPropertyBlock();

        void Update()
        {
            if (screen == null)
            {
                Debug.LogWarning($"[Screen] '{name}' has no screen renderer; disabled.");
                enabled = false;
                return;
            }
            float t = Time.time * 0.25f;
            int i = Mathf.FloorToInt(t) % Palette.Length;
            Color c = Color.Lerp(Palette[i], Palette[(i + 1) % Palette.Length], Mathf.SmoothStep(0f, 1f, Mathf.Repeat(t, 1f)));
            var cycle = DayNightCycle.Instance;
            float boost = cycle != null ? Mathf.Lerp(1.2f, 2.5f, cycle.Night) : 1.5f;
            _block.SetColor(EmissionId, c * boost);
            screen.SetPropertyBlock(_block);

            // A zero width keeps the headline fixed in place (it would otherwise overrun the panel edges).
            if (ticker == null || tickerWidth <= 0f)
                return;
            _x -= tickerSpeed * Time.deltaTime;
            if (_x < -tickerWidth * 2.5f)
                _x = tickerWidth * 0.5f;
            var p = ticker.transform.localPosition;
            ticker.transform.localPosition = new Vector3(_x, p.y, p.z);
        }
    }
}
