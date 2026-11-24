using System.Collections.Generic;
using KR.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Player
{
    // People are solid to you on foot. The city's people have no colliders, on purpose: hundreds of moving capsules
    // would cost the Quest, and the traffic and the grounding raycasts must not hit them. Instead, each frame, after
    // you have moved, you are pushed back out of anyone you walked into (a 0.3 m circle round them), through the
    // CharacterController so walls still hold. Seated people, drivers and anyone hidden do not count.
    [DefaultExecutionOrder(100)]   // after PlayerMotor
    public sealed class PeopleBlocker : MonoBehaviour
    {
        const float PersonRadius = 0.3f, Refresh = 4f, Near = 6f;

        PlayerRig _rig;
        CharacterController _controller;
        readonly List<Transform> _people = new List<Transform>(512);
        float _refreshAt;

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
            if (rig == null)
                return;
            // The walking body is its own object (not under the rig), so take it from the rig.
            var cc = rig.Player != null ? rig.Player.GetComponent<CharacterController>() : rig.GetComponentInChildren<CharacterController>(true);
            if (cc != null && cc.GetComponent<PeopleBlocker>() == null)
                cc.gameObject.AddComponent<PeopleBlocker>();
        }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _rig = GetComponentInParent<PlayerRig>();
            if (_rig == null)
                _rig = FindFirstObjectByType<PlayerRig>();
        }

        void Collect()
        {
            _people.Clear();
            foreach (var pa in FindObjectsByType<PersonAnimator>(FindObjectsSortMode.None))
            {
                var t = pa.transform;
                // Drivers and passengers ride inside vehicles, which have their own colliders.
                if (t.GetComponentInParent<Rigidbody>() != null || t.GetComponentInParent<RoadNetworkTraffic>() != null)
                    continue;
                if (t.IsChildOf(transform))
                    continue;
                _people.Add(t);
            }
            if (_refreshAt == 0f)
                Debug.Log($"[People] {_people.Count} people are solid to you on foot.");
            _refreshAt = Time.time + Refresh;
        }

        void LateUpdate()
        {
            if (_controller == null || !_controller.enabled || _rig == null || _rig.CurrentMode == PlayerRig.Mode.Driving)
                return;
            if (Time.time >= _refreshAt)
                Collect();

            var me = transform.position;
            float reach = PersonRadius + _controller.radius;
            var push = Vector3.zero;
            for (int i = 0; i < _people.Count; i++)
            {
                var t = _people[i];
                if (t == null || !t.gameObject.activeInHierarchy)
                    continue;
                var p = t.position;
                float dx = me.x - p.x, dz = me.z - p.z;
                if (dx > Near || dx < -Near || dz > Near || dz < -Near)
                    continue;
                // Only someone at about your height (not on a balcony above or a road below).
                if (Mathf.Abs(p.y - (me.y + _controller.center.y - _controller.height * 0.5f)) > 1.2f)
                    continue;
                float d2 = dx * dx + dz * dz;
                if (d2 >= reach * reach)
                    continue;
                float d = Mathf.Sqrt(d2);
                var away = d > 0.001f ? new Vector3(dx / d, 0f, dz / d) : -transform.forward;
                push += away * (reach - d);
            }
            if (push.sqrMagnitude > 1e-6f)
                _controller.Move(push);
        }
    }
}
