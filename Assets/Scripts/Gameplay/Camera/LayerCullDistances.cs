using UnityEngine;

namespace KR.Gameplay.CameraRig
{
    // Camera.layerCullDistances is not serialized, so it is applied at runtime.
    // Lets small props stop rendering well before the far plane that distant terrain needs.
    // Several components on one camera combine: each sets only its own layer.
    [RequireComponent(typeof(Camera))]
    public sealed class LayerCullDistances : MonoBehaviour
    {
        [SerializeField] string layerName = "StreetDressing";
        [SerializeField] float distance = 380f;

        public void Configure(string layer, float cullDistance)
        {
            layerName = layer;
            distance = cullDistance;
        }

        void Awake()
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
                return;
            var cam = GetComponent<Camera>();
            var distances = cam.layerCullDistances;
            distances[layer] = distance;
            cam.layerCullDistances = distances;
        }
    }
}
