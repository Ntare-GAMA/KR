using System;
using UnityEngine;

namespace KR.Gameplay.World
{
    // Two-phase signal timing per junction (phase 0 = the junction's through road, phase 1 = the cross road), with
    // an all-red gap between phases. Cars and the road-rule monitor query State(); each approach has a three-lamp
    // head whose lit lens follows the live phase. Heads beyond drawDistance are hidden (they are unreadable there).
    public sealed class TrafficSignalSystem : MonoBehaviour
    {
        [Serializable]
        public sealed class Lamp
        {
            public GameObject head;
            public Renderer red, amber, green;
            public int junction;
            public int phase;
            [NonSerialized] public int shown;   // state + 1; 0 = not drawn yet
        }

        public const int Red = 0, Amber = 1, Green = 2;

        [SerializeField] float[] offsets;
        [SerializeField] Lamp[] lamps;
        [SerializeField, Tooltip("Lit lens materials: red, amber, green.")] Material[] litMaterials;
        [SerializeField] Material offMaterial;
        [SerializeField] float green = 18f;
        [SerializeField] float amber = 3f;
        [SerializeField] float allRed = 2f;
        [SerializeField] float drawDistance = 220f;

        float _refresh;

        float HalfCycle => green + amber + allRed;
        public int JunctionCount => offsets?.Length ?? 0;

        public void Configure(float[] junctionOffsets, Lamp[] lampList, Material[] lit, Material off)
        {
            offsets = junctionOffsets;
            lamps = lampList;
            litMaterials = lit;
            offMaterial = off;
        }

        // Heads placed as SignalHead components (on their poles, editable by hand) replace the built list.
        void Awake()
        {
            var heads = FindObjectsByType<SignalHead>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (heads.Length == 0)
                return;
            lamps = new Lamp[heads.Length];
            for (int i = 0; i < heads.Length; i++)
                lamps[i] = new Lamp { head = heads[i].gameObject, red = heads[i].red, amber = heads[i].amber, green = heads[i].green,
                                      junction = heads[i].junction, phase = heads[i].phase };
        }

        public int State(int junction, int phase)
        {
            if (offsets == null || junction < 0 || junction >= offsets.Length)
                return Green;
            float t = Mathf.Repeat(Time.time + offsets[junction], HalfCycle * 2f);
            float local = phase == 0 ? t : Mathf.Repeat(t - HalfCycle, HalfCycle * 2f);
            return local < green ? Green : local < green + amber ? Amber : Red;
        }

        // Seconds until this approach next changes colour (for the driving coach).
        public float SecondsToChange(int junction, int phase)
        {
            if (offsets == null || junction < 0 || junction >= offsets.Length)
                return 99f;
            float t = Mathf.Repeat(Time.time + offsets[junction], HalfCycle * 2f);
            float local = phase == 0 ? t : Mathf.Repeat(t - HalfCycle, HalfCycle * 2f);
            return local < green ? green - local : local < green + amber ? green + amber - local : HalfCycle * 2f - local;
        }

        void Update()
        {
            _refresh -= Time.deltaTime;
            if (_refresh > 0f || lamps == null || litMaterials == null || litMaterials.Length < 3)
                return;
            _refresh = 0.1f;
            var cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            float far2 = drawDistance * drawDistance;
            foreach (var lamp in lamps)
            {
                if (lamp.head == null)
                    continue;
                bool near = cam == null || (lamp.head.transform.position - eye).sqrMagnitude < far2;
                if (lamp.head.activeSelf != near)
                {
                    lamp.head.SetActive(near);
                    lamp.shown = 0;
                }
                if (!near)
                    continue;
                int s = State(lamp.junction, lamp.phase);
                if (s + 1 == lamp.shown)
                    continue;
                lamp.shown = s + 1;
                // Swapping shared materials keeps every lens on the SRP Batcher (no per-renderer property blocks).
                lamp.red.sharedMaterial = s == Red ? litMaterials[0] : offMaterial;
                lamp.amber.sharedMaterial = s == Amber ? litMaterials[1] : offMaterial;
                lamp.green.sharedMaterial = s == Green ? litMaterials[2] : offMaterial;
            }
        }
    }
}
