using KR.Gameplay.Story;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Player
{
    // The car radio. The station's intro (Resources/Audio/Radio/intro, about 4 minutes) plays first, from the start,
    // once per session; then the playlist (Resources/Audio/Radio/radio) from a random point, looping (owner
    // 2026-11-23: "use them and mix ... once you're done, use the rest of the music"). It plays for the whole game and
    // loops; turning the volume down to 0 switches it off, and that choice is remembered (PlayerPrefs, shared
    // with the main menu and the in-game Settings).
    //   In the car: at the volume you set. Right stick up/down (- and + keys) changes it in steps of 10 %; the
    //   vehicle screen shows the level.
    //   On foot: it carries on quietly in the background.
    //   Quieter while someone is talking to you, inside the restaurant (it has its own music) and while paused.
    // Added to the player's car when the Story scene loads, so the scene does not need rebuilding for it.
    public sealed class CarRadio : MonoBehaviour
    {
        const string VolumeKey = "kr.radio.volume";
        const float DefaultVolume = 0.5f, OnFootShare = 0.3f, TalkShare = 0.2f;

        public static CarRadio Instance { get; private set; }

        // 0..1 in steps of 0.1; 0 = off.
        public static float Volume
        {
            get => PlayerPrefs.GetFloat(VolumeKey, DefaultVolume);
            set
            {
                PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(Mathf.Round(value * 10f) / 10f));
                PlayerPrefs.Save();
            }
        }

        public static string VolumeText => Volume <= 0.001f ? "off" : $"{Mathf.RoundToInt(Volume * 100f)}%";

        // When the volume last changed (for the screen to light up the radio strip).
        public float ChangedAt { get; private set; } = -10f;

        PlayerRig _rig;
        AudioSource _src;
        ConversationRunner _talk;
        Restaurant _restaurant;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var rig = Object.FindFirstObjectByType<PlayerRig>();
            if (rig == null || rig.Car == null || rig.Car.GetComponent<CarRadio>() != null)
                return;
            var clip = Resources.Load<AudioClip>("Audio/Radio/radio");
            var intro = Resources.Load<AudioClip>("Audio/Radio/intro");
            if (clip == null && intro == null)
                return;
            rig.Car.gameObject.AddComponent<CarRadio>().Setup(rig, clip != null ? clip : intro, _introPlayed ? null : intro);
        }

        static bool _introPlayed;
        AudioClip _playlist;

        void Setup(PlayerRig rig, AudioClip clip, AudioClip intro)
        {
            _rig = rig;
            _playlist = clip;
            var go = new GameObject("Radio");
            go.transform.SetParent(transform, false);
            _src = go.AddComponent<AudioSource>();
            _src.clip = intro != null ? intro : clip;
            _src.loop = intro == null;
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;     // in the cabin it is all around you
            _src.priority = 32;
            _src.volume = 0f;
            _talk = FindFirstObjectByType<ConversationRunner>();
            _restaurant = FindFirstObjectByType<Restaurant>();
            Instance = this;
            if (Volume > 0.001f)
                Begin();
        }

        void Begin()
        {
            if (_src.clip != _playlist)
            {
                // The intro, from its start; the playlist follows when it ends.
                _introPlayed = true;
                _src.Play();
                return;
            }
            // A random point of the mix, so every session does not start on the same song.
            _src.Play();
            if (_src.clip.length > 60f)
                _src.time = Random.Range(0f, _src.clip.length - 30f);
        }

        // The intro has ended: on to the playlist.
        void NextClip()
        {
            if (_playlist == null)
            {
                _src.loop = true;   // only the intro is there: it loops
                _src.Play();
                return;
            }
            _src.clip = _playlist;
            _src.loop = true;
            _src.time = 0f;
            Begin();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            if (_src == null || _rig == null)
                return;
            bool driving = _rig.CurrentMode == PlayerRig.Mode.Driving;
            int step = driving ? RigInput.RadioStep : 0;
            if (step != 0)
            {
                Volume += step * 0.1f;
                ChangedAt = Time.unscaledTime;
            }

            float v = Volume;
            float target = v * (driving ? 1f : OnFootShare);
            if (_talk != null && _talk.Talking || _restaurant != null && _restaurant.Inside)
                target *= TalkShare;
            if (GamePause.Paused)
                target *= 0.3f;
            _src.volume = Mathf.MoveTowards(_src.volume, target, Time.unscaledDeltaTime * 0.8f);

            if (v > 0.001f && !_src.isPlaying && _src.clip != _playlist && _introPlayed && _src.time <= 0f && !GamePause.Paused)
                NextClip();
            else if (v > 0.001f && !_src.isPlaying)
            {
                if (_src.time > 0f)
                    _src.UnPause();
                else
                    Begin();
            }
            else if (v <= 0.001f && _src.isPlaying && _src.volume <= 0.001f)
                _src.Pause();
        }
    }
}
