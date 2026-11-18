using UnityEngine;

namespace KR.Gameplay.World
{
    // Shows its children only between two clock hours (wraps past midnight), so city life follows the day:
    // markets in the morning, football in the afternoon, the club at night.
    public sealed class ScheduleVisibility : MonoBehaviour
    {
        [SerializeField, Range(0f, 24f)] float fromHour = 8f;
        [SerializeField, Range(0f, 24f)] float toHour = 18f;

        float _timer;
        bool _shown = true;

        public void Configure(float from, float to)
        {
            fromHour = from;
            toHour = to;
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f)
                return;
            _timer = 0.25f;
            var cycle = DayNightCycle.Instance;
            if (cycle == null)
                return;
            float h = cycle.Hour;
            bool show = fromHour <= toHour ? h >= fromHour && h < toHour : h >= fromHour || h < toHour;
            if (show == _shown)
                return;
            _shown = show;
            foreach (Transform child in transform)
                child.gameObject.SetActive(show);
        }
    }
}
