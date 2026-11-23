using KR.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Player
{
    // Keeps the rain out of the player's car: an invisible trigger box the size of the car body; any raindrop that
    // reaches it disappears, so none fall through the roof onto the seats and the dashboard. On the Ignore Raycast
    // layer and a trigger, so it never touches the car's physics, the traffic or any ground check.
    public sealed class CarRainShelter : MonoBehaviour
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
            var rig = Object.FindFirstObjectByType<PlayerRig>();
            var weather = Object.FindFirstObjectByType<WeatherCycle>();
            if (rig == null || rig.Car == null || weather == null || rig.Car.GetComponent<CarRainShelter>() != null)
                return;
            rig.Car.gameObject.AddComponent<CarRainShelter>().Build(weather);
        }

        void Build(WeatherCycle weather)
        {
            // The body's extent in the car's own frame, from its meshes.
            var toCar = transform.worldToLocalMatrix;
            var b = new Bounds();
            bool any = false;
            foreach (var mf in GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.GetComponent<Renderer>() == null)
                    continue;
                var m = toCar * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var c = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents,
                        new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f)));
                    if (!any) { b = new Bounds(c, Vector3.zero); any = true; }
                    else b.Encapsulate(c);
                }
            }
            if (!any || b.size.x > 4f || b.size.z > 8f)
                b = new Bounds(new Vector3(0f, 0.75f, 0f), new Vector3(1.9f, 1.5f, 4.6f));   // a sedan, if the meshes are odd
            var go = new GameObject("Rain shelter") { layer = 2 };
            go.transform.SetParent(transform, false);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = b.center;
            box.size = b.size + new Vector3(0.1f, 0.15f, 0.1f);
            weather.KeepRainOut(box);
        }
    }
}
