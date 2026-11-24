using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KR.Gameplay.Story
{
    // The satnav route on a map: a thick polyline in the map's own space (points given as 0..1 across the map).
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RouteLine : MaskableGraphic
    {
        readonly List<Vector2> _uv = new List<Vector2>();
        public float Width { get; set; } = 6f;

        public void SetPoints(IReadOnlyList<Vector2> uv)
        {
            _uv.Clear();
            _uv.AddRange(uv);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            Vector2 P(Vector2 uv) => new Vector2(r.xMin + uv.x * r.width, r.yMin + uv.y * r.height);
            float half = Width * 0.5f;
            for (int i = 1; i < _uv.Count; i++)
            {
                Vector2 a = P(_uv[i - 1]), b = P(_uv[i]);
                var d = b - a;
                if (d.sqrMagnitude < 0.01f)
                    continue;
                var n = new Vector2(-d.y, d.x).normalized * half;
                // Each segment is a quad, extended by half the width so the joints have no gaps.
                var e = d.normalized * half;
                int v = vh.currentVertCount;
                vh.AddVert(a - e - n, color, Vector2.zero);
                vh.AddVert(a - e + n, color, Vector2.zero);
                vh.AddVert(b + e + n, color, Vector2.zero);
                vh.AddVert(b + e - n, color, Vector2.zero);
                vh.AddTriangle(v, v + 1, v + 2);
                vh.AddTriangle(v, v + 2, v + 3);
            }
        }
    }
}
