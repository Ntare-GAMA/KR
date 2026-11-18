using System;
using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // The game's time of day in Kigali. Every session starts in the morning and the clock runs on from there
    // (owner decision 2026-11-17), faster than real time so a session sees the day turn to evening.
    // Falls back to the real Kigali time (UTC+2, no daylight saving) when there is no sky in the scene.
    public static class KigaliTime
    {
        public static DateTime Now
        {
            get
            {
                var today = DateTime.UtcNow.AddHours(2).Date;
                var sky = DayNightCycle.Instance;
                return sky != null ? today.AddHours(Mathf.Repeat(sky.Hour, 24f)) : DateTime.UtcNow.AddHours(2);
            }
        }

        public static float HourOfDay => (float)Now.TimeOfDay.TotalHours;
        public static string Clock => Now.ToString("HH:mm");
    }

    // Starts the day in the morning and sets how fast it runs.
    public sealed class KigaliClock : MonoBehaviour
    {
        [SerializeField] DayNightCycle cycle;
        [SerializeField, Tooltip("Hour each session starts at.")] float startHour = 6.5f;
        [SerializeField, Tooltip("Game minutes per real second (0.2 = one game hour every 5 real minutes).")]
        float gameMinutesPerSecond = 0.2f;

        public void Configure(DayNightCycle dayNight) => cycle = dayNight;

        void Start()
        {
            if (cycle == null)
                return;
            cycle.Hour = startHour;
            cycle.GameMinutesPerSecond = gameMinutesPerSecond;
        }
    }
}
