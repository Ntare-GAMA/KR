using System.Collections.Generic;
using System.IO;
using System.Text;
using KR.Gameplay.Boot;
using KR.Gameplay.UI;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace KR.Editor.Batch
{
    // Headset build setup: the app name "KR" and its icon; the scenes in order (loading card, main menu, the
    // city); the credits list for the menu, made from Documentation/asset-register.csv; the Rajdhani font's
    // fallbacks; and Android texture limits for the sky panoramas (16k sources).
    public static class QuestBoot
    {
        public const string BootPath = "Assets/Scenes/Quest Boot.unity";
        public const string MenuPath = "Assets/Scenes/Quest Menu.unity";
        public const string IconPath = "Assets/Resources/UI/kr_icon.png";
        const string CreditsPath = "Assets/Resources/UI/credits.txt";
        const string FontFolder = "Assets/Resources/UI/Fonts";
        public const string AppName = "KR";

        public static string[] Scenes => new[] { BootPath, MenuPath, Kigali.StoryModeBuilder.ScenePath };

        public static void Prepare()
        {
            PlayerSettings.productName = AppName;
            Icon();
            Fonts();
            Skies();
            Credits();
            MakeBoot();
            MakeMenu();
            var list = new List<EditorBuildSettingsScene>();
            foreach (var s in Scenes)
                list.Add(new EditorBuildSettingsScene(s, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static void Icon()
        {
            if (AssetImporter.GetAtPath(IconPath) is TextureImporter ti)
            {
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.isReadable = true;
                ti.maxTextureSize = 1024;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                Debug.LogWarning($"[Quest] No icon at {IconPath}.");
                return;
            }
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            foreach (var kind in new[] { UnityEditor.Android.AndroidPlatformIconKind.Legacy, UnityEditor.Android.AndroidPlatformIconKind.Round })
            {
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (var i in icons)
                    i.SetTexture(icon);
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }
        }

        // Rajdhani has no symbols such as ✔ ★ →: let the system fonts draw those.
        static void Fonts()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Font", new[] { FontFolder }))
            {
                if (AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is not TrueTypeFontImporter fi)
                    continue;
                fi.includeFontData = true;
                fi.fontNames = new[] { "Rajdhani", "Roboto", "Noto Sans", "Noto Sans Symbols", "Noto Sans Symbols 2", "Arial" };
                fi.SaveAndReimport();
            }
        }

        static void Skies()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/AllSkyFree" }))
            {
                if (AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is not TextureImporter ti)
                    continue;
                var s = ti.GetPlatformTextureSettings("Android");
                if (s.overridden && s.maxTextureSize == 2048 && s.format == TextureImporterFormat.ASTC_6x6)
                    continue;
                s.overridden = true;
                s.maxTextureSize = 2048;
                s.format = TextureImporterFormat.ASTC_6x6;
                ti.SetPlatformTextureSettings(s);
                ti.SaveAndReimport();
            }
        }

        // One line per asset from the register: what it is, who made it, the licence (and its attribution text).
        static void Credits()
        {
            string csvPath = Path.Combine(Application.dataPath, "..", "Documentation", "asset-register.csv");
            var sb = new StringBuilder("KR – Kigali Rush. Made with Unity.\nMap data © OpenStreetMap contributors (ODbL).\n\n");
            if (File.Exists(csvPath))
            {
                var rows = ParseCsv(File.ReadAllText(csvPath));
                if (rows.Count > 0)
                {
                    var head = rows[0];
                    int Col(string n) => head.IndexOf(n);
                    int name = Col("name"), author = Col("author"), licence = Col("licence"), attr = Col("attribution_text");
                    var seen = new HashSet<string>();
                    for (int r = 1; r < rows.Count; r++)
                    {
                        var row = rows[r];
                        string Get(int c) => c >= 0 && c < row.Count ? row[c].Trim() : "";
                        if (Get(name).Length == 0)
                            continue;
                        // Register notes ("TODO: verify ...") are for us, not for the credits screen.
                        string Clean(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\s*\(?TODO[^)]*\)?", "").Trim(' ', ';', ',');
                        string by = Clean(Get(author));
                        string what = Clean(Get(attr));
                        if (what.Length == 0)
                            what = by.Length > 0 ? $"{Get(name)} by {by}" : Get(name);
                        string lic = Clean(Get(licence));
                        if (lic.Length == 0)
                            lic = "licence being checked";
                        string line = $"• {what}  <color=#beacb2>({lic})</color>\n";
                        if (seen.Add(line))
                            sb.Append(line);
                    }
                }
            }
            File.WriteAllText(Path.Combine(Application.dataPath, "..", CreditsPath), sb.ToString());
            AssetDatabase.ImportAsset(CreditsPath);
        }

        static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cell.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else cell.Append(c);
            }
            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }
            return rows;
        }

        static Camera HeadCamera(Color background, bool position)
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.nearClipPlane = 0.05f;
            camGo.transform.position = new Vector3(0f, 1.6f, 0f);
            var pose = camGo.AddComponent<TrackedPoseDriver>();
            pose.positionAction = new InputAction("Head position", binding: "<XRHMD>/centerEyePosition");
            pose.rotationAction = new InputAction("Head rotation", binding: "<XRHMD>/centerEyeRotation");
            pose.trackingType = position ? TrackedPoseDriver.TrackingType.RotationAndPosition : TrackedPoseDriver.TrackingType.RotationOnly;
            return cam;
        }

        static void MakeBoot()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            HeadCamera(new Color(0.02f, 0.01f, 0.015f), false);
            var loader = new GameObject("Boot loader").AddComponent<BootLoader>();
            loader.Configure(1);
            EditorSceneManager.SaveScene(scene, BootPath);
        }

        static void MakeMenu()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientLight = Color.white;
            HeadCamera(new Color(0.05f, 0.01f, 0.02f), true);
            new GameObject("Main menu").AddComponent<MainMenu>();
            EditorSceneManager.SaveScene(scene, MenuPath);
        }
    }
}
