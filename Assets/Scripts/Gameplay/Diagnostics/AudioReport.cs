using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Diagnostics
{
    // Writes the state of the game's sound to the log a few seconds after each scene loads and every 20 s after:
    // the audio listeners (and whether they are switched on), the master volume and pause, the output settings and
    // how many sounds are playing. For tracking down silence on the headset (adb logcat -s Unity | grep Audio).
    public sealed class AudioReport : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            var go = new GameObject("Audio report");
            DontDestroyOnLoad(go);
            go.AddComponent<AudioReport>();
        }

        float _next = 5f;

        void OnEnable() => SceneManager.sceneLoaded += Loaded;
        void OnDisable() => SceneManager.sceneLoaded -= Loaded;
        void Loaded(Scene s, LoadSceneMode m) => _next = Time.unscaledTime + 5f;

        void Update()
        {
            if (Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + 20f;
            var sb = new StringBuilder("[Audio] ");
            sb.Append("scene ").Append(SceneManager.GetActiveScene().name);
            var listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.Append(" | listeners ").Append(listeners.Length).Append(':');
            foreach (var l in listeners)
                sb.Append(' ').Append(l.name).Append(l.isActiveAndEnabled ? "(on)" : "(OFF)");
            var cfg = AudioSettings.GetConfiguration();
            sb.Append(" | volume ").Append(AudioListener.volume).Append(" paused ").Append(AudioListener.pause)
              .Append(" | ").Append(cfg.sampleRate).Append(" Hz ").Append(cfg.speakerMode).Append(" dsp ").Append(AudioSettings.dspTime.ToString("0.0"));
            int playing = 0, total = 0, silentPlaying = 0;
            foreach (var s in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                total++;
                if (s.isPlaying)
                {
                    playing++;
                    if (s.volume < 0.01f || s.mute || s.clip == null)
                        silentPlaying++;
                }
            }
            sb.Append(" | sources ").Append(total).Append(", playing ").Append(playing).Append(" (silent ").Append(silentPlaying).Append(')');
            sb.Append(" | timeScale ").Append(Time.timeScale);
            Debug.Log(sb.ToString());
        }
    }
}
