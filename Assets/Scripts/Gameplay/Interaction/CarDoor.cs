using KR.Gameplay.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Interaction
{
    // The driver's door of your car opens with your hand: take the handle, pull, and the door swings out with your
    // hand; past 40 degrees you get in and the door shuts behind you. The car model's front-left door meshes
    // (Door_FL_*, the driver's side in Rwanda) are hung on a hinge made at their front edge. A (E on a desktop)
    // still gets you in as before.
    public sealed class CarDoor : MonoBehaviour
    {
        PlayerRig _rig;
        Transform _hinge;
        DoorHandle _handle;
        float _closeAt;

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
            if (rig != null && rig.Car != null && rig.Car.GetComponent<CarDoor>() == null)
                rig.Car.gameObject.AddComponent<CarDoor>();
        }

        void Start()
        {
            _rig = FindFirstObjectByType<PlayerRig>();
            var car = transform;
            // The driver's door meshes and their extent in the car's own space.
            var parts = new System.Collections.Generic.List<Transform>();
            Bounds local = default;
            bool any = false;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!r.name.StartsWith("Door_FL"))
                    continue;
                parts.Add(r.transform);
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = car.InverseTransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                    else local.Encapsulate(p);
                }
            }
            if (!any)
            {
                Debug.LogWarning("[CarDoor] No Door_FL parts on the car; getting in stays on the A button.");
                enabled = false;
                return;
            }
            // Hinge on the door's front edge, on its outer face. Its local +x runs back along the closed door and its
            // local +z points into the car, so the door swings out towards local -z (DoorHandle sign -1).
            _hinge = new GameObject("Driver door hinge").transform;
            _hinge.SetParent(car, false);
            _hinge.localPosition = new Vector3(local.min.x, local.center.y, local.max.z);
            _hinge.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            foreach (var t in parts)
                t.SetParent(_hinge, true);
            // The handle: towards the back of the door, a little above its middle, just outside it.
            var grip = new GameObject("Driver door handle").transform;
            grip.SetParent(_hinge, false);
            float length = local.size.z;
            grip.localPosition = new Vector3(length * 0.78f, local.size.y * 0.12f, -0.06f);
            var trigger = grip.gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(0.3f, 0.2f, 0.22f);
            _handle = grip.gameObject.AddComponent<DoorHandle>();
            _handle.Configure(_hinge, -1f);
            _handle.Opened += _ => GetIn();
        }

        void GetIn()
        {
            if (_rig == null || _rig.CurrentMode != PlayerRig.Mode.Walking)
                return;
            if (_rig.GetInCar())
                _closeAt = Time.time + 0.6f;
        }

        void Update()
        {
            if (_handle == null)
                return;
            // The door shuts once you are in (or if you let go of it half open and walk off).
            bool inside = _rig != null && _rig.CurrentMode == PlayerRig.Mode.Driving;
            if (_closeAt > 0f && Time.time > _closeAt)
            {
                _closeAt = 0f;
                _handle.ResetDoor();
            }
            // Only reachable from outside, while walking.
            _handle.enabled = !inside;
        }
    }
}
