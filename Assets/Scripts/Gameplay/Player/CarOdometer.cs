using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Player
{
    // Mileage of the player's car for the instrument cluster: the odometer (total, kept between sessions in
    // PlayerPrefs) and the trip (since the game started). Counts distance actually driven. Added when the scene loads.
    public sealed class CarOdometer : MonoBehaviour
    {
        const string Key = "kr.car.odometer";

        Rigidbody _rb;
        DrivableCar _car;
        double _total;
        float _saveTimer;

        public double TotalKm => _total / 1000.0;
        public float TripKm { get; private set; }

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
            if (rig != null && rig.Car != null && rig.Car.GetComponent<CarOdometer>() == null)
                rig.Car.gameObject.AddComponent<CarOdometer>();
        }

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _car = GetComponent<DrivableCar>();
            _total = PlayerPrefs.GetFloat(Key, 0f);
        }

        void FixedUpdate()
        {
            if (_rb == null || _car == null || !_car.Driven)
                return;
            float v = _rb.linearVelocity.magnitude;
            if (v < 0.3f || v > 60f)
                return;   // standing (suspension jitter) or a teleport
            float d = v * Time.fixedDeltaTime;
            _total += d;
            TripKm += d / 1000f;
            _saveTimer += Time.fixedDeltaTime;
            if (_saveTimer > 10f)
                Save();
        }

        void Save()
        {
            _saveTimer = 0f;
            PlayerPrefs.SetFloat(Key, (float)_total);
            PlayerPrefs.Save();
        }

        void OnDisable() => Save();
    }
}
