using UnityEngine;

namespace KR.Gameplay.World
{
    // The city map image (drawn at build time from the OSM roads and building footprints of the play area) and
    // where it sits in the world, so map views can place the player, the route and destinations on it.
    public sealed class CityMap : MonoBehaviour
    {
        [SerializeField] Texture2D texture;
        [SerializeField] Vector2 min;      // world x, z of the image's bottom-left corner
        [SerializeField] Vector2 size;     // metres covered by the image (x, z)

        public static CityMap Instance { get; private set; }
        public Texture2D Texture => texture;
        public Vector2 SizeMetres => size;

        public void Configure(Texture2D image, Vector2 worldMin, Vector2 worldSize)
        {
            texture = image;
            min = worldMin;
            size = worldSize;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // 0..1 across the image (x east, y north).
        public Vector2 ToUV(Vector3 world) => new Vector2((world.x - min.x) / size.x, (world.z - min.y) / size.y);
    }
}
