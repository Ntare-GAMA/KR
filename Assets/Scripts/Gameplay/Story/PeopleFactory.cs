using System.Collections.Generic;
using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // Makes extra people (bank staff, nurses, hotel guests...) as copies of the city's own animated people: only
    // bodies that really animate (a valid humanoid avatar and an animation controller) are copied, so nobody
    // slides or stands frozen. The copy keeps only its body and PersonAnimator; no routes or schedules.
    public static class PeopleFactory
    {
        static readonly List<GameObject> Donors = new List<GameObject>();

        static void FindDonors()
        {
            Donors.RemoveAll(d => d == null);
            if (Donors.Count > 0)
                return;
            foreach (var pa in Object.FindObjectsByType<PersonAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var anim = pa.GetComponentInChildren<Animator>(true);
                if (anim == null || anim.runtimeAnimatorController == null || anim.avatar == null || !anim.avatar.isValid || !anim.avatar.isHuman)
                    continue;
                string n = pa.name.ToLowerInvariant();
                if (n.Contains("officer") || n.Contains("police"))
                    continue;   // uniforms stay with the police
                Donors.Add(pa.gameObject);
                if (Donors.Count >= 40)
                    break;
            }
            if (Donors.Count == 0)
                Debug.LogWarning("[People] No animated person in the city to copy.");
        }

        // A person at, facing along facing, under parent; seed picks which body. Null if there is nobody to copy.
        public static PersonAnimator Make(Transform parent, Vector3 at, Vector3 facing, int seed, string name)
        {
            FindDonors();
            if (Donors.Count == 0)
                return null;
            var donor = Donors[Mathf.Abs(seed) % Donors.Count];
            var go = Object.Instantiate(donor, parent);
            go.name = name;
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(mb is PersonAnimator))
                    Object.Destroy(mb);
            facing.y = 0f;
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(facing.sqrMagnitude > 0.01f ? facing : Vector3.forward));
            go.SetActive(true);
            return go.GetComponent<PersonAnimator>();
        }
    }
}
