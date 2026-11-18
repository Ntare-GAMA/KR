using System;
using UnityEngine;

namespace KR.Gameplay.World
{
    // People crossing at the zebra crossings of signalised junctions. Each person waits on the kerb and crosses only
    // while the cars on that road have red and there is time to reach the other side; then they wait there a while
    // and cross back later. Their walk animation comes from PersonAnimator (it follows how fast they move).
    public sealed class CrossingWalkers : MonoBehaviour
    {
        [Serializable]
        public sealed class Crossing
        {
            public Vector3 a, b;        // kerb ends, on the footpath surface
            public int junction, phase; // the signal that stops the traffic on the road being crossed
        }

        [Serializable]
        public sealed class Walker
        {
            public Transform body;
            public int crossing;
            public float lateral;       // spread along the kerb so a group does not walk in one file
        }

        [SerializeField] TrafficSignalSystem signals;
        [SerializeField] Crossing[] crossings;
        [SerializeField] Walker[] walkers;
        [SerializeField] float speed = 1.35f;

        bool[] _onB;
        float[] _progress;      // < 0 waiting; 0..1 crossing
        float[] _readyAt;

        public void Configure(TrafficSignalSystem signalSystem, Crossing[] list, Walker[] people)
        {
            signals = signalSystem;
            crossings = list;
            walkers = people;
        }

        void Awake()
        {
            if (signals == null || crossings == null || walkers == null || crossings.Length == 0)
            {
                enabled = false;
                return;
            }
            int n = walkers.Length;
            _onB = new bool[n];
            _progress = new float[n];
            _readyAt = new float[n];
            for (int i = 0; i < n; i++)
            {
                _onB[i] = UnityEngine.Random.value < 0.5f;
                _progress[i] = -1f;
                _readyAt[i] = UnityEngine.Random.Range(0f, 20f);
                Place(i, 0f);
            }
        }

        void Update()
        {
            float now = Time.time;
            for (int i = 0; i < walkers.Length; i++)
            {
                var w = walkers[i];
                if (w.body == null)
                    continue;
                var c = crossings[w.crossing];
                float length = Vector3.Distance(c.a, c.b);
                if (_progress[i] < 0f)
                {
                    if (now < _readyAt[i])
                        continue;
                    // Cross only on red for the cars, with time to spare for the whole width.
                    if (signals.State(c.junction, c.phase) == TrafficSignalSystem.Red &&
                        signals.SecondsToChange(c.junction, c.phase) > length / speed + 2f)
                        _progress[i] = 0f;
                    continue;
                }
                _progress[i] += speed * Time.deltaTime / Mathf.Max(1f, length);
                if (_progress[i] >= 1f)
                {
                    _onB[i] = !_onB[i];
                    _progress[i] = -1f;
                    _readyAt[i] = now + UnityEngine.Random.Range(15f, 60f);
                    Place(i, 0f);
                    continue;
                }
                Place(i, _progress[i]);
            }
        }

        void Place(int i, float t)
        {
            var w = walkers[i];
            var c = crossings[w.crossing];
            Vector3 from = _onB[i] ? c.b : c.a, to = _onB[i] ? c.a : c.b;
            Vector3 dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
                return;
            var side = Vector3.Cross(Vector3.up, dir.normalized);
            w.body.position = Vector3.Lerp(from, to, t) + side * w.lateral;
            // Waiting: face the road; crossing: face the far kerb (same direction).
            w.body.rotation = Quaternion.LookRotation(dir);
        }
    }
}
