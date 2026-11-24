using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.World
{
    // Parked cars were placed level, so on Kigali's slopes one end floated. When the scene loads, each parked car
    // is tilted to the ground under its four wheels and set down on it, a few hundred cars per frame so loading
    // does not stall.
    public sealed class ParkedCarSettler : MonoBehaviour
    {
        const int PerFrame = 200;
        static readonly RaycastHit[] Hits = new RaycastHit[8];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var layer = GameObject.Find("Parked cars");
            if (layer != null && layer.GetComponent<ParkedCarSettler>() == null)
                layer.AddComponent<ParkedCarSettler>();
        }

        static Transform _layer;   // no parked car is ground for another

        IEnumerator Start()
        {
            _layer = transform;
            var cars = new List<Transform>();
            foreach (Transform area in transform)
                foreach (Transform car in area)
                    cars.Add(car);
            int moved = 0;
            for (int i = 0; i < cars.Count; i++)
            {
                if (Settle(cars[i]))
                    moved++;
                if (i % PerFrame == PerFrame - 1)
                    yield return null;
            }
            Physics.SyncTransforms();
            Debug.Log($"[ParkedCars] {moved} of {cars.Count} parked cars set down on the ground under their wheels.");
        }

        static bool Settle(Transform car)
        {
            var rs = car.GetComponentsInChildren<MeshRenderer>();
            if (rs.Length == 0)
                return false;
            var b = Bounds(rs);
            var fwd = Vector3.ProjectOnPlane(car.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f)
                return false;
            // The long side is the length (some models are turned 90 degrees inside their holder).
            var right = Vector3.Cross(Vector3.up, fwd);
            float alongF = Mathf.Abs(Vector3.Dot(b.extents, Abs(fwd))), alongR = Mathf.Abs(Vector3.Dot(b.extents, Abs(right)));
            if (alongR > alongF)
            {
                (fwd, right) = (right, -fwd);
                (alongF, alongR) = (alongR, alongF);
            }
            float hl = alongF * 0.72f, hw = alongR * 0.78f;
            var c = b.center;
            float top = b.max.y + 0.5f;
            if (!Ground(c + fwd * hl + right * hw, top, car, out float fr) || !Ground(c + fwd * hl - right * hw, top, car, out float fl) ||
                !Ground(c - fwd * hl + right * hw, top, car, out float br) || !Ground(c - fwd * hl - right * hw, top, car, out float bl))
                return false;
            float front = (fr + fl) * 0.5f, back = (br + bl) * 0.5f, rightY = (fr + br) * 0.5f, leftY = (fl + bl) * 0.5f;
            float pitch = Mathf.Atan2(front - back, hl * 2f) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(rightY - leftY, hw * 2f) * Mathf.Rad2Deg;
            if (Mathf.Abs(pitch) > 20f || Mathf.Abs(roll) > 20f)
                return false;   // a wall or a drop, not a slope: leave it
            // Tilt about the car's centre so its floor lies along the ground, then drop it onto the ground.
            var tilt = Quaternion.AngleAxis(-pitch, right) * Quaternion.AngleAxis(roll, fwd);
            car.SetPositionAndRotation(c + tilt * (car.position - c), tilt * car.rotation);
            float groundMid = (front + back) * 0.5f;
            var nb = Bounds(rs);
            float lift = groundMid - nb.min.y;
            if (Mathf.Abs(lift) > 1.5f)
                return false;
            car.position += Vector3.up * lift;
            return true;
        }

        static Bounds Bounds(MeshRenderer[] rs)
        {
            var b = rs[0].bounds;
            foreach (var r in rs)
                if (r.enabled && !r.name.StartsWith("LOD1"))
                    b.Encapsulate(r.bounds);
            return b;
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        // Ground under p (not the car itself, not another vehicle).
        static bool Ground(Vector3 p, float from, Transform car, out float y)
        {
            y = 0f;
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, from, p.z), Vector3.down, Hits, 12f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider.transform.IsChildOf(car) || (_layer != null && h.collider.transform.IsChildOf(_layer)) || h.collider.attachedRigidbody != null || h.normal.y < 0.6f || h.collider is CapsuleCollider)
                    continue;
                if (h.point.y > best)
                    best = h.point.y;
            }
            if (best == float.MinValue)
                return false;
            y = best;
            return true;
        }
    }
}
