using System.Collections.Generic;
using System.IO;
using System.Linq;
using KR.Gameplay.Player;
using KR.Gameplay.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KR.Editor.Batch
{
    // Renders traffic in Play mode (where it actually drives and its lamps exist), to check by eye that cars sit on the
    // road and their lamps sit on the cars, by day and by night. Not in -batchmode (Play needs the editor loop):
    //   Unity -projectPath <KR> -executeMethod KR.Editor.Batch.PlayCapture.Traffic -captureDir <folder>
    // Writes <folder>/traffic_*.png and quits.
    [InitializeOnLoad]
    public static class PlayCapture
    {
        const string Key = "kr.playcapture.dir";
        static double _next;
        static int _step;
        static readonly List<int> Picked = new List<int>();

        static PlayCapture()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(Key, "")))
                EditorApplication.update += Tick;
        }

        // Goes into a spread of the city's buildings and renders each room from the doorway (interiors_*.png).
        public static void Interiors()
        {
            SessionState.SetString(ModeKey, "interiors");
            Traffic();
        }

        const string ModeKey = "kr.playcapture.mode";

        public static void Traffic()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-captureDir");
            string dir = Path.GetFullPath(i >= 0 && i + 1 < args.Length ? args[i + 1] : "Logs/PlayCapture");
            Directory.CreateDirectory(dir);
            SessionState.SetString(Key, dir);
            EditorSceneManager.OpenScene(KR.Editor.Kigali.StoryModeBuilder.ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (!EditorApplication.isPlaying)
            {
                // Press Play once the editor has finished starting up.
                if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
                {
                    if (_next == 0)
                        _next = now + 5;
                    else if (now >= _next)
                    {
                        _next = 0;
                        EditorApplication.isPlaying = true;
                    }
                }
                return;
            }
            if (_next == 0)
                _next = now + 12;   // let the city settle
            if (now < _next)
                return;
            string dir = SessionState.GetString(Key, "");
            if (SessionState.GetString(ModeKey, "") == "interiors")
            {
                TickInteriors(dir, now);
                return;
            }
            var traffic = Object.FindObjectsByType<RoadNetworkTraffic>(FindObjectsSortMode.None).FirstOrDefault(t => !t.IsWalkers);
            var rig = Object.FindFirstObjectByType<PlayerRig>();
            if (traffic == null || rig == null || traffic.Count == 0)
            {
                Debug.LogError("[PlayCapture] No traffic or player.");
                Quit();
                return;
            }
            if (Picked.Count == 0)
                for (int k = 0; k < 8; k++)
                    Picked.Add((k * 37 + 5) % traffic.Count);

            // Steps: for each car (day, then night) move there, wait, render.
            int shotsPerPass = Picked.Count;
            int pass = _step / (shotsPerPass * 2);
            int within = _step % (shotsPerPass * 2);
            if (pass >= 2)
            {
                Debug.Log($"[PlayCapture] Done: {dir}");
                Quit();
                return;
            }
            var cycle = DayNightCycle.Instance;
            if (cycle != null)
                cycle.Hour = pass == 0 ? 12.5f : 21.5f;
            int car = Picked[within / 2];
            var body = traffic.Body(car);
            if (body == null)
            {
                _step += 2 - within % 2;
                return;
            }
            if (within % 2 == 0)
            {
                // Stand beside it so it is "near the camera" (traffic follows the real surface near you).
                var p = body.position + body.right * 12f;
                rig.Player.Teleport(p, 0f);
                _next = now + 2.5;
            }
            else
            {
                Render(dir, $"traffic_{(pass == 0 ? "day" : "night")}_{car}", body);
                _next = now + 0.2;
            }
            _step++;
        }

        static void TickInteriors(string dir, double now)
        {
            var bi = Object.FindFirstObjectByType<KR.Gameplay.Story.BuildingInteriors>();
            if (bi == null || bi.Count == 0)
            {
                Debug.LogError("[PlayCapture] No enterable buildings.");
                Quit();
                return;
            }
            const int shots = 8;
            int k = _step / 2;
            if (k >= shots)
            {
                bi.ForceLeave();
                Debug.Log($"[PlayCapture] Interiors done: {dir}");
                Quit();
                return;
            }
            int index = (k * 7919 + 101) % bi.Count;
            if (_step % 2 == 0)
            {
                Debug.Log($"[PlayCapture] Enter building {index}: {bi.TryEnter(index)}");
                _next = now + 1.5;
            }
            else
            {
                var main = Camera.main;
                if (main != null)
                {
                    var go = new GameObject("Capture camera");
                    var cam = go.AddComponent<Camera>();
                    cam.CopyFrom(main);
                    cam.stereoTargetEye = StereoTargetEyeMask.None;
                    cam.fieldOfView = 80f;
                    var rt = new RenderTexture(1280, 720, 24);
                    var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                    cam.targetTexture = rt;
                    foreach (var (view, turn) in new[] { ("ahead", 0f), ("back to door", 180f) })
                    {
                        cam.transform.SetPositionAndRotation(main.transform.position, Quaternion.Euler(10f, main.transform.eulerAngles.y + turn, 0f));
                        cam.Render();
                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                        File.WriteAllBytes(Path.Combine(dir, $"interiors_{k}_{view}.png"), tex.EncodeToPNG());
                    }
                    RenderTexture.active = null;
                    Object.DestroyImmediate(go);
                    Object.DestroyImmediate(rt);
                }
                _next = now + 0.3;
            }
            _step++;
        }

        static void Render(string dir, string name, Transform body)
        {
            var go = new GameObject("Capture camera");
            var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main != null)
                cam.CopyFrom(main);
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.fieldOfView = 50f;
            const int w = 1280, h = 720;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            var views = new[]
            {
                ("front", body.position + body.forward * 8f + body.right * 2.5f + Vector3.up * 1.4f),
                ("side", body.position + body.right * 7f + Vector3.up * 0.6f),
                ("back", body.position - body.forward * 8f - body.right * 2f + Vector3.up * 1.6f),
            };
            foreach (var (view, at) in views)
            {
                cam.transform.SetPositionAndRotation(at, Quaternion.LookRotation(body.position + Vector3.up * 0.7f - at));
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, $"{name}_{view}.png"), tex.EncodeToPNG());
            }
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(rt);
            Debug.Log($"[PlayCapture] {name}: {body.name} at {body.position:F1}");
        }

        static void Quit()
        {
            SessionState.EraseString(Key);
            SessionState.EraseString(ModeKey);
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
    }
}
