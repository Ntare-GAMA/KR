using System.Text;
using KR.Core.Diagnostics;
using KR.Gameplay.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KR.Gameplay.Diagnostics
{
    // A live performance readout: FPS, frame time, the frame budget, main-thread time, draw calls, batches,
    // SetPass calls, triangles, GC allocation and memory. F3 toggles it on a desktop; on the headset it is a row
    // on the info panel's Settings page. In the headset it floats at the top left of your view; on a desktop it is
    // drawn over the screen. Text is rebuilt twice a second so the overlay itself costs almost nothing.
    public sealed class PerformanceOverlay : MonoBehaviour
    {
        const float Interval = 0.5f;

        public static bool Visible { get; set; }

        readonly FrameStats _window = new FrameStats(256);
        readonly StringBuilder _sb = new StringBuilder(256);
        RenderCounters _counters;
        float _elapsed;
        string _label = "";
        GUIStyle _style;
        TextMesh _vrText;

        public static float BudgetMs => 1000f / TargetHz;

        // The display refresh rate the game must keep up with: 72 Hz on a Quest by default, the monitor on a PC.
        public static float TargetHz
        {
            get
            {
                float hz = VrRig.Active ? UnityEngine.XR.XRDevice.refreshRate : (float)Screen.currentResolution.refreshRateRatio.value;
                return hz > 1f ? hz : 60f;
            }
        }

        void OnEnable() => _counters = new RenderCounters();

        void OnDisable()
        {
            _counters?.Dispose();
            _counters = null;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame)
                Visible = !Visible;

            _window.Add(Time.unscaledDeltaTime * 1000f);
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed >= Interval)
            {
                _elapsed = 0f;
                if (Visible)
                    Compose();
                _window.Clear();
            }
            UpdateVrText();
        }

        void Compose()
        {
            float budget = BudgetMs;
            float avg = _window.AverageMs;
            _sb.Clear();
            _sb.Append(_window.AverageFps.ToString("0")).Append(" FPS   ")
               .Append(avg.ToString("0.0")).Append(" ms (budget ").Append(budget.ToString("0.0")).Append(")   worst ")
               .Append(_window.WorstMs.ToString("0")).Append(" ms\n");
            _sb.Append("CPU main ").Append(_counters.MainThreadMs.ToString("0.0")).Append(" ms   GC ")
               .Append((_counters.GcBytesThisFrame / 1024f).ToString("0.0")).Append(" KB/frame   RAM ")
               .Append((_counters.UsedMemoryBytes / (1024f * 1024f)).ToString("0")).Append(" MB\n");
            if (_counters.Available)
                _sb.Append("Draw calls ").Append(_counters.DrawCalls).Append("   batches ").Append(_counters.Batches)
                   .Append("   SetPass ").Append(_counters.SetPassCalls).Append("   tris ")
                   .Append((_counters.Triangles / 1000f).ToString("0")).Append("k");
            else
                _sb.Append("Draw-call counters need a development build");
            _label = _sb.ToString();
        }

        void UpdateVrText()
        {
            bool show = Visible && VrRig.Active;
            if (show && _vrText == null)
                _vrText = MakeVrText();
            if (_vrText == null)
                return;
            _vrText.gameObject.SetActive(show);
            if (!show)
                return;
            var cam = Camera.main;
            if (cam != null && _vrText.transform.parent != cam.transform)
            {
                _vrText.transform.SetParent(cam.transform, false);
                _vrText.transform.localPosition = new Vector3(-0.22f, 0.16f, 0.6f);
                _vrText.transform.localRotation = Quaternion.identity;
            }
            _vrText.text = _label;
        }

        static TextMesh MakeVrText()
        {
            var go = new GameObject("Performance overlay (VR)");
            var t = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            t.fontSize = 48;
            t.characterSize = 0.0022f;
            t.anchor = TextAnchor.UpperLeft;
            t.color = new Color(0.25f, 1f, 0.45f);
            return t;
        }

        void OnGUI()
        {
            if (!Visible || VrRig.Active)
                return;
            _style ??= new GUIStyle(GUI.skin.box)
            {
                fontSize = Mathf.Max(14, Mathf.RoundToInt(Screen.height * 0.018f)),
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.4f, 1f, 0.5f) },
            };
            var size = _style.CalcSize(new GUIContent(_label));
            GUI.Box(new Rect(12f, 12f, size.x + 16f, size.y + 8f), _label, _style);
        }
    }
}
