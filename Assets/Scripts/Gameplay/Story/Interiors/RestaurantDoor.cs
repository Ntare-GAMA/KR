using System.Collections.Generic;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // The front door of a cafe or restaurant: its member of staff and where you come back out.
    public sealed class RestaurantDoor : MonoBehaviour
    {
        [SerializeField] Npc staff;
        [SerializeField] string placeName;
        [SerializeField] Vector3 outside;
        [SerializeField] float outsideYaw;

        static readonly List<RestaurantDoor> All = new List<RestaurantDoor>();

        public string PlaceName => placeName;
        public Vector3 Outside => outside;
        public float OutsideYaw => outsideYaw;

        public void Configure(Npc server, string name, Vector3 doorstep, float yaw)
        {
            staff = server;
            placeName = name;
            outside = doorstep;
            outsideYaw = yaw;
        }

        void Awake() => All.Add(this);
        void OnDestroy() => All.Remove(this);

        public static RestaurantDoor For(Npc server)
        {
            foreach (var d in All)
                if (d.staff == server)
                    return d;
            return null;
        }
    }
}
