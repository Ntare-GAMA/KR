using System;
using KR.Editor.Kigali;
using UnityEditor;
using UnityEngine;

namespace KR.Editor.Batch
{
    // Entry points for command-line runs (Unity -batchmode -executeMethod KR.Editor.Batch.BatchTasks.X).
    // Each exits with 0 on success and 1 if anything logged an error or threw.
    public static class BatchTasks
    {
        // Story Mode base map: Kimironko sector (incl. Kibagabaga cell) and the ALU campus edge, from OSM + Copernicus.
        public static void BuildStoryScene() => Run(() =>
            PcShowcaseBuilder.Build("kimironko_kibagabaga", "Assets/Scenes/PC Only - Story Kimironko Kibagabaga.unity",
                                    "Kimironko, Kibagabaga"));

        // Rush Week content, police posts and Story Mode wiring on top of the Kimironko scene.
        public static void AddStoryMode() => Run(StoryModeBuilder.Build);

        // Windows player of the Story Mode scene (PC only), written to <project>/Builds/StoryPC.
        public static void BuildStoryWindows() => Run(() =>
        {
            string exe = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Builds", "StoryPC", "KigaliRushStory.exe"));
            var report = UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions
            {
                scenes = new[] { StoryModeBuilder.ScenePath },
                locationPathName = exe,
                target = UnityEditor.BuildTarget.StandaloneWindows64,
                options = UnityEditor.BuildOptions.None,
            });
            var sum = report.summary;
            Debug.Log($"[Build] {sum.result}: {exe}, {sum.totalSize / (1024f * 1024f):0} MB, {sum.totalErrors} errors, {sum.totalTime.TotalMinutes:0.0} min");
            if (sum.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                Debug.LogError("[Build] Windows build failed.");
        });

        // Preview renders of the ALU campus in the Story scene (-captureDir <folder>).
        public static void CaptureAluCampus() => Run(() =>
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-captureDir");
            AluCampus.Capture(StoryModeBuilder.ScenePath, i >= 0 && i + 1 < args.Length ? args[i + 1] : "Temp/AluCaptures");
        });

        // Renders of the player's car (-captureDir <folder>): outside, and the driver's view from the eye point,
        // to check the car's orientation, seat height and the view over the bonnet without a headset.
        public static void CaptureCar() => Run(() =>
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-captureDir");
            string folder = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Temp/CarCaptures";
            System.IO.Directory.CreateDirectory(folder);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(StoryModeBuilder.ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            var car = UnityEngine.Object.FindFirstObjectByType<KR.Gameplay.Player.DrivableCar>(FindObjectsInactive.Include);
            var cam = Camera.main;
            if (car == null || cam == null)
            {
                Debug.LogError("[Capture] No car or camera in the Story scene.");
                return;
            }
            var hands = UnityEngine.Object.FindFirstObjectByType<KR.Gameplay.Player.PlayerHands>(FindObjectsInactive.Include);
            if (hands != null)
                hands.PoseOnWheel(car);
            var t = car.transform;
            Vector3 eye = car.EyePoint;
            var shots = new (string name, Vector3 pos, Quaternion rot, float fov)[]
            {
                ("1 Outside front-left", t.position + t.forward * 5.5f - t.right * 4f + Vector3.up * 1.6f, Quaternion.LookRotation(t.position + Vector3.up * 0.7f - (t.position + t.forward * 5.5f - t.right * 4f + Vector3.up * 1.6f)), 50f),
                ("2 Outside rear-right", t.position - t.forward * 6f + t.right * 3.5f + Vector3.up * 2f, Quaternion.LookRotation(t.position + Vector3.up * 0.7f - (t.position - t.forward * 6f + t.right * 3.5f + Vector3.up * 2f)), 50f),
                ("3 Driver view ahead", eye, Quaternion.LookRotation(t.forward, Vector3.up) * Quaternion.Euler(6f, 0f, 0f), 90f),
                ("4 Driver view down to wheel", eye, Quaternion.LookRotation(t.forward, Vector3.up) * Quaternion.Euler(35f, 0f, 0f), 90f),
            };
            cam.nearClipPlane = 0.03f;
            const int w = 1600, h = 900;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            foreach (var s in shots)
            {
                cam.transform.SetPositionAndRotation(s.pos, s.rot);
                cam.fieldOfView = s.fov;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, s.name + ".png"), tex.EncodeToPNG());
            }
            cam.targetTexture = null;
            RenderTexture.active = null;
            Debug.Log($"[Capture] {shots.Length} car renders in {folder}. Eye {eye:F2}, hip {(car.HipPoint != null ? car.HipPoint.position.ToString("F2") : "none")}, " +
                      $"grips {(car.LeftGrip != null ? car.LeftGrip.position.ToString("F2") : "none")} / {(car.RightGrip != null ? car.RightGrip.position.ToString("F2") : "none")}.");
        });

        static void Run(Action task)
        {
            int errors = 0;
            void Count(string message, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    errors++;
            }
            Application.logMessageReceived += Count;
            try
            {
                task();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                errors++;
            }
            finally
            {
                Application.logMessageReceived -= Count;
            }
            Debug.Log($"[Batch] Finished with {errors} error(s).");
            EditorApplication.Exit(errors == 0 ? 0 : 1);
        }
    }
}
