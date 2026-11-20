using System.Collections.Generic;
using UnityEngine;

namespace KR.Gameplay.World
{
    // Where a street lamp's light comes from (the end of its arm). The lamp pool lights the heads nearest the camera,
    // wherever the lamps have been moved by hand.
    public sealed class LampHead : MonoBehaviour
    {
        public static readonly List<LampHead> All = new List<LampHead>();

        void Awake() => All.Add(this);
        void OnDestroy() => All.Remove(this);
    }
}
