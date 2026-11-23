using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace KR.Gameplay.Player
{
    // Resolution choices (owner feedback 2026-11-23: "make sure the resolution can be adjusted"), kept on this
    // device in PlayerPrefs and applied at launch:
    //   - on a monitor: the screen resolution and windowed or full screen;
    //   - in a headset: the render resolution per eye (lower is smoother, higher is sharper).
    // Changed from the main menu's Settings and the in-game Settings page.
    public static class DisplaySettings
    {
        const string KeyW = "kr.display.w", KeyH = "kr.display.h", KeyMode = "kr.display.mode", KeyVr = "kr.display.vrScale";

        public static readonly float[] VrScales = { 0.7f, 0.85f, 1f, 1.2f };
        static readonly FullScreenMode[] Modes = { FullScreenMode.FullScreenWindow, FullScreenMode.Windowed, FullScreenMode.ExclusiveFullScreen };

        // The standalone headset starts a little under native (it holds the frame rate); a PC headset at native.
        public static float DefaultVrScale => Application.platform == RuntimePlatform.Android ? 0.85f : 1f;

        public static float VrScale
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(KeyVr, DefaultVrScale), 0.5f, 1.5f);
            private set { PlayerPrefs.SetFloat(KeyVr, value); PlayerPrefs.Save(); }
        }

        public static string VrScaleText => $"{Mathf.RoundToInt(VrScale * 100f)}%";
        public static string ResolutionText => $"{Screen.width} x {Screen.height}";
        public static string ModeText => Screen.fullScreenMode switch
        {
            FullScreenMode.Windowed => "windowed",
            FullScreenMode.ExclusiveFullScreen => "full screen (exclusive)",
            _ => "full screen",
        };
        public static bool HasMonitor => Application.platform != RuntimePlatform.Android;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ApplySaved()
        {
            ApplyVr();
            if (!HasMonitor || Application.isEditor)
                return;
            int w = PlayerPrefs.GetInt(KeyW, 0), h = PlayerPrefs.GetInt(KeyH, 0);
            var mode = (FullScreenMode)PlayerPrefs.GetInt(KeyMode, (int)Screen.fullScreenMode);
            if (w > 0 && h > 0)
                Screen.SetResolution(w, h, mode);
        }

        public static void ApplyVr()
        {
            if (XRSettings.enabled)
                XRSettings.eyeTextureResolutionScale = VrScale;
        }

        public static void NextVrScale()
        {
            int i = System.Array.IndexOf(VrScales, Nearest(VrScale));
            VrScale = VrScales[(i + 1) % VrScales.Length];
            ApplyVr();
        }

        static float Nearest(float v)
        {
            float best = VrScales[0];
            foreach (var s in VrScales)
                if (Mathf.Abs(s - v) < Mathf.Abs(best - v))
                    best = s;
            return best;
        }

        // The monitor's resolutions (distinct sizes, smallest first); a common list where the platform gives none.
        public static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (v.x >= 800 && !list.Contains(v))
                    list.Add(v);
            }
            if (list.Count == 0)
                list.AddRange(new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440) });
            list.Sort((a, b) => (a.x * a.y).CompareTo(b.x * b.y));
            return list;
        }

        public static void NextResolution()
        {
            var list = Resolutions();
            var now = new Vector2Int(Screen.width, Screen.height);
            int i = list.IndexOf(now);
            var next = list[(i + 1) % list.Count];
            Screen.SetResolution(next.x, next.y, Screen.fullScreenMode);
            PlayerPrefs.SetInt(KeyW, next.x);
            PlayerPrefs.SetInt(KeyH, next.y);
            PlayerPrefs.Save();
        }

        public static void NextMode()
        {
            int i = System.Array.IndexOf(Modes, Screen.fullScreenMode);
            var mode = Modes[(i + 1) % Modes.Length];
            Screen.fullScreenMode = mode;
            PlayerPrefs.SetInt(KeyMode, (int)mode);
            PlayerPrefs.Save();
        }
    }
}
