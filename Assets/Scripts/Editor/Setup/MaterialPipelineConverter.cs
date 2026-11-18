using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KR.Editor.Setup
{
    // Converts Built-in Standard / Legacy / Mobile / Unlit materials to URP Lit or Unlit in chosen folders.
    // Leaves URP, Shader Graph and Skybox materials untouched.
    public static class MaterialPipelineConverter
    {
        public static int ConvertFolders(params string[] folders)
        {
            var valid = System.Array.FindAll(folders, AssetDatabase.IsValidFolder);
            if (valid.Length == 0)
                return 0;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            int converted = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", valid))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat == null || mat.shader == null)
                    continue;
                string s = mat.shader.name;
                if (s.StartsWith("Universal Render Pipeline") || s.StartsWith("Shader Graphs") || s.StartsWith("Skybox"))
                    continue;
                if (s.StartsWith("Unlit/"))
                    ToUnlit(mat, unlit);
                else if (s.StartsWith("Standard") || s.StartsWith("Legacy Shaders") || s.StartsWith("Mobile") ||
                         s.StartsWith("Nature") || s == "Hidden/InternalErrorShader")
                    ToLit(mat, lit, s);
                else
                {
                    Debug.LogWarning($"[MaterialConverter] Skipped {mat.name}: unknown shader '{s}'", mat);
                    continue;
                }
                EditorUtility.SetDirty(mat);
                converted++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[MaterialConverter] Converted {converted} materials in {string.Join(", ", valid)}");
            return converted;
        }

        static void ToLit(Material m, Shader lit, string oldShader)
        {
            var mainTex = Tex(m, "_MainTex");
            Vector2 scale = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offset = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;
            Color color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            var bump = Tex(m, "_BumpMap");
            float bumpScale = Float(m, "_BumpScale", 1f);
            var metalMap = Tex(m, "_MetallicGlossMap");
            float metallic = Float(m, "_Metallic", 0f);
            float gloss = Float(m, "_Glossiness", 0.3f);
            float glossScale = Float(m, "_GlossMapScale", 1f);
            var occlusion = Tex(m, "_OcclusionMap");
            float occlusionStrength = Float(m, "_OcclusionStrength", 1f);
            var emissionMap = Tex(m, "_EmissionMap");
            Color emissionColor = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            bool emission = m.IsKeywordEnabled("_EMISSION");
            float cutoff = Float(m, "_Cutoff", 0.5f);
            float mode = Float(m, "_Mode", 0f);
            if (oldShader.Contains("Cutout")) mode = 1f;
            else if (oldShader.Contains("Transparent")) mode = 2f;

            m.shader = lit;
            m.shaderKeywords = new string[0];
            m.SetTexture("_BaseMap", mainTex);
            m.SetTextureScale("_BaseMap", scale);
            m.SetTextureOffset("_BaseMap", offset);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", metalMap != null ? glossScale : gloss);
            if (bump != null)
            {
                m.SetTexture("_BumpMap", bump);
                m.SetFloat("_BumpScale", bumpScale);
                m.EnableKeyword("_NORMALMAP");
            }
            if (metalMap != null)
            {
                m.SetTexture("_MetallicGlossMap", metalMap);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            if (occlusion != null)
            {
                m.SetTexture("_OcclusionMap", occlusion);
                m.SetFloat("_OcclusionStrength", occlusionStrength);
                m.EnableKeyword("_OCCLUSIONMAP");
            }
            if (emission)
            {
                m.SetTexture("_EmissionMap", emissionMap);
                m.SetColor("_EmissionColor", emissionColor);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            SetSurface(m, mode, cutoff);
        }

        static void ToUnlit(Material m, Shader unlit)
        {
            var mainTex = Tex(m, "_MainTex");
            Color color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            bool transparent = m.shader.name.Contains("Transparent");
            m.shader = unlit;
            m.shaderKeywords = new string[0];
            m.SetTexture("_BaseMap", mainTex);
            m.SetColor("_BaseColor", color);
            SetSurface(m, transparent ? 2f : 0f, 0.5f);
        }

        // mode follows the Standard shader: 0 opaque, 1 cutout, 2 fade, 3 transparent.
        static void SetSurface(Material m, float mode, float cutoff)
        {
            if (mode >= 2f)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_AlphaClip", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetShaderPassEnabled("DepthOnly", false);
                m.SetShaderPassEnabled("ShadowCaster", false);
                return;
            }

            m.SetFloat("_Surface", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.Zero);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
            m.SetFloat("_ZWrite", 1f);
            m.SetShaderPassEnabled("DepthOnly", true);
            m.SetShaderPassEnabled("ShadowCaster", true);
            if (mode >= 1f)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", cutoff);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                m.SetFloat("_AlphaClip", 0f);
                m.SetOverrideTag("RenderType", "Opaque");
                m.renderQueue = -1;
            }
        }

        static Texture Tex(Material m, string prop) => m.HasProperty(prop) ? m.GetTexture(prop) : null;
        static float Float(Material m, string prop, float fallback) => m.HasProperty(prop) ? m.GetFloat(prop) : fallback;
    }
}
