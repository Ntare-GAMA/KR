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
        const float HopMetres = 60f;
        float _lastShown = -1f;
        public static string LastSummary { get; private set; }

        PlayerRig _rig;
        Navigator _nav;
        bool _quitWhenDone;

        void Start()
        {
            _rig = FindFirstObjectByType<PlayerRig>();
            _nav = FindFirstObjectByType<Navigator>();
            var args = System.Environment.GetCommandLineArgs();
            // -lodBias 1000 keeps every object at full detail: the "LODs off" comparison run.
            int bias = System.Array.IndexOf(args, "-lodBias");
            if (bias >= 0 && bias + 1 < args.Length && float.TryParse(args[bias + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float lodBias))
                QualitySettings.lodBias = lodBias;
            // Switches to find what the frame time goes on (compare runs): -noTraffic, -noPeople (all person
            // animators off), -noCity (instanced city scatter off), -noLights (street lamps, signals), -noPhysics.
            Without = Off(args);
            if (System.Array.IndexOf(args, "-benchmark") >= 0)
            {
                Debug.Log("[Benchmark] -benchmark: starting in 8 s.");
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
            {
                Debug.LogWarning($"[Benchmark] Not started: running {Running}, rig {_rig != null}, navigator {_nav != null}, graph {_nav != null && _nav.Graph != null}.");
                return;
            }
            if (_rig.CurrentMode != PlayerRig.Mode.Walking)
            {
                _rig.Toast("Get out of the car to run the benchmark.", 3f);
                Debug.LogWarning("[Benchmark] Not started: the player is in the car.");
                return;
            }
            var route = PlanRoute();
            if (route == null)
            {
                _rig.Toast("No benchmark route found.", 3f);
                Debug.LogWarning("[Benchmark] Not started: no route found.");
                return;
            }
            Fix();
            Debug.Log($"[Benchmark] Started: {Length(route):0} m route, {route.Count} points, 10:00, calm weather.");
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

        // Same conditions every run: 10:00 with the clock held, calm dry weather, the same random sequence.
        float _hour, _minutesPerSecond;
        int _vSync, _targetFps;

        void Fix()
        {
            // Uncapped on a desktop, so the result is what the machine can do, not the monitor's refresh rate
            // (the headset always runs at its display rate).
            _vSync = QualitySettings.vSyncCount;
            _targetFps = Application.targetFrameRate;
            if (!VrRig.Active)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
            }
            var sky = KR.Gameplay.World.DayNightCycle.Instance;
            if (sky != null)
            {
                _hour = sky.Hour;
                _minutesPerSecond = sky.GameMinutesPerSecond;
                sky.Hour = 10f;
                sky.GameMinutesPerSecond = 0f;
            }
            var weather = FindFirstObjectByType<KR.Gameplay.World.WeatherCycle>();
            if (weather != null)
            {
                weather.Force(KR.Gameplay.World.WeatherCycle.Kind.Calm);
                weather.SnapToForced();
            }
            Random.InitState(20261122);
        }

        void Unfix()
        {
            QualitySettings.vSyncCount = _vSync;
            Application.targetFrameRate = _targetFps;
            var sky = KR.Gameplay.World.DayNightCycle.Instance;
            if (sky != null)
            {
                sky.Hour = _hour;
                sky.GameMinutesPerSecond = _minutesPerSecond;
            }
            var weather = FindFirstObjectByType<KR.Gameplay.World.WeatherCycle>();
            if (weather != null)
                weather.Force(null);
        }

        public static string Without { get; private set; } = "";

        static string Off(string[] args)
        {
            var off = new System.Collections.Generic.List<string>();
            bool Has(string a) => System.Array.IndexOf(args, a) >= 0;
            if (Has("-noTraffic"))
            {
                off.Add("traffic");
                foreach (var t in FindObjectsByType<KR.Gameplay.World.RoadNetworkTraffic>(FindObjectsSortMode.None))
                    t.gameObject.SetActive(false);
            }
            if (Has("-noPeople"))
            {
                off.Add("people");
                foreach (var a in FindObjectsByType<Animator>(FindObjectsSortMode.None))
                    if (a.GetComponentInParent<PlayerRig>() == null)
                        a.enabled = false;
                foreach (var pa in FindObjectsByType<KR.Gameplay.World.PersonAnimator>(FindObjectsSortMode.None))
                    pa.enabled = false;
            }
            if (Has("-noCity"))
            {
                off.Add("city scatter");
                foreach (var c in FindObjectsByType<KR.Gameplay.World.InstancedScatter>(FindObjectsSortMode.None))
                    c.enabled = false;
            }
            if (Has("-noLights"))
            {
                off.Add("lights");
                foreach (var l in FindObjectsByType<KR.Gameplay.World.StreetLightPool>(FindObjectsSortMode.None))
                    l.enabled = false;
                foreach (var l in FindObjectsByType<KR.Gameplay.World.TrafficSignalSystem>(FindObjectsSortMode.None))
                    l.enabled = false;
            }
            if (Has("-noPhysics"))
            {
                off.Add("physics");
                Physics.simulationMode = SimulationMode.Script;
            }
            return string.Join(", ", off);
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
            if (_rig.DesktopCamera != null)
                _rig.DesktopCamera.ViewLocked = true;
            _rig.Toast(VrRig.Active ? "Benchmark: about 70 s. The view jumps along a street every few seconds; you stay still in between."
                                    : "Benchmark: 90 s along the streets.", 6f);
            _lastShown = -1f;
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
                float dt = Time.unscaledDeltaTime;
                clock += dt;
                travelled += Speed * dt;
                // In the headset the view never glides (that makes people sick): it jumps to the next point along
                // the route every 60 m, behind a short fade, and stays still in between. Same route and length.
                float shown = VrRig.Active ? Mathf.Floor(travelled / HopMetres) * HopMetres : travelled;
                bool hop = VrRig.Active && shown != _lastShown;
                _lastShown = shown;
                if (VrRig.Active && !hop)
                    goto measure;
                if (hop)
                    VrRig.Fade(0.25f);
                float d = shown;
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
                if (_rig.DesktopCamera != null)
                    _rig.DesktopCamera.LookAlong(yaw, 3f);

            measure:
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
            if (_rig.DesktopCamera != null)
                _rig.DesktopCamera.ViewLocked = false;
            _rig.Busy = false;
            Running = false;
            Unfix();

            float budget = PerformanceOverlay.BudgetMs;
            var sum = new StringBuilder();
            sum.Append("Kigali Rush benchmark ").Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci)).Append('\n')
               .Append("Device: ").Append(SystemInfo.deviceModel).Append(" | GPU: ").Append(SystemInfo.graphicsDeviceName)
               .Append(" | ").Append(SystemInfo.graphicsDeviceType).Append(" | VR: ").Append(VrRig.Active).Append('\n')
               .Append("Quality: ").Append(QualitySettings.names[QualitySettings.GetQualityLevel()])
               .Append(" | off: ").Append(string.IsNullOrEmpty(Without) ? "nothing" : Without)
               .Append(" | LOD bias ").Append(QualitySettings.lodBias.ToString("0.##", ci))
               .Append(" | display ").Append(PerformanceOverlay.TargetHz.ToString("0", ci)).Append(" Hz (budget ")
               .Append(budget.ToString("0.0", ci)).Append(" ms, ").Append(VrRig.Active ? "vsync" : "uncapped").Append(") | dev build: ").Append(Debug.isDebugBuild).Append('\n')
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
