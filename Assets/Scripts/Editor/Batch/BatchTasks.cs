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

        // Objects standing in the carriageway: list them (Temp/clear_roads.txt), or remove them and keep the layers.
        public static void ClearRoadsReport() => Run(KR.Editor.Kigali.ClearRoads.Report);
        public static void ClearRoads() => Run(KR.Editor.Kigali.ClearRoads.Apply);

        // Where streets have steps, gaps, floating edges or impossible grades (Temp/road_report.txt).
        public static void RoadReport() => Run(KR.Editor.Kigali.RoadReport.Run);

        // Windows player of the Story Mode scene (PC only), written to <project>/Builds/StoryPC.
        public static void BuildStoryWindows() => Run(() =>
        {
            UnityEditor.PlayerSettings.productName = QuestBoot.AppName;
            string exe = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Builds", "StoryPC", "KigaliRushStory.exe"));
            var report = UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions
            {
                // Same start as the headset: the KR loading card, the main menu, then the city.
                scenes = QuestBoot.Scenes,
                locationPathName = exe,
                target = UnityEditor.BuildTarget.StandaloneWindows64,
                options = UnityEditor.BuildOptions.None,
            });
            var sum = report.summary;
            Debug.Log($"[Build] {sum.result}: {exe}, {sum.totalSize / (1024f * 1024f):0} MB, {sum.totalErrors} errors, {sum.totalTime.TotalMinutes:0.0} min");
            if (sum.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                Debug.LogError("[Build] Windows build failed.");
        });

        // Meta Quest APK of the Story Mode scene (Builds/Quest); install with adb install -r.
        public static void BuildStoryQuest() => Run(() => QuestBuild.Build(false));

        // Preview renders of the ALU campus in the Story scene (-captureDir <folder>).
        public static void CaptureAluCampus() => Run(() =>
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-captureDir");
            AluCampus.Capture(StoryModeBuilder.ScenePath, i >= 0 && i + 1 < args.Length ? args[i + 1] : "Temp/AluCaptures");
        });

        // Renders of the player's car (-captureDir <folder>): outside, and the driver's view from the eye point,
        // to check the car's orientation, seat height and the view over the bonnet without a headset.
        // Opens the Story scene in the Editor and presses Play (desktop mode when no headset is connected).
        // Command line, without -batchmode: Unity -projectPath <KR> -executeMethod KR.Editor.Batch.BatchTasks.PlayStory
        public static void PlayStory()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(StoryModeBuilder.ScenePath);
            EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }

        // The restaurant dining room (-captureDir <folder>): from the door, and from your seat at a table.
        public static void CaptureRestaurant() => Run(() =>
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-captureDir");
            string folder = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Logs/Restaurant";
            System.IO.Directory.CreateDirectory(folder);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(StoryModeBuilder.ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            Transform room = null;
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name.StartsWith("Restaurant dining room"))
                    room = t;
            var seat = room != null ? room.Find("Your seat") : null;
            if (room == null || seat == null)
            {
                Debug.LogError("[Capture] No restaurant room or seat.");
                return;
            }
            var cam = new GameObject("Capture").AddComponent<Camera>();
            cam.fieldOfView = 70f;
            var shots = new[]
            {
                ("1 From the door", room.position + new Vector3(0f, 1.62f, -4.6f), Quaternion.Euler(8f, 0f, 0f)),
                ("2 Seated at a table", seat.position + Vector3.up * 1.17f, seat.rotation * Quaternion.Euler(18f, 0f, 0f)),
            };
            var rt = new RenderTexture(1600, 900, 24);
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            foreach (var (name, pos, rot) in shots)
            {
                cam.transform.SetPositionAndRotation(pos, rot);
                cam.Render();
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".png"), tex.EncodeToPNG());
            }
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
        });

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
            // The instrument cluster (sample content) and the working mirrors, as they look while driving.
            var cluster = KR.Gameplay.Story.CarCluster.Create(car, null, null);
            cluster.Preview();
            Canvas.ForceUpdateCanvases();
            var mirrors = KR.Gameplay.Player.CarMirrors.Attach(car);
            if (mirrors != null)
            {
                mirrors.ShowForCapture(eye);   // the first frame can use the Editor's placeholder shaders
                mirrors.ShowForCapture(eye);
                var view = new Texture2D(mirrors.View.width, mirrors.View.height, TextureFormat.RGB24, false);
                RenderTexture.active = mirrors.View;
                view.ReadPixels(new Rect(0, 0, view.width, view.height), 0, 0);
                RenderTexture.active = null;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, "7 Mirror camera view.png"), view.EncodeToPNG());
                var mc = mirrors.ViewCamera.transform;
                Debug.Log($"[Capture] Mirror camera at {t.InverseTransformPoint(mc.position):F2} (car-local), facing {t.InverseTransformDirection(mc.forward):F2}.");
            }
            Vector3 Look(Vector3 at) => at - eye;
            var parts = KR.Gameplay.Player.CarGeometry.Mirrors;
            var shots = new System.Collections.Generic.List<(string name, Vector3 pos, Quaternion rot, float fov)>
            {
                ("1 Outside front-left", t.position + t.forward * 5.5f - t.right * 4f + Vector3.up * 1.6f, Quaternion.LookRotation(t.position + Vector3.up * 0.7f - (t.position + t.forward * 5.5f - t.right * 4f + Vector3.up * 1.6f)), 50f),
                ("2 Outside rear-right", t.position - t.forward * 6f + t.right * 3.5f + Vector3.up * 2f, Quaternion.LookRotation(t.position + Vector3.up * 0.7f - (t.position - t.forward * 6f + t.right * 3.5f + Vector3.up * 2f)), 50f),
                ("3 Driver view ahead", eye, Quaternion.LookRotation(t.forward, Vector3.up) * Quaternion.Euler(6f, 0f, 0f), 90f),
                ("4 Driver view down to wheel", eye, Quaternion.LookRotation(t.forward, Vector3.up) * Quaternion.Euler(35f, 0f, 0f), 90f),
                ("5 Cluster close", eye, Quaternion.LookRotation(Look(cluster.transform.position), t.up), 40f),
            };
            foreach (var g in parts)
                shots.Add(($"6 Mirror {(Mathf.Abs(g.centre.x) < 0.3f ? "rear-view" : g.centre.x < 0f ? "left" : "right")}", eye,
                           Quaternion.LookRotation(Look(t.TransformPoint(g.centre)), t.up), 35f));
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
            Debug.Log($"[Capture] {shots.Count} car renders in {folder}. Eye {eye:F2}, hip {(car.HipPoint != null ? car.HipPoint.position.ToString("F2") : "none")}, " +
                      $"grips {(car.LeftGrip != null ? car.LeftGrip.position.ToString("F2") : "none")} / {(car.RightGrip != null ? car.RightGrip.position.ToString("F2") : "none")}.");
        });

        // Stills for the main menu (-captureDir <folder>): a few views of places in each sector and of the car,
        // rendered from the Story scene. Pick the best and copy them to Assets/Resources/UI/Stills.
        public static void CaptureMenuStills() => Run(() =>
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-captureDir");
            string folder = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Temp/MenuStills";
            System.IO.Directory.CreateDirectory(folder);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(StoryModeBuilder.ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            var cam = Camera.main;
            var places = UnityEngine.Object.FindFirstObjectByType<KR.Gameplay.Story.Discoveries>(FindObjectsInactive.Include);
            if (cam == null || places == null || places.Catalog == null)
            {
                Debug.LogError("[Stills] No camera or place catalog in the Story scene.");
                return;
            }
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 3000f);
            cam.fieldOfView = 55f;
            var shots = new System.Collections.Generic.List<(string, Vector3, Vector3)>();
            float Ground(Vector3 p) => Physics.Raycast(p + Vector3.up * 300f, Vector3.down, out var hit, 600f) ? hit.point.y : p.y;
            foreach (var sector in new[] { "Kimironko", "Kibagabaga", "ALU campus" })
            {
                int n = 0;
                foreach (var p in places.Catalog.places)
                {
                    if (p.sector != sector || n >= 3)
                        continue;
                    n++;
                    for (int a = 0; a < 3; a++)
                    {
                        var dir = Quaternion.Euler(0f, 40f + a * 120f, 0f) * Vector3.forward;
                        var pos = p.position + dir * 26f;
                        pos.y = Ground(pos) + 3.5f + a * 3f;
                        var look = p.position;
                        look.y = Ground(p.position) + 3f;
                        shots.Add(($"{sector} {n} {p.name} {a}", pos, look));
                    }
                }
            }
            var car = UnityEngine.Object.FindFirstObjectByType<KR.Gameplay.Player.DrivableCar>(FindObjectsInactive.Include);
            if (car != null)
            {
                var t = car.transform;
                shots.Add(("Car front", t.position + t.forward * 6f - t.right * 3.5f + Vector3.up * 1.2f, t.position + Vector3.up * 0.8f));
                shots.Add(("Car street", t.position - t.forward * 9f + t.right * 2f + Vector3.up * 2.2f, t.position + t.forward * 20f + Vector3.up * 2f));
            }
            const int w = 1600, h = 900;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            foreach (var (name, pos, look) in shots)
            {
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                string file = string.Concat(name.Split(System.IO.Path.GetInvalidFileNameChars()));
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, file + ".png"), tex.EncodeToPNG());
            }
            cam.targetTexture = null;
            RenderTexture.active = null;
            Debug.Log($"[Stills] {shots.Count} stills in {folder}.");
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
