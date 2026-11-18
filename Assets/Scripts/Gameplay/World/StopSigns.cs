using UnityEngine;

namespace KR.Gameplay.World
{
    // Where the STOP signs are: one stop position per sign, the direction of travel it controls, and the road width
    // there. Pure data for the road-rule monitor and the driving coach; the sign meshes are in the instanced city.
    public sealed class StopSigns : MonoBehaviour
    {
        [SerializeField] Vector3[] points;
        [SerializeField] Vector3[] directions;
        [SerializeField] float[] widths;

        public int Count => points?.Length ?? 0;

        public void Configure(Vector3[] stopPoints, Vector3[] travelDirections, float[] roadWidths)
        {
            points = stopPoints;
            directions = travelDirections;
            widths = roadWidths;
        }

        public void Get(int i, out Vector3 point, out Vector3 direction, out float width)
        {
            point = points[i];
            direction = directions[i];
            width = widths != null && i < widths.Length ? widths[i] : 7f;
        }
    }
}
