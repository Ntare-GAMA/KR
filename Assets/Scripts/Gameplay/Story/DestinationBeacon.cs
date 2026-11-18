using KR.Gameplay.Player;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // Marks exactly where the chosen destination is: a tall, soft column of light standing at its door, visible over
    // the rooftops from a few hundred metres, with its name on a board at eye height when you are close. Hidden once
    // you have arrived and when there is no destination.
    public sealed class DestinationBeacon : MonoBehaviour
    {
        [SerializeField] Navigator nav;
        [SerializeField] PlayerRig rig;
        [SerializeField] Material material;
        [SerializeField] float height = 30f;

        Transform _column;
        SpeechBubble _label;

        public void Configure(Navigator navigator, PlayerRig playerRig, Material glow)
        {
            nav = navigator;
            rig = playerRig;
            material = glow;
        }

        void Start()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Destination column";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(0.7f, height * 0.5f, 0.7f);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _column = go.transform;
            _label = SpeechBubble.Create(transform);
            go.SetActive(false);
        }

        void LateUpdate()
        {
            if (nav == null || _column == null)
                return;
            var dest = nav.Destination;
            bool show = dest != null && nav.Remaining > 8f;
            if (_column.gameObject.activeSelf != show)
                _column.gameObject.SetActive(show);
            if (!show)
                return;
            _column.position = dest.Value + Vector3.up * (height * 0.5f);
            if (nav.Remaining < 60f && !string.IsNullOrEmpty(nav.DestinationName))
                _label.Say(_column, -height * 0.5f + 2.6f, nav.DestinationName, 0.3f);
        }
    }
}
