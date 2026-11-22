using KR.Gameplay.Story;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Diagnostics
{
    // Adds the performance overlay and the benchmark to the story scene when it loads (no scene edits needed).
    static class DiagnosticsBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var nav = Object.FindFirstObjectByType<Navigator>();
            if (nav == null || nav.GetComponent<BenchmarkRun>() != null)
                return;
            nav.gameObject.AddComponent<PerformanceOverlay>();
            nav.gameObject.AddComponent<BenchmarkRun>();
        }
    }
}
