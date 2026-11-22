using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.World
{
    // People who stand still (staff, people chatting, the story's characters) were placed on the ground as it was
    // when the city was built; where a floor or a plinth was added later (the new ALU halls), their feet ended up
    // under it (owner, 2026-11-22: "people who are in the ground"). Once the scene has loaded, each standing person
    // is set on the walkable surface under them, if that is up to a metre above their feet. People moved by a
    // script (walkers, wanderers, traffic) keep their own placement.
    public sealed class PeopleGrounder : MonoBehaviour
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
            if (Object.FindFirstObjectByType<PeopleGrounder>() == null && Object.FindFirstObjectByType<RoadNetworkTraffic>() != null)
                new GameObject("People grounder").AddComponent<PeopleGrounder>();
        }

        IEnumerator Start()
        {
            yield return new WaitForSeconds(1f);   // after the city and the story have placed everyone
            int lifted = 0, n = 0;
            foreach (var pa in FindObjectsByType<PersonAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var t = pa.transform;
                if (t.GetComponentInParent<RoadNetworkTraffic>() != null || t.GetComponentInParent<Wanderer>() != null ||
                    t.GetComponentInParent<StreetWalker>() != null || t.GetComponentInParent<CrossingWalkers>() != null)
                    continue;
                n++;
                if (Lift(t))
                    lifted++;
            }
            Debug.Log($"[People] {lifted} of {n} standing people lifted onto the floor under them.");
            Destroy(gameObject);
        }

        static bool Lift(Transform person)
        {
            var p = person.position;
            // The floor is the surface most of five samples agree on (under them and half a metre round), so a desk
            // or a stall on one side does not count, and a desk they stand behind does not stop the lift.
            var tops = new[] { Top(person, p), Top(person, p + Vector3.forward * 0.5f), Top(person, p + Vector3.back * 0.5f),
                               Top(person, p + Vector3.left * 0.5f), Top(person, p + Vector3.right * 0.5f) };
            float best = float.MinValue;
            foreach (float a in tops)
            {
                int agree = 0;
                foreach (float b in tops)
                    if (Mathf.Abs(a - b) < 0.12f)
                        agree++;
                if (agree >= 3 && (best == float.MinValue || a < best))
                    best = a;   // of the agreeing surfaces, the lowest: a floor, never a table top
            }
            if (best == float.MinValue || best <= p.y + 0.05f || best > p.y + 1.0f)
                return false;
            person.position = new Vector3(p.x, best, p.z);
            return true;
        }

        // The highest walkable surface from 1.2 m above p down to 0.2 m below it (not people, not vehicles).
        static float Top(Transform person, Vector3 p)
        {
            float best = float.MinValue;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, person.position.y + 1.2f, p.z), Vector3.down, 1.4f, ~(1 << 2), QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(person) || h.collider is CapsuleCollider || h.collider is CharacterController ||
                    h.collider.attachedRigidbody != null || h.normal.y < 0.7f)
                    continue;
                if (h.point.y > best)
                    best = h.point.y;
            }
            return best;
        }
    }
}
