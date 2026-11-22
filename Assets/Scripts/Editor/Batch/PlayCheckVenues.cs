using System.Collections.Generic;
using System.IO;
using System.Linq;
using KR.Gameplay.Player;
using KR.Gameplay.Story;
using KR.Gameplay.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KR.Editor.Batch
{
    // Play-mode check (no headset): measures every person in the city (feet below the ground, or crouched), then
    // goes into a bank, a clinic, a hotel and a pharmacy, photographs each, runs the bank's ticket call, and writes
    // everything to the log and -captureDir. Run without -batchmode:
    //   Unity -projectPath . -executeMethod KR.Editor.Batch.PlayCheckVenues.Run -captureDir Logs/Venues
    [InitializeOnLoad]
    public static class PlayCheckVenues
    {
        const string Key = "kr.playcheck.venues";
        static double _next, _waitedSince;
        static int _step;
        static readonly List<(VenueKind kind, string name, Vector3 pos)> Targets = new List<(VenueKind, string, Vector3)>();

        static PlayCheckVenues()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(Key, "")))
                EditorApplication.update += Tick;
        }

        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-captureDir");
            string dir = Path.GetFullPath(i >= 0 && i + 1 < args.Length ? args[i + 1] : "Logs/Venues");
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
                _next = now + 15;
            if (now < _next)
                return;
            string dir = SessionState.GetString(Key, "");
            var bi = Object.FindFirstObjectByType<BuildingInteriors>();
            var rig = Object.FindFirstObjectByType<PlayerRig>();
            var nav = Object.FindFirstObjectByType<Navigator>();
            if (bi == null || rig == null || nav == null)
            {
                Debug.LogError("[VenueCheck] Missing interiors, player or navigator.");
                Quit();
                return;
            }
            if (_step == 0)
            {
                People(dir);
                foreach (var want in new[] { VenueKind.Bank, VenueKind.Clinic, VenueKind.Hotel, VenueKind.Pharmacy })
                    for (int k = 0; k < nav.Places.Count; k++)
                        if (Venue.Classify(nav.Places[k].name, nav.GroupOf(k)) == want && bi.BuildingNear(nav.Places[k].pos, 25f) >= 0)
                        {
                            Targets.Add((want, nav.Places[k].name, nav.Places[k].pos));
                            break;
                        }
                Debug.Log($"[VenueCheck] Targets: {string.Join(", ", Targets.Select(t => $"{t.kind} '{t.name}'"))}");
                _step = 1;
                _next = now + 1;
                return;
            }
            int v = (_step - 1) / 3, phase = (_step - 1) % 3;
            if (v >= Targets.Count)
            {
                bi.ForceLeave();
                Debug.Log("[VenueCheck] Done.");
                Quit();
                return;
            }
            var t = Targets[v];
            if (phase == 0)
            {
                bool ok = bi.TryEnter(bi.BuildingNear(t.pos, 25f));
                Debug.Log($"[VenueCheck] Enter {t.kind} '{t.name}': {ok}");
                _next = now + 3;
            }
            else if (phase == 1)
            {
                var life = Object.FindFirstObjectByType<VenueLife>();
                var cam0 = Camera.main;
                Debug.Log($"[VenueCheck] {t.kind}: inside {bi.IsInside}, player {rig.Player.transform.position:F1}, camera {(cam0 != null ? cam0.name + " " + cam0.transform.position.ToString("F1") : "none")}, place {t.pos:F0}");
                Debug.Log($"[VenueCheck] {t.kind}: venue life {(life != null ? life.Kind + ", " + life.People + " people" : "NONE")}, seats {Object.FindObjectsByType<KR.Gameplay.Interaction.SeatSpot>(FindObjectsSortMode.None).Length}");
                Overhead(dir, $"venue_{v}_{t.kind}_top");
                Shot(dir, $"venue_{v}_{t.kind}_ahead", 0f);
                Shot(dir, $"venue_{v}_{t.kind}_left", -60f);
                Shot(dir, $"venue_{v}_{t.kind}_right", 60f);
                if (life != null && (t.kind == VenueKind.Bank || t.kind == VenueKind.Clinic))
                    life.Simulate("venue_ticket");
                _next = now + (t.kind == VenueKind.Bank || t.kind == VenueKind.Clinic ? 22 : 1);
            }
            else
            {
                var life = Object.FindFirstObjectByType<VenueLife>();
                bool waits = life != null && (t.kind == VenueKind.Bank || t.kind == VenueKind.Clinic);
                // Game time runs slower than real time when frames are slow: wait for the call itself (up to 90 s).
                if (waits && life.CalledTo == null && _waitedSince + 90 > now)
                {
                    if (_waitedSince == 0)
                        _waitedSince = now;
                    _next = now + 2;
                    return;
                }
                _waitedSince = 0;
                if (waits)
                {
                    Debug.Log($"[VenueCheck] {t.kind}: called to {(life.CalledTo ?? "NOBODY")}");
                    Shot(dir, $"venue_{v}_{t.kind}_called", 0f);
                }
                bi.ForceLeave();
                _next = now + 1;
            }
            _step++;
        }

        // Everyone with a PersonAnimator that is showing: feet below the ground, or much shorter than standing.
        static void People(string dir)
        {
            int total = 0, sunk = 0, crouched = 0;
            var lines = new List<string>();
            var byModel = new Dictionary<string, (int n, int bad)>();
            foreach (var pa in Object.FindObjectsByType<PersonAnimator>(FindObjectsSortMode.None))
            {
                var rs = pa.GetComponentsInChildren<SkinnedMeshRenderer>();
                if (rs.Length == 0 || !rs[0].isVisible && !rs[0].enabled)
                    continue;
                var b = rs[0].bounds;
                foreach (var r in rs)
                    b.Encapsulate(r.bounds);
                total++;
                var p = pa.transform.position;
                // The ground under them: not their own collider, not another person, not furniture they stand by.
                float ground = p.y;
                float bestGap = float.MaxValue;
                foreach (var h in Physics.RaycastAll(p + Vector3.up * 1.0f, Vector3.down, 4f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                    if (!h.collider.transform.IsChildOf(pa.transform) && !(h.collider is CapsuleCollider) && !(h.collider is CharacterController) &&
                        h.normal.y > 0.7f && Mathf.Abs(h.point.y - p.y) < bestGap)
                    {
                        bestGap = Mathf.Abs(h.point.y - p.y);
                        ground = h.point.y;
                    }
                bool isSunk = b.min.y < ground - 0.2f;
                bool isLow = b.size.y < 1.3f;
                if (isSunk) sunk++;
                if (isLow) crouched++;
                string model = rs[0].sharedMesh != null ? rs[0].sharedMesh.name : "?";
                var m = byModel.TryGetValue(model, out var e) ? e : (0, 0);
                byModel[model] = (m.Item1 + 1, m.Item2 + (isSunk || isLow ? 1 : 0));
                if ((isSunk || isLow) && lines.Count < 60)
                    lines.Add($"{(isSunk ? "SUNK" : "")}{(isLow ? " LOW" : "")} {pa.name} model {model} height {b.size.y:0.00} feet {b.min.y - ground:0.00}");
            }
            File.WriteAllLines(Path.Combine(dir, "people.txt"), lines.Concat(byModel.OrderByDescending(k => k.Value.bad).Select(k => $"MODEL {k.Key}: {k.Value.n} people, {k.Value.bad} wrong")));
            Debug.Log($"[VenueCheck] People: {total} showing, {sunk} with feet in the ground, {crouched} crouched/short. Details in people.txt");
        }

        // The whole room from just under the ceiling, looking straight down.
        static void Overhead(string dir, string name)
        {
            var room = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == "Interior");
            if (room == null)
                return;
            var rs = room.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0)
                return;
            var b = rs[0].bounds;
            foreach (var r in rs)
                b.Encapsulate(r.bounds);
            var go = new GameObject("Capture camera");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(b.extents.x, b.extents.z) + 0.5f;
            cam.nearClipPlane = 0.3f;   // starts under the ceiling slab
            cam.farClipPlane = 6f;
            var rt = new RenderTexture(1024, 1024, 24);
            var tex = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            float ceiling = b.max.y - 0.35f;
            cam.transform.SetPositionAndRotation(new Vector3(b.center.x, ceiling, b.center.z), Quaternion.Euler(90f, 0f, 0f));
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(rt);
        }

        static void Shot(string dir, string name, float turn)
        {
            var main = Camera.main;
            if (main == null)
                return;
            var go = new GameObject("Capture camera");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.fieldOfView = 75f;
            var rt = new RenderTexture(1280, 720, 24);
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            cam.transform.SetPositionAndRotation(main.transform.position, Quaternion.Euler(8f, main.transform.eulerAngles.y + turn, 0f));
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(rt);
        }

        static void Quit()
        {
            SessionState.EraseString(Key);
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
    }
}
