using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using KR.Core.Diagnostics;
using KR.Gameplay.Player;
using KR.Gameplay.Story;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KR.Gameplay.Diagnostics
{
    // A repeatable FPS benchmark: the same 1.1 km street route every time, from the player's start spot beside the
    // car, carried along at 12 m/s at eye height (about 90 s) with the city, traffic, people and lights all running.
    // The first 3 s are a warm-up and are not counted. It writes a per-second CSV and a summary (average FPS, 1%
    // and 0.1% lows, share of frames over the budget, draw calls) to persistentDataPath/Benchmarks and the log.
    // Start it with F9, the Settings page on the headset, or the -benchmark command line (quits when done).
    public sealed class BenchmarkRun : MonoBehaviour
    {
        const float Speed = 12f, MaxLength = 1100f, WarmUp = 3f;
        static readonly Vector3[] Probes = { new Vector3(700f, 0f, 700f), new Vector3(-700f, 0f, 700f),
                                             new Vector3(700f, 0f, -700f), new Vector3(-700f, 0f, -700f) };

        public static bool Running { get; private set; }
        public static string LastSummary { get; private set; }

        PlayerRig _rig;
        Navigator _nav;
        bool _quitWhenDone;

        void Start()
        {
            _rig = FindFirstObjectByType<PlayerRig>();
            _nav = FindFirstObjectByType<Navigator>();
            foreach (var arg in System.Environment.GetCommandLineArgs())
                if (arg == "-benchmark")
                {
                    _quitWhenDone = true;
                    StartCoroutine(StartLater(8f));
                }
        }

        IEnumerator StartLater(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Begin();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f9Key.wasPressedThisFrame)
                Begin();
        }

        public void Begin()
        {
            if (Running || _rig == null || _nav == null || _nav.Graph == null)
                return;
            if (_rig.CurrentMode != PlayerRig.Mode.Walking)
            {
                _rig.Toast("Get out of the car to run the benchmark.", 3f);
                return;
            }
            var route = PlanRoute();
            if (route == null)
            {
                _rig.Toast("No benchmark route found.", 3f);
                return;
            }
            StartCoroutine(Run(route));
        }

        // A fixed route: from the start spot towards the first of four far-off probe points that gives a route of
        // at least 600 m, cut to MaxLength. The world is fixed, so it is the same route on every run and device.
        System.Collections.Generic.List<Vector3> PlanRoute()
        {
            var g = _nav.Graph;
            int from = g.Nearest(_rig.StartSpot);
            foreach (var probe in Probes)
            {
                int to = g.Nearest(_rig.StartSpot + probe, 2000f);
                var path = g.Route(from, to, false);
                if (path == null || path.Count < 2 || Length(path) < 600f)
                    continue;
                return path;
            }
            return null;
        }

        static float Length(System.Collections.Generic.List<Vector3> path)
        {
            float len = 0f;
            for (int i = 1; i < path.Count; i++)
                len += Vector3.Distance(path[i - 1], path[i]);
            return len;
        }

        IEnumerator Run(System.Collections.Generic.List<Vector3> route)
        {
            Running = true;
            var motor = _rig.Player;
            var back = motor.transform.position;
            float backYaw = motor.transform.eulerAngles.y;
            _rig.Busy = true;
            motor.Frozen = true;
            _rig.Toast("Benchmark: 90 s along the streets. Keep your head still; close your eyes if moving bothers you.", 6f);
            VrRig.Fade(0.4f);

            var all = new FrameStats();
            var second = new FrameStats(256);
            var counters = new RenderCounters();
            var csv = new StringBuilder("t_s,fps,avg_ms,worst_ms,main_thread_ms,draw_calls,batches,setpass,triangles,gc_kb_frame,used_mb\n");
            float total = Mathf.Min(Length(route), MaxLength), travelled = 0f, clock = 0f, secondClock = 0f;
            long drawSum = 0, batchSum = 0, trisSum = 0;
            int rows = 0;
            var ci = CultureInfo.InvariantCulture;

            while (travelled < total)
            {
                yield return null;
                if (GamePause.Paused)
                    continue;
                float dt = Time.unscaledDeltaTime;
                clock += dt;
                travelled += Speed * dt;
                // Walk the route to the current distance.
                float d = travelled;
                Vector3 pos = route[route.Count - 1], dir = Vector3.forward;
                for (int i = 1; i < route.Count; i++)
                {
                    float seg = Vector3.Distance(route[i - 1], route[i]);
                    if (d <= seg || i == route.Count - 1)
                    {
                        pos = Vector3.Lerp(route[i - 1], route[i], seg > 0f ? Mathf.Clamp01(d / seg) : 1f);
                        dir = route[i] - route[i - 1];
                        break;
                    }
                    d -= seg;
                }
                dir.y = 0f;
                float yaw = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir).eulerAngles.y : motor.transform.eulerAngles.y;
                motor.Teleport(pos, yaw);

                if (clock < WarmUp)
                    continue;
                float ms = dt * 1000f;
                all.Add(ms);
                second.Add(ms);
                secondClock += dt;
                if (secondClock >= 1f)
                {
                    rows++;
                    drawSum += counters.DrawCalls;
                    batchSum += counters.Batches;
                    trisSum += counters.Triangles;
                    csv.Append((clock - WarmUp).ToString("0.0", ci)).Append(',')
                       .Append(second.AverageFps.ToString("0.0", ci)).Append(',')
                       .Append(second.AverageMs.ToString("0.00", ci)).Append(',')
                       .Append(second.WorstMs.ToString("0.00", ci)).Append(',')
                       .Append(counters.MainThreadMs.ToString("0.00", ci)).Append(',')
                       .Append(counters.DrawCalls).Append(',').Append(counters.Batches).Append(',')
                       .Append(counters.SetPassCalls).Append(',').Append(counters.Triangles).Append(',')
                       .Append((counters.GcBytesThisFrame / 1024f).ToString("0.0", ci)).Append(',')
                       .Append((counters.UsedMemoryBytes / (1024f * 1024f)).ToString("0", ci)).Append('\n');
                    second.Clear();
                    secondClock = 0f;
                }
            }
            counters.Dispose();

            VrRig.Fade(0.4f);
            motor.Teleport(back, backYaw);
            motor.Frozen = false;
            _rig.Busy = false;
            Running = false;

            float budget = PerformanceOverlay.BudgetMs;
            var sum = new StringBuilder();
            sum.Append("Kigali Rush benchmark ").Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci)).Append('\n')
               .Append("Device: ").Append(SystemInfo.deviceModel).Append(" | GPU: ").Append(SystemInfo.graphicsDeviceName)
               .Append(" | ").Append(SystemInfo.graphicsDeviceType).Append(" | VR: ").Append(VrRig.Active).Append('\n')
               .Append("Quality: ").Append(QualitySettings.names[QualitySettings.GetQualityLevel()])
               .Append(" | target ").Append(PerformanceOverlay.TargetHz.ToString("0", ci)).Append(" Hz (budget ")
               .Append(budget.ToString("0.0", ci)).Append(" ms) | dev build: ").Append(Debug.isDebugBuild).Append('\n')
               .Append("Route: ").Append(total.ToString("0", ci)).Append(" m at ").Append(Speed.ToString("0", ci))
               .Append(" m/s, ").Append(all.Count).Append(" frames measured\n")
               .Append("Average FPS: ").Append(all.AverageFps.ToString("0.0", ci))
               .Append(" | 1% low: ").Append(all.LowFps(1f).ToString("0.0", ci))
               .Append(" | 0.1% low: ").Append(all.LowFps(0.1f).ToString("0.0", ci))
               .Append(" | worst frame: ").Append(all.WorstMs.ToString("0.0", ci)).Append(" ms\n")
               .Append("Frames over budget: ").Append((all.ShareOver(budget) * 100f).ToString("0.0", ci)).Append("%");
            if (rows > 0 && drawSum > 0)
                sum.Append(" | avg draw calls ").Append(drawSum / rows).Append(", batches ").Append(batchSum / rows)
                   .Append(", triangles ").Append((trisSum / rows / 1000).ToString(ci)).Append("k");
            LastSummary = sum.ToString();

            string folder = Path.Combine(Application.persistentDataPath, "Benchmarks");
            string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss", ci);
            string platform = VrRig.Active ? "quest" : Application.platform.ToString().ToLowerInvariant();
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, $"benchmark_{platform}_{stamp}.csv"), csv.ToString());
                File.WriteAllText(Path.Combine(folder, $"benchmark_{platform}_{stamp}.txt"), LastSummary);
            }
            catch (IOException e)
            {
                Debug.LogWarning("[Benchmark] Could not write results: " + e.Message);
            }
            Debug.Log("[Benchmark] " + LastSummary.Replace('\n', ' ') + " | files in " + folder);
            _rig.Toast($"Benchmark done: {all.AverageFps:0} FPS average, 1% low {all.LowFps(1f):0}. Settings page shows the full result.", 8f);
            if (_quitWhenDone)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }
    }
}
