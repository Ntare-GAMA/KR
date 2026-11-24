using KR.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Player
{
    // While you are out of your car, traffic treats it as an obstacle: it stops behind it and passes it in the other
    // lane, instead of driving through it. While you drive it, the traffic already gives way to it as the player.
    public sealed class ParkedCarObstacle : MonoBehaviour
    {
        PlayerRig _rig;
        bool _registered;

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
            if (rig != null && rig.Car != null && rig.Car.GetComponent<ParkedCarObstacle>() == null)
                rig.Car.gameObject.AddComponent<ParkedCarObstacle>()._rig = rig;
        }

        void Update()
        {
            bool parked = _rig != null && _rig.CurrentMode != PlayerRig.Mode.Driving;
            if (parked == _registered)
                return;
            _registered = parked;
            if (parked)
                RoadNetworkTraffic.AddObstacle(transform, 1.0f);
            else
                RoadNetworkTraffic.RemoveObstacle(transform);
        }

        void OnDestroy() => RoadNetworkTraffic.RemoveObstacle(transform);
    }
}
