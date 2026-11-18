using KR.Editor.Util;
using UnityEditor;
using UnityEngine;

namespace KR.Editor.Kigali
{
    // Photo-scanned CC0 materials from Poly Haven (downloaded by Tools/polyhaven_fetch.py).
    internal static class PcMaterials
    {
        const string Root = "Assets/World/PC/PolyHaven";

        public static Texture2D Diffuse(string id) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/{id}/{id}_diff.jpg");

        public static Texture2D Normal(string id)
        {
            string path = $"{Root}/{id}/{id}_nor.jpg";
            if (AssetImporter.GetAtPath(path) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static bool Has(string id) => Diffuse(id) != null;

        // URP Lit with photo albedo + normal map, tiling in metres (meshes use metre UVs).
        public static Material Photo(string outDir, string id, float tileMetres, float smoothness = 0.15f, Color? tint = null)
        {
            var diffuse = Diffuse(id);
            if (diffuse == null)
                Debug.LogWarning($"[PcMaterials] Missing Poly Haven texture '{id}'. Run Tools/polyhaven_fetch.py.");
            var mat = EditorAssetUtil.LitMaterial(outDir, $"PH_{id}", tint ?? Color.white, smoothness, diffuse, tileMetres);
            var normal = Normal(id);
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        public static TerrainLayer Layer(string outDir, string id, float tileMetres)
        {
            string path = $"{outDir}/TL_{id}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = Diffuse(id);
            layer.normalMapTexture = Normal(id);
            layer.normalScale = 1f;
            layer.tileSize = new Vector2(tileMetres, tileMetres);
            layer.smoothnessSource = TerrainLayerSmoothnessSource.Constant;   // not the diffuse alpha (mirror)
            layer.smoothness = 0.05f;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        // Plain URP Lit with emission enabled, for parts whose glow is driven at runtime by property blocks.
        public static Material Emissive(string outDir, string name, Color baseColor, Color emission, float smoothness = 0.5f)
        {
            var mat = EditorAssetUtil.LitMaterial(outDir, name, baseColor, smoothness);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
