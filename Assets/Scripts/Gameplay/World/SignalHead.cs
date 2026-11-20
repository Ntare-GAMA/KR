using UnityEngine;

namespace KR.Gameplay.World
{
    // One traffic-signal head (three lenses) on its pole. The signal system finds every head in the scene when the
    // game starts, so a pole moved by hand in the editor takes its head (and its live phase) with it.
    public sealed class SignalHead : MonoBehaviour
    {
        public Renderer red, amber, green;
        public int junction;
        public int phase;
    }
}
