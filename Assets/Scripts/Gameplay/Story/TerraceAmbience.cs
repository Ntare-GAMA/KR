using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // Cafe chatter (cups, voices) at whichever terrace is nearest you, within earshot. One looping source moves
    // between terraces, so the whole city costs a single voice.
    public sealed class TerraceAmbience : MonoBehaviour
    {
        [SerializeField] float hearing = 28f, volume = 0.5f;

        AudioSource _src;
        Transform _at;
        float _next;

        void Start() => _src = SoundBank.Loop(transform, "cafe", true, 3f, hearing);

        void Update()
        {
            if (_src == null)
                return;
            var you = RoadNetworkTraffic.Player;
            if (Time.time >= _next && you != null)
            {
                _next = Time.time + 0.5f;
                Transform best = null;
                float bd = hearing * hearing;
                var tables = ServiceTable.Tables;
                for (int i = 0; i < tables.Count; i++)
                {
                    float d = (tables[i].transform.position - you.position).sqrMagnitude;
                    if (d < bd)
                    {
                        bd = d;
                        best = tables[i].transform;
                    }
                }
                // Move only while silent, so the sound never jumps from one cafe to the next.
                if (best != _at && _src.volume <= 0.01f)
                {
                    _at = best;
                    if (_at != null)
                        _src.transform.position = _at.position + Vector3.up;
                }
            }
            SoundBank.Fade(_src, _at != null && _at == Current(you) ? volume : 0f, 0.8f);
        }

        Transform Current(Transform you)
        {
            if (_at == null || you == null)
                return null;
            return (_at.position - you.position).sqrMagnitude < hearing * hearing ? _at : null;
        }
    }
}
