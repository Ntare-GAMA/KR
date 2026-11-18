using UnityEngine;

namespace KR.Gameplay.Player
{
    // The player's comfort choices (set from the Settings page, saved by Story Mode).
    public static class Comfort
    {
        public enum Turn { Snap30, Snap45, Smooth }

        public static Turn TurnMode = Turn.Snap30;
        public static float VignetteStrength = 0.5f;   // 0 off .. 1 strong
        public static bool Teleport;                    // walk by teleport instead of the stick
        public static bool LargeText;
        public static bool Seated;                      // raises the view for seated play
        public static bool RecenterRequested;
    }

    // One place that pauses the game: the info panel, the headset taken off. Physics and the clocks stop with
    // Time.timeScale; anything that must keep running (menus) uses unscaled time.
    public static class GamePause
    {
        static bool _panel, _headset;

        public static bool Paused => _panel || _headset;
        public static bool HeadsetOff => _headset;

        public static void SetPanel(bool on) { _panel = on; Apply(); }
        public static void SetHeadsetOff(bool on) { _headset = on; Apply(); }

        static void Apply() => Time.timeScale = Paused ? 0f : 1f;
    }
}
