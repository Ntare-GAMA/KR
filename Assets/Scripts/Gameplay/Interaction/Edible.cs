using System;
using UnityEngine;

namespace KR.Gameplay.Interaction
{
    // Food or drink you eat by bringing it to your mouth: while held, each time it comes within reach of your mouth
    // (a little below your eyes) it is one bite or sip. Pull it away and bring it back for the next one.
    [RequireComponent(typeof(Grabbable))]
    public sealed class Edible : MonoBehaviour
    {
        const float AwayAgainExtra = 0.11f;   // pull it this much further away before the next bite

        public event Action<Edible> Bitten;

        Grabbable _grab;
        bool _atMouth;

        void Awake() => _grab = GetComponent<Grabbable>();

        void Update()
        {
            var cam = Camera.main;
            if (!_grab.Held || cam == null)
            {
                _atMouth = false;
                return;
            }
            var mouth = cam.transform.position - cam.transform.up * 0.09f + cam.transform.forward * 0.05f;
            float d = Vector3.Distance(_grab.GripPoint, mouth);
            float reach = InteractionTuning.Current.mouthReach;
            if (!_atMouth && d < reach)
            {
                _atMouth = true;
                _grab.Hand.Pulse(0.2f, 0.04f);
                Bitten?.Invoke(this);
            }
            else if (_atMouth && d > reach + AwayAgainExtra)
                _atMouth = false;
        }
    }
}
