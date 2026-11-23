using UnityEngine;

namespace KR.Gameplay.World
{
    // The walkable surface under a point, on any layer (roads, the ALU forecourt and halls, pavements, terrain), for
    // people placed or moved by script. Ignores triggers, vehicles and other people (rigidbodies, capsules), steep
    // faces (walls, kerb sides) and anything more than `above` over the point, so a roof or a canopy never counts.
    public static class Floor
    {
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        // The highest such surface from `above` over p down to `below` under it; p itself if there is none.
        public static Vector3 At(Vector3 p, float above = 1.0f, float below = 4f)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + above, p.z), Vector3.down, Hits, above + below, ~(1 << 2), QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider.attachedRigidbody != null || h.normal.y < 0.7f || h.collider is CapsuleCollider || h.collider is CharacterController)
                    continue;
                if (h.point.y > best)
                    best = h.point.y;
            }
            return best > float.MinValue ? new Vector3(p.x, best, p.z) : p;
        }
    }
}
