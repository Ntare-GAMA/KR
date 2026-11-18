using System.Collections.Generic;
using System.IO;
using KR.Editor.GIS;
using KR.Gameplay.World;
using UnityEditor;
using UnityEngine;

namespace KR.Editor.Kigali
{
    // Draws the city map image for the satnav and the full map from the same OSM data the city is built from:
    // building footprints, minor roads in white and main roads in amber, on a dark ground (easy to read in a
    // headset), with everything outside the play area dimmed. 1.5 m per pixel.
    // Map data © OpenStreetMap contributors (ODbL); credited in Credits with the rest of the OSM-derived city.
    internal static class CityMapBaker
    {
        const float MetresPerPixel = 1.5f;
        const int MaxPixels = 4096;
        static readonly Color32 Outside = new Color32(18, 21, 22, 255);
        static readonly Color32 Ground = new Color32(38, 46, 41, 255);
        static readonly Color32 Building = new Color32(78, 82, 88, 255);
        static readonly Color32 MinorRoad = new Color32(214, 216, 212, 255);
        static readonly Color32 MainRoad = new Color32(246, 196, 84, 255);

        public static CityMap Bake(DistrictImporter.Road[] roads, DistrictImporter.Building[] buildings, string outDir, Transform root)
        {
            if (!PlayArea.Bounds2D(out var min, out var max))
            {
                Debug.LogWarning("[Map] No play area; no city map.");
                return null;
            }
            min -= Vector2.one * 150f;
            max += Vector2.one * 150f;
            Vector2 size = max - min;
            float mpp = Mathf.Max(MetresPerPixel, Mathf.Max(size.x, size.y) / MaxPixels);
            int w = Mathf.CeilToInt(size.x / mpp), h = Mathf.CeilToInt(size.y / mpp);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = PlayArea.Contains(new Vector3(min.x + (x + 0.5f) * mpp, 0f, min.y + (y + 0.5f) * mpp)) ? Ground : Outside;

            Vector2 P(float wx, float wz) => new Vector2((wx - min.x) / mpp, (wz - min.y) / mpp);

            foreach (var b in buildings)
                if (b.pts != null && b.pts.Length >= 6)
                {
                    var poly = new List<Vector2>(b.pts.Length / 2);
                    for (int i = 0; i + 1 < b.pts.Length; i += 2)
                        poly.Add(P(b.pts[i], b.pts[i + 1]));
                    FillPolygon(px, w, h, poly, Building);
                }

            // Minor roads first, main roads on top.
            for (int pass = 0; pass < 2; pass++)
                foreach (var r in roads)
                {
                    if (r.pts == null || r.pts.Length < 6 || r.highway == "footway" || r.highway == "path" || r.highway == "steps")
                        continue;
                    bool main = PcRoadBuilder.Major.Contains(r.highway);
                    if (main != (pass == 1))
                        continue;
                    float radius = (main ? 6f : 3.5f) / mpp;
                    for (int i = 3; i + 2 < r.pts.Length; i += 3)
                        ThickLine(px, w, h, P(r.pts[i - 3], r.pts[i - 1]), P(r.pts[i], r.pts[i + 2]), radius, main ? MainRoad : MinorRoad);
                }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            string path = $"{outDir}/CityMap.png";
            File.WriteAllBytes(Path.GetFullPath(path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                imp.textureType = TextureImporterType.Default;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.mipmapEnabled = true;
                imp.maxTextureSize = 4096;
                imp.textureCompression = TextureImporterCompression.Compressed;
                imp.SaveAndReimport();
            }
            var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            var go = new GameObject("City map (satnav)");
            go.transform.SetParent(root, false);
            var map = go.AddComponent<CityMap>();
            map.Configure(asset, min, size);
            Debug.Log($"[Map] City map {w} x {h} px ({mpp:0.0} m/px) over {size.x / 1000f:0.0} x {size.y / 1000f:0.0} km.");
            return map;
        }

        static void ThickLine(Color32[] px, int w, int h, Vector2 a, Vector2 b, float radius, Color32 c)
        {
            float len = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / Mathf.Max(0.5f, radius * 0.5f)));
            for (int s = 0; s <= steps; s++)
                Disc(px, w, h, Vector2.Lerp(a, b, s / (float)steps), radius, c);
        }

        static void Disc(Color32[] px, int w, int h, Vector2 p, float r, Color32 c)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(p.x - r)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(p.x + r));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(p.y - r)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(p.y + r));
            float r2 = r * r;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - p.x, dy = y + 0.5f - p.y;
                    if (dx * dx + dy * dy <= r2)
                        px[y * w + x] = c;
                }
        }

        // Even-odd scanline fill.
        static void FillPolygon(Color32[] px, int w, int h, List<Vector2> poly, Color32 c)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in poly)
            {
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(maxY));
            var xs = new List<float>();
            for (int y = y0; y <= y1; y++)
            {
                float sy = y + 0.5f;
                xs.Clear();
                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                {
                    Vector2 a = poly[i], b = poly[j];
                    if ((a.y > sy) != (b.y > sy))
                        xs.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int xa = Mathf.Max(0, Mathf.CeilToInt(xs[k] - 0.5f)), xb = Mathf.Min(w - 1, Mathf.FloorToInt(xs[k + 1] - 0.5f));
                    for (int x = xa; x <= xb; x++)
                        px[y * w + x] = c;
                }
            }
        }
    }
}
