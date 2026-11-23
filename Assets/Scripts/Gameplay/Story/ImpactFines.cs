using KR.Gameplay.Player;
using KR.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Story
{
    // Hitting anything that is not a person or another vehicle (a pole, a wall, a sign, a parked object) is a fine
    // (owner 2026-11-23: "hitting anything else, are fines"). People are CarImpacts and EmergencyResponse; other
    // vehicles are AccidentScene. The road itself (surfaces, kerbs, the ground) never counts, nor a scrape: only a
    // knock at 9 km/h or more, against something standing, once per object every few seconds.
    public sealed class ImpactFines : MonoBehaviour
    {
        const float MinImpact = 2.5f, Cooldown = 4f;

        StoryDirector _director;
        PlayerRig _rig;
        Collider _last;
        float _lastAt = -10f;

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
            var director = Object.FindFirstObjectByType<StoryDirector>();
            if (rig == null || rig.Car == null || director == null || rig.Car.GetComponent<ImpactFines>() != null)
                return;
            var f = rig.Car.gameObject.AddComponent<ImpactFines>();
            f._rig = rig;
            f._director = director;
        }

        void OnCollisionEnter(Collision c)
        {
            if (_director == null || _rig == null || _rig.CurrentMode != PlayerRig.Mode.Driving)
                return;
            if (c.relativeVelocity.magnitude < MinImpact)
                return;
            var col = c.collider;
            if (col == null || col is TerrainCollider || col.isTrigger)
                return;
            var t = col.transform;
            // Other vehicles and people have their own consequences.
            if (col.attachedRigidbody != null || t.GetComponentInParent<RoadNetworkTraffic>() != null || t.GetComponentInParent<PersonAnimator>() != null)
                return;
            // The road, kerbs and footpaths (the generated road group) and anything hit from above (driving over it).
            for (var p = t; p != null; p = p.parent)
                if (p.name.StartsWith("Roads (") || p.name.StartsWith("Road edges"))
                    return;
            if (c.contactCount > 0 && c.GetContact(0).normal.y > 0.6f)
                return;
            if (col == _last && Time.time - _lastAt < Cooldown || Time.time - _lastAt < 1f)
                return;
            _last = col;
            _lastAt = Time.time;
            _director.ChargeFine(RuleId.HitObject);
        }
    }
}
