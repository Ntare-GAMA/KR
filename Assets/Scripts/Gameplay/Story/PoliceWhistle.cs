using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // A traffic officer's whistle, every few seconds while you are near enough to hear it.
    public sealed class PoliceWhistle : MonoBehaviour
    {
        [SerializeField] float hearing = 45f;
        [SerializeField] Vector2 every = new Vector2(5f, 12f);

        float _next;

        void OnEnable() => _next = Time.time + Random.Range(every.x, every.y);

        void Update()
        {
            if (Time.time < _next)
                return;
            _next = Time.time + Random.Range(every.x, every.y);
            var you = RoadNetworkTraffic.Player;
            if (you != null && (you.position - transform.position).sqrMagnitude < hearing * hearing)
                SoundBank.Play("police_whistle", transform.position + Vector3.up * 1.6f, 0.8f, true, hearing + 10f);
        }
    }
}
