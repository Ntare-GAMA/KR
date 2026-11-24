using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

namespace KR.Editor.Perf
{
    // The screenshots for the GDD's Performance Considerations section, taken from the open Editor (owner 2026-11-24).
    // Menu: Kigali Rush > Performance > Capture GDD screenshots. Leave the mouse alone until it says it is done
    // (about a minute): it selects things, opens windows, enters Play mode, then saves each view as a PNG in
    // DOCS/performance and leaves Play mode.
    //   1 LOD Group on a parked car, 2 texture import settings (Inspector),
    //   4 Profiler (CPU, Timeline) while playing, 5 Game view with Stats, 6 Frame Debugger.
    // Player Settings (Other Settings > IL2CPP, ARM64) is taken by hand: its foldout cannot be opened from a script.
    [InitializeOnLoad]
    public static class GddScreenshots
    {
        const string StepKey = "KR.GddShots.Step";
        static string OutDir => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "DOCS", "performance"));

        static readonly Queue<(float wait, Action act)> Queue = new Queue<(float, Action)>();
        static double _next;

        static GddScreenshots()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            // Back after the domain reload of entering Play mode: carry on with the play-mode shots.
            if (SessionState.GetInt(StepKey, 0) == 2)
                EditorApplication.delayCall += QueuePlayShots;
        }

        [MenuItem("Kigali Rush/Performance/Capture GDD screenshots")]
        static void Start()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[GDD shots] Stop Play mode first, then run this again.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            if (EditorSceneManager.GetActiveScene().path != PerfReport.StoryScene)
                EditorSceneManager.OpenScene(PerfReport.StoryScene, OpenSceneMode.Single);
            Directory.CreateDirectory(OutDir);
            Debug.Log("[GDD shots] Started. Do not touch the mouse or keyboard until it says done.");

            Queue.Clear();
            Then(0.5f, CloseStrayPopups);
            Then(1f, ShowLod);
            Then(2.5f, () => ShotMain("shot1_lod_group.png"));
            Then(0.5f, ShowTexture);
            Then(2f, () => ShotWindow("InspectorWindow", "shot2_texture_import.png"));
            Then(0.5f, () =>
            {
                SessionState.SetInt(StepKey, 2);
                ProfilerDriver.enabled = true;
                EditorApplication.ExecuteMenuItem("Window/Analysis/Profiler");
                EditorApplication.isPlaying = true;
            });
        }

        // After you set them up by hand (Profiler stopped on a frame in Timeline, Stats on, Frame Debugger enabled):
        // brings each window to the front in turn and saves it exactly as it is. Changes nothing.
        [MenuItem("Kigali Rush/Performance/Capture open Profiler, Stats and Frame Debugger")]
        static void CaptureOpen()
        {
            Directory.CreateDirectory(OutDir);
            Queue.Clear();
            Then(0.2f, () => Debug.Log("[GDD shots] Capturing the open windows. Do not touch the mouse for 6 seconds."));
            foreach (var (type, file) in new[]
                     {
                         ("ProfilerWindow", "shot4_profiler_cpu.png"),
                         ("GameView", "shot5_game_view_stats.png"),
                         ("FrameDebuggerWindow", "shot6_frame_debugger.png"),
                     })
            {
                Then(0.3f, () =>
                {
                    var w = Find(type);
                    if (w == null)
                        Debug.LogWarning($"[GDD shots] {type} is not open; skipped {file}.");
                    else
                    {
                        w.Focus();
                        w.Repaint();
                    }
                });
                Then(1.5f, () => ShotWindow(type, file));
            }
            Then(0.2f, () => Debug.Log($"[GDD shots] Done. Screenshots in {OutDir}"));
        }

        static void QueuePlayShots()
        {
            Queue.Clear();
            // Let the city load and the traffic and people start, so the Profiler has real frames.
            Then(25f, () =>
            {
                CloseStrayPopups();
                ProfilerDriver.enabled = true;
                SetGameViewStats(true);
            });
            // Stop recording (the game keeps running) and select a recent game frame, so the Timeline shows a real
            // frame of the city, not an empty Editor frame.
            Then(8f, () =>
            {
                ProfilerDriver.enabled = false;
                var pw = EditorWindow.GetWindow<ProfilerWindow>();
                pw.selectedFrameIndex = Math.Max(ProfilerDriver.firstFrameIndex, ProfilerDriver.lastFrameIndex - 3);
                pw.Focus();
                pw.Repaint();
            });
            Then(2f, () => ShotWindow("ProfilerWindow", "shot4_profiler_cpu.png"));
            Then(0.5f, () =>
            {
                FocusWindow("GameView");
                SetGameViewStats(true);
            });
            Then(2f, () => ShotWindow("GameView", "shot5_game_view_stats.png"));
            Then(0.5f, OpenFrameDebugger);
            Then(4f, () => ShotWindow("FrameDebuggerWindow", "shot6_frame_debugger.png"));
            Then(0.5f, () =>
            {
                SetFrameDebugger(false);
                SetGameViewStats(false);
                SessionState.EraseInt(StepKey);
                EditorApplication.isPlaying = false;
                Debug.Log($"[GDD shots] Done. Screenshots in {OutDir}");
            });
        }

        static void Then(float wait, Action act)
        {
            if (Queue.Count == 0)
                _next = EditorApplication.timeSinceStartup + wait;
            Queue.Enqueue((wait, act));
        }

        static void Tick()
        {
            if (Queue.Count == 0 || EditorApplication.timeSinceStartup < _next)
                return;
            var (_, act) = Queue.Dequeue();
            try { act(); }
            catch (Exception e) { Debug.LogError($"[GDD shots] {e.Message}"); }
            if (Queue.Count > 0)
                _next = EditorApplication.timeSinceStartup + Queue.Peek().wait;
        }

        // A parked car with only its LOD Group open in the Inspector, framed in the Scene view.
        static void ShowLod()
        {
            var lod = UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                          .FirstOrDefault(l => PathOf(l.transform).Contains("Parked cars"))
                      ?? UnityEngine.Object.FindFirstObjectByType<LODGroup>();
            if (lod == null)
            {
                Debug.LogWarning("[GDD shots] No LOD Group in the scene.");
                return;
            }
            foreach (var c in lod.GetComponents<Component>())
                InternalEditorUtility.SetIsInspectorExpanded(c, c is LODGroup);
            Selection.activeGameObject = lod.gameObject;
            EditorGUIUtility.PingObject(lod.gameObject);
            var view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
            view.Focus();
            view.FrameSelected();
            Debug.Log($"[GDD shots] LOD Group: {PathOf(lod.transform)}");
        }

        // A world texture with mipmaps, its import settings in the Inspector.
        static void ShowTexture()
        {
            Texture2D pick = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/World", "Assets/Art" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is TextureImporter ti && ti.mipmapEnabled && ti.textureType == TextureImporterType.Default)
                {
                    pick = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    break;
                }
            }
            if (pick == null)
            {
                Debug.LogWarning("[GDD shots] No world texture with mipmaps found.");
                return;
            }
            Selection.activeObject = pick;
            EditorGUIUtility.PingObject(pick);
            FocusWindow("InspectorWindow");
            Debug.Log($"[GDD shots] Texture: {AssetDatabase.GetAssetPath(pick)}");
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        // Any loaded type with this short name (Unity keeps several editor windows internal).
        static Type AnyType(string name) =>
            AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { return e.Types.Where(x => x != null).ToArray(); }
                    catch { return Array.Empty<Type>(); }
                })
                .FirstOrDefault(t => t.Name == name && typeof(UnityEngine.Object).IsAssignableFrom(t) || t.Name == name && name == "FrameDebuggerUtility");

        static EditorWindow Find(string type)
        {
            var t = AnyType(type);
            return t == null ? null : Resources.FindObjectsOfTypeAll(t).FirstOrDefault() as EditorWindow;
        }

        static void FocusWindow(string type)
        {
            var w = Find(type);
            if (w == null && AnyType(type) is Type t)
                w = EditorWindow.GetWindow(t);
            if (w != null)
            {
                w.Focus();
                w.Repaint();
            }
        }

        static void SetGameViewStats(bool on)
        {
            var t = AnyType("GameView");
            if (t == null)
                return;
            foreach (var w in Resources.FindObjectsOfTypeAll(t))
            {
                bool set = false;
                for (var k = t; k != null && !set; k = k.BaseType)
                    foreach (var f in k.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                        if (f.FieldType == typeof(bool) && f.Name.IndexOf("Stats", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            f.SetValue(w, on);
                            set = true;
                            break;
                        }
                if (!set && on)
                    Debug.LogWarning("[GDD shots] Could not switch on Stats: click Stats in the Game view toolbar.");
                ((EditorWindow)w).Repaint();
            }
        }

        static void OpenFrameDebugger()
        {
            EditorApplication.ExecuteMenuItem("Window/Analysis/Frame Debugger");
            if (!SetFrameDebugger(true))
                Debug.LogWarning("[GDD shots] Could not switch the Frame Debugger on: press Enable in its window and take this one by hand.");
            FocusWindow("FrameDebuggerWindow");
        }

        // FrameDebuggerUtility.SetEnabled(bool remote, int playerId) is internal; found by name.
        static bool SetFrameDebugger(bool on)
        {
            var t = AnyType("FrameDebuggerUtility");
            var m = t?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(x => x.Name == "SetEnabled" && x.GetParameters().Length == 2);
            if (m == null)
                return false;
            m.Invoke(null, new object[] { on, 0 });
            return true;
        }

        // Third-party installer or welcome pop-ups, and Project Settings, which would cover the shots.
        static void CloseStrayPopups()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                var title = w.titleContent != null ? w.titleContent.text : "";
                if (title.IndexOf("Installer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("Welcome", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    w.GetType().Name == "SettingsWindow")
                    w.Close();
            }
        }

        static void ShotMain(string file) => Capture(EditorGUIUtility.GetMainWindowPosition(), file);

        static void ShotWindow(string type, string file)
        {
            var w = Find(type);
            if (w == null)
            {
                Debug.LogWarning($"[GDD shots] {type} is not open; skipped {file}.");
                return;
            }
            Capture(w.position, file);
        }

        // Reads the pixels on screen inside `rect` (Editor points) and writes a PNG.
        static void Capture(Rect rect, string file)
        {
            float s = EditorGUIUtility.pixelsPerPoint;
            int w = Mathf.RoundToInt(rect.width * s), h = Mathf.RoundToInt(rect.height * s);
            var pixels = InternalEditorUtility.ReadScreenPixel(rect.position * s, w, h);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.SetPixels(pixels);
            tex.Apply();
            var path = System.IO.Path.Combine(OutDir, file);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log($"[GDD shots] Saved {path} ({w} x {h})");
        }
    }
}
