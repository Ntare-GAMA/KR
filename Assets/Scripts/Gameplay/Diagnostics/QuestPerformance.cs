using System.Collections.Generic;
using KR.Gameplay.Player;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace KR.Gameplay.Diagnostics
{
    // Settings for the standalone headset only (an Android build with the headset active), so the game holds the
    // display rate: a frame rate under the display's 72 Hz is the biggest cause of motion sickness (the headset fills
    // in frames and the world judders). The PC build is not touched. Measured before on a Quest 2: 41.6 FPS average,
    // 1811 draw calls, 591k triangles (BenchmarkRun). Applied when the city loads:
    //   - fewer pixels per eye (85%) and the strongest fixed foveation (sharp in the middle of the view);
    //   - no real-time shadows (the city's lighting stays);
    //   - small things, street dressing, trees and houses stop being drawn sooner (per-layer cull distances); LODs switch to simpler models sooner;
    //   - the camera's far plane at 600 m (fog hides the edge).
    public sealed class QuestPerformance : MonoBehaviour
    {
        public const float EyeScale = 0.85f, LodBias = 0.55f, FarPlane = 600f;

        static readonly Dictionary<string, float> LayerDistances = new Dictionary<string, float>
        {
            // Tightened 2026-11-24 after the Quest Pro benchmark (1,009 draw calls, 438k triangles: draw calls, not
            // triangles, are the limit). Fog hides the edge at these distances.
            { "PcDetail", 90f }, { "StreetDressing", 150f }, { "PcTrees", 180f }, { "PcHouses", 320f },
        };

        public static bool Applies => Application.platform == RuntimePlatform.Android;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            if (!Applies)
                return;
            var go = new GameObject("Quest performance");
            DontDestroyOnLoad(go);
            go.AddComponent<QuestPerformance>();
        }

        void OnEnable() => SceneManager.sceneLoaded += Loaded;
        void OnDisable() => SceneManager.sceneLoaded -= Loaded;

        void Start() => Apply();
        void Loaded(Scene s, LoadSceneMode m) => Invoke(nameof(Apply), 0.5f);

        void Apply()
        {
            // The player's render resolution (Settings) wins; 85% unless they changed it.
            XRSettings.eyeTextureResolutionScale = DisplaySettings.VrScale;
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays)
            {
                d.foveatedRenderingLevel = 1f;   // strongest
                d.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed;
            }
            QualitySettings.lodBias = LodBias;
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.shadowDistance = 0f;
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional)
                    light.shadows = LightShadows.None;
            var cam = Camera.main;
            if (cam != null)
            {
                cam.farClipPlane = Mathf.Min(cam.farClipPlane, FarPlane);
                var distances = cam.layerCullDistances;
                foreach (var kv in LayerDistances)
                {
                    int layer = LayerMask.NameToLayer(kv.Key);
                    if (layer >= 0)
                        distances[layer] = distances[layer] > 0f ? Mathf.Min(distances[layer], kv.Value) : kv.Value;
                }
                cam.layerCullDistances = distances;
                cam.layerCullSpherical = true;
            }
            Debug.Log($"[QuestPerformance] eye scale {DisplaySettings.VrScale}, foveation max, shadows off, LOD bias {LodBias}, far {FarPlane} m, layer culling on.");
        }
    }
}
