using System.Collections.Generic;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // The terrace tables of a cafe, restaurant or bar, and who serves at them. Orders taken by that member of staff
    // are brought to the free table nearest you (ServiceOrders).
    public sealed class ServiceTable : MonoBehaviour
    {
        [SerializeField] Npc staff;
        [SerializeField, Tooltip("Where a served item goes on each table (on the table top).")] Transform[] tops;

        static readonly List<ServiceTable> All = new List<ServiceTable>();

        public IReadOnlyList<Transform> Tops => tops;
        public static IReadOnlyList<ServiceTable> Tables => All;

        public void Configure(Npc server, Transform[] tableTops)
        {
            staff = server;
            tops = tableTops;
        }

        void Awake() => All.Add(this);
        void OnDestroy() => All.Remove(this);

        public static ServiceTable For(Npc server)
        {
            foreach (var t in All)
                if (t.staff == server)
                    return t;
            return null;
        }

        public Transform NearestTop(Vector3 to)
        {
            Transform best = null;
            float bestD = float.MaxValue;
            if (tops == null)
                return null;
            foreach (var t in tops)
            {
                if (t == null)
                    continue;
                float d = (t.position - to).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = t;
                }
            }
            return best;
        }
    }
}
