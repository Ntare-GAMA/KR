using System.IO;
using System.Reflection;
using KR.Gameplay.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KR.Editor.Batch
{
    // Screenshots of the main menu without a headset (-captureDir <folder>): builds the Quest scenes, plays the
    // menu scene and saves the start screen, another tile, and the ••• options.
    // Unity -batchmode -projectPath <KR> -executeMethod KR.Editor.Batch.MenuPreview.Capture -captureDir <folder>
    [InitializeOnLoad]
    public static class MenuPreview
    {
        const string Key = "KR.MenuPreview.Dir";
        static int _frames;

        static MenuPreview()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(Key, "")))
                EditorApplication.update += Tick;   // survives the domain reload when Play starts
        }

        public static void Capture()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-captureDir");
            string dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Temp/MenuPreview";
            Directory.CreateDirectory(dir);
            QuestBoot.Prepare();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(QuestBoot.MenuPath);
            SessionState.SetString(Key, dir);
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying)
                return;
            _frames++;
            var menu = Object.FindFirstObjectByType<MainMenu>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            if (_frames == 90)
                Shot("1 Start");
            if (_frames == 95 && menu != null)
                typeof(MainMenu).GetMethod("Select", flags)?.Invoke(menu, new object[] { 3 });
            if (_frames == 110)
                Shot("2 ALU tile");
            if (_frames == 115 && menu != null)
            {
                var modal = typeof(MainMenu).GetNestedType("Modal", BindingFlags.NonPublic);
                typeof(MainMenu).GetMethod("OpenModal", flags)?.Invoke(menu, new[] { System.Enum.Parse(modal, "Settings") });
            }
            if (_frames == 130)
            {
                Shot("3 Settings");
                SessionState.EraseString(Key);
                EditorApplication.update -= Tick;
                EditorApplication.Exit(0);
            }
        }

        static void Shot(string name)
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            const int w = 1920, h = 1080;
            cam.fieldOfView = 52f;
            var rt = new RenderTexture(w, h, 24);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(SessionState.GetString(Key, "Temp"), name + ".png"), tex.EncodeToPNG());
        }
    }
}
