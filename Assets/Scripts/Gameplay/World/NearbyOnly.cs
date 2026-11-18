using System.Collections.Generic;
using UnityEngine;

namespace KR.Gameplay.World
{
    // Keeps a cluster's children (a shop's staff and customers, a group chatting, a signboard) switched on only while
    // the camera is within range, so hundreds of them across the city cost nothing when you are elsewhere.
    // All clusters are checked by one shared update, a slice per frame.
    public sealed class NearbyOnly : MonoBehaviour
    {
        [SerializeField] float radius = 120f;

        static readonly List<NearbyOnly> All = new List<NearbyOnly>();
        static int _cursor, _frame = -1;
        bool _shown = true;

        public void Configure(float range) => radius = range;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start() => Apply(true);

        void Update()
        {
            // The first cluster to update each frame checks a slice of all of them.
            if (_frame == Time.frameCount || All.Count == 0)
                return;
            _frame = Time.frameCount;
            int slice = Mathf.Max(8, All.Count / 15);
            for (int k = 0; k < slice; k++)
            {
                _cursor = (_cursor + 1) % All.Count;
                All[_cursor].Apply(false);
            }
        }

        void Apply(bool force)
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            Vector3 d = cam.transform.position - transform.position;
            bool show = d.x * d.x + d.z * d.z < radius * radius;
            if (show == _shown && !force)
                return;
            _shown = show;
            foreach (Transform child in transform)
                child.gameObject.SetActive(show);
        }
    }
}
