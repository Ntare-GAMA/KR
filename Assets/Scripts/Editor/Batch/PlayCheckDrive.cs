using System.IO;
using System.Reflection;
using KR.Gameplay.Player;
using KR.Gameplay.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Editor.Batch
{
    // Play-mode check (no headset) of the driver's controls and the way back to the menu: sits in the car, photographs
    // the gear lever and the steering wheel from the driver's eyes, selects D by the lever's API, turns the wheel as a
    // hand would and checks it turns the car the same way, then uses Settings > Quit to main menu and waits for the
    // menu (photographed too). Run without -batchmode:
    //   Unity -projectPath . -executeMethod KR.Editor.Batch.PlayCheckDrive.Run -captureDir Logs/Drive
    [InitializeOnLoad]
    public static class PlayCheckDrive
    {
        const string Key = "kr.playcheck.drive";
        static double _next, _since;
        static int _step;

        static PlayCheckDrive()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(Key, "")))
                EditorApplication.update += Tick;
        }

        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-captureDir");
            string dir = Path.GetFullPath(i >= 0 && i + 1 < args.Length ? args[i + 1] : "Logs/Drive");
            Directory.CreateDirectory(dir);
            SessionState.SetString(Key, dir);
            PlayerPrefs.DeleteKey("kr.welcome.seen");   // so the menu shows the welcome screen
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
            try
            {
                Step(dir, now);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[DriveCheck] " + e);
                Quit();
            }
        }

        static void Step(string dir, double now)
        {
            var rig = Object.FindFirstObjectByType<PlayerRig>();
            switch (_step)
            {
                case 0:
                    if (rig == null || rig.Car == null)
                        throw new System.Exception("No player or car.");
                    typeof(PlayerRig).GetMethod("EnterCar", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(rig, null);
                    Debug.Log($"[DriveCheck] In the car: mode {rig.CurrentMode}, gear {rig.Car.Gear}");
                    _step = 1;
                    _next = now + 3;
                    return;
                case 1:
                {
                    var car = rig.Car;
                    var shifter = car.GetComponent<GearShifter>();
                    var grab = car.GetComponent<WheelGrab>();
                    Debug.Log($"[DriveCheck] shifter {(shifter != null && shifter.Knob != null ? car.transform.InverseTransformPoint(shifter.Knob.position).ToString("F3") : "MISSING")} (car space), " +
                              $"wheel grab {(grab != null ? "on" : "MISSING")}, eyes {car.transform.InverseTransformPoint(Camera.main.transform.position):F3}");
                    if (shifter != null && shifter.Knob != null)
                        PlayCaptureShot(dir, "lever_P", shifter.Knob.position);
                    PlayCaptureShot(dir, "wheel_straight", car.SteeringWheel.position);
                    if (grab != null)
                        grab.enabled = false;   // no headset here: drive the wheel angle directly
                    bool ok = car.SelectGear(DrivableCar.GearSel.D);
                    Debug.Log($"[DriveCheck] SelectGear(D) {ok}: gear {car.Gear}");
                    car.HandWheelAngle = 90f;   // a quarter turn clockwise, as the driver's hands would
                    _step = 2;
                    _next = now + 2;
                    return;
                }
                case 2:
                {
                    var car = rig.Car;
                    var shifter = car.GetComponent<GearShifter>();
                    if (shifter != null && shifter.Knob != null)
                        PlayCaptureShot(dir, "lever_D", shifter.Knob.position);
                    PlayCaptureShot(dir, "wheel_right_90", car.SteeringWheel.position);
                    float topRight = Vector3.Dot(car.SteeringWheel.up, car.transform.right);
                    Debug.Log($"[DriveCheck] wheel +90: road wheels {car.SteerAngle:F1}° (want > 0, right), rim top towards car right {topRight:F2} (want > 0) => " +
                              (car.SteerAngle > 0f && topRight > 0f ? "SAME WAY: OK" : "MISMATCH"));
                    car.HandWheelAngle = float.NaN;
                    car.SelectGear(DrivableCar.GearSel.P);
                    var hud = Object.FindFirstObjectByType<VrHud>();
                    if (hud == null)
                        throw new System.Exception("No VrHud.");
                    typeof(VrHud).GetMethod("QuitToMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, null);
                    Debug.Log("[DriveCheck] Quit to main menu pressed.");
                    _since = now;
                    _step = 3;
                    _next = now + 1;
                    return;
                }
                case 3:
                    if (SceneManager.GetActiveScene().name == "Quest Menu")
                    {
                        Debug.Log($"[DriveCheck] Main menu loaded after {now - _since:F1} s: OK. timeScale {Time.timeScale}, paused {GamePause.Paused}");
                        _step = 4;
                        _next = now + 4;
                        return;
                    }
                    if (now - _since > 90)
                        throw new System.Exception("The main menu did not load within 90 s.");
                    _next = now + 1;
                    return;
                default:
                    var menu = Object.FindFirstObjectByType<KR.Gameplay.UI.MainMenu>();
                    if (menu != null)
                        PlayCaptureShot(dir, "menu_welcome", menu.transform.GetComponentInChildren<Canvas>().transform.position);
                    Debug.Log("[DriveCheck] Done.");
                    Quit();
                    return;
            }
        }

        // Height map of the centre console (car space, cm) from the body mesh, to find the model's own gear stick.
        static void Probe(DrivableCar car)
        {
            var t = car.transform;
            MeshFilter body = null;
            foreach (var mf in car.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name.EndsWith("Body_LOD0"))
                    body = mf;
            if (body == null)
            {
                Debug.Log("[DriveCheck] probe: no body mesh");
                return;
            }
            var go = new GameObject("probe");
            go.transform.SetPositionAndRotation(body.transform.position, body.transform.rotation);
            go.transform.localScale = body.transform.lossyScale;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = body.sharedMesh;
            Physics.SyncTransforms();
            var sb = new System.Text.StringBuilder("[DriveCheck] console heights (cm above car origin), rows z from 0.70 down to -0.10, columns x -0.30..0.20 step 2 cm:\n");
            for (float z = 0.70f; z >= -0.10f; z -= 0.02f)
            {
                sb.Append($"z{z:+0.00;-0.00} ");
                for (float x = -0.30f; x <= 0.201f; x += 0.02f)
                {
                    var from = t.TransformPoint(new Vector3(x, 1.0f, z));
                    float h = mc.Raycast(new Ray(from, -t.up), out var hit, 1.0f) ? t.InverseTransformPoint(hit.point).y : 0f;
                    sb.Append(Mathf.RoundToInt(h * 100f).ToString().PadLeft(4));
                }
                sb.Append('\n');
            }
            Debug.Log(sb.ToString());
            Object.DestroyImmediate(go);
        }

        static void PlayCaptureShot(string dir, string name, Vector3 target)
        {
            var main = Camera.main;
            if (main == null)
                return;
            var go = new GameObject("Capture camera");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.05f;
            var rt = new RenderTexture(1280, 720, 24);
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            cam.transform.SetPositionAndRotation(main.transform.position, Quaternion.LookRotation(target - main.transform.position));
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
            EditorApplication.Exit(0);
        }
    }
}
