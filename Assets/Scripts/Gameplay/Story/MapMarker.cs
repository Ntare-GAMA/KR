using UnityEngine;
using UnityEngine.UI;

namespace KR.Gameplay.Story
{
    // Map symbols drawn as simple shapes (no sprites needed): the player's arrow and the destination pin.
    public sealed class MapMarker : MaskableGraphic
    {
        public enum Shape { Arrow, Diamond }

        public Shape Kind { get; set; } = Shape.Arrow;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            Vector2 c = r.center;
            float w = r.width * 0.5f, h = r.height * 0.5f;
            if (Kind == Shape.Arrow)
            {
                // A chevron pointing up (the way you face).
                vh.AddVert(c + new Vector2(0f, h), color, Vector2.zero);
                vh.AddVert(c + new Vector2(w, -h), color, Vector2.zero);
                vh.AddVert(c + new Vector2(0f, -h * 0.45f), color, Vector2.zero);
                vh.AddVert(c + new Vector2(-w, -h), color, Vector2.zero);
                vh.AddTriangle(0, 1, 2);
                vh.AddTriangle(0, 2, 3);
            }
            else
            {
                vh.AddVert(c + new Vector2(0f, h), color, Vector2.zero);
                vh.AddVert(c + new Vector2(w, 0f), color, Vector2.zero);
                vh.AddVert(c + new Vector2(0f, -h), color, Vector2.zero);
                vh.AddVert(c + new Vector2(-w, 0f), color, Vector2.zero);
                vh.AddTriangle(0, 1, 2);
                vh.AddTriangle(0, 2, 3);
            }
        }
    }
}
