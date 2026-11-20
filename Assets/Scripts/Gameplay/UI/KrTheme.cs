using UnityEngine;
using UnityEngine.UI;

namespace KR.Gameplay.UI
{
    // The KR look, shared by the main menu and every in-game panel: near-black panels with one cut corner, a deep
    // red wash, a cyan outline on whatever is selected, and Rajdhani (SIL Open Font License) for all text.
    // Sprites are drawn in code, so there are no image assets to keep in step.
    public static class KrTheme
    {
        public enum Weight { Medium, SemiBold, Bold }

        public static readonly Color Ink = new Color32(12, 7, 10, 235);        // panel fill
        public static readonly Color InkSoft = new Color32(28, 14, 20, 225);   // focused panel fill
        public static readonly Color Red = new Color32(215, 38, 61, 255);
        public static readonly Color RedDeep = new Color32(70, 6, 16, 255);
        public static readonly Color Pink = new Color32(255, 98, 120, 255);    // subtitles
        public static readonly Color Accent = new Color32(62, 230, 255, 255);  // selection outline
        public static readonly Color Text = new Color32(246, 238, 240, 255);
        public static readonly Color Muted = new Color32(190, 172, 178, 255);
        public static readonly Color Gold = new Color32(255, 200, 87, 255);
        public const string AccentHex = "#3ee6ff", MutedHex = "#beacb2", PinkHex = "#ff6278", GoldHex = "#ffc857";

        static Font _medium, _semi, _bold;
        static Sprite _panel, _outline;
        static Texture2D _fadeRight, _fadeUp;

        public static Font Font(Weight w)
        {
            switch (w)
            {
                case Weight.Bold: return _bold ??= Load("Rajdhani-Bold");
                case Weight.Medium: return _medium ??= Load("Rajdhani-Medium");
                default: return _semi ??= Load("Rajdhani-SemiBold");
            }
        }

        static Font Load(string name)
        {
            var f = Resources.Load<Font>("UI/Fonts/" + name);
            return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // Panel with the top-left corner cut off (9-sliced, so any size keeps the same corner).
        public static Sprite PanelSprite => _panel ??= CutCorner(false);
        // The same outline only (selection frame).
        public static Sprite OutlineSprite => _outline ??= CutCorner(true);

        // White to clear, left to right / bottom to top; tint with the Image or RawImage colour.
        public static Texture2D FadeRight => _fadeRight ??= Fade(true);
        public static Texture2D FadeUp => _fadeUp ??= Fade(false);

        static Sprite CutCorner(bool outline)
        {
            const int size = 64, cut = 18, line = 3;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int top = size - 1 - y;
                    // Distance inside the diagonal edge of the cut corner, and inside the straight edges.
                    float diag = (x + top - cut) / 1.4142f;
                    float edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, top));
                    float inside = Mathf.Min(diag, edge);
                    float a = Mathf.Clamp01(inside + 0.5f);
                    if (outline)
                        a *= Mathf.Clamp01(line - inside + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                                 new Vector4(cut + 4, cut + 4, cut + 4, cut + 4));
        }

        static Texture2D Fade(bool horizontal)
        {
            var tex = new Texture2D(horizontal ? 256 : 1, horizontal ? 1 : 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f;
                float a = 1f - Mathf.SmoothStep(0f, 1f, t);
                tex.SetPixel(horizontal ? i : 0, horizontal ? 0 : i, new Color(1f, 1f, 1f, a));
            }
            tex.Apply(false, true);
            return tex;
        }

        // ---------- building blocks ----------

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Panel(Transform parent, string name, Color color)
        {
            var img = Node(name, parent).gameObject.AddComponent<Image>();
            img.sprite = PanelSprite;
            img.type = Image.Type.Sliced;
            img.color = color;
            return img;
        }

        // A selection frame laid over a panel (hidden until selected).
        public static Image Outline(Transform parent, Color color)
        {
            var img = Node("Outline", parent).gameObject.AddComponent<Image>();
            img.sprite = OutlineSprite;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            Stretch(img.rectTransform, 0f);
            return img;
        }

        public static Text Label(Transform parent, string name, int size, Weight weight, Color color, TextAnchor align = TextAnchor.UpperLeft)
        {
            var t = Node(name, parent).gameObject.AddComponent<Text>();
            Style(t, size, weight, color, align);
            return t;
        }

        public static void Style(Text t, int size, Weight weight, Color color, TextAnchor align = TextAnchor.UpperLeft)
        {
            t.font = Font(weight);
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
        }

        // Restyles an existing panel background (the in-game panels) with the KR panel and a thin outline.
        public static void Skin(Image background, bool withOutline = true)
        {
            background.sprite = PanelSprite;
            background.type = Image.Type.Sliced;
            background.color = Ink;
            if (withOutline)
                Outline(background.transform, new Color(Red.r, Red.g, Red.b, 0.55f));
        }

        public static void Stretch(RectTransform r, float pad)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(pad, pad);
            r.offsetMax = new Vector2(-pad, -pad);
        }

        // Anchored at a corner of the parent: position of the child's pivot from that corner, in canvas units.
        public static void Place(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }
    }
}
