using UnityEngine;

namespace KR.Core.Diagnostics
{
    // Development builds and Editor only. Label refreshes twice a second to keep allocations negligible.
    public sealed class FpsOverlay : MonoBehaviour
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        const float Interval = 0.5f;

        float _elapsed;
        int _frames;
        float _worstMs;
        string _label = "";
        GUIStyle _style;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _elapsed += dt;
            _frames++;
            _worstMs = Mathf.Max(_worstMs, dt * 1000f);
            if (_elapsed < Interval)
                return;
            _label = $"{_frames / _elapsed:0} FPS  worst {_worstMs:0} ms";
            _elapsed = 0f;
            _frames = 0;
            _worstMs = 0f;
        }

        void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.022f) };
            GUI.Label(new Rect(16, 16 + Screen.safeArea.yMin, Screen.width, 100), _label, _style);
        }
#endif
    }
}
