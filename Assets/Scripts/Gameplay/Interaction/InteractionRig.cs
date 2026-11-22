using KR.Gameplay.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Interaction
{
    // Gives the player hands that can hold things when the story scene loads: two tracked hands on the headset, or
    // one keyboard-and-mouse hand on a desktop. Switches if the headset comes or goes.
    public sealed class InteractionRig : MonoBehaviour
    {
        PlayerRig _rig;
        bool _vr;
        HandInteractor _left, _right;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var rig = Object.FindFirstObjectByType<PlayerRig>();
            if (rig != null && rig.GetComponent<InteractionRig>() == null)
                rig.gameObject.AddComponent<InteractionRig>();
        }

        void Start()
        {
            _rig = GetComponent<PlayerRig>();
            Make(VrRig.Active);
        }

        void Update()
        {
            if (VrRig.Active != _vr)
                Make(VrRig.Active);
        }

        void Make(bool vr)
        {
            if (_left != null) Destroy(_left.gameObject);
            if (_right != null) Destroy(_right.gameObject);
            _left = null;
            _vr = vr;
            if (vr)
            {
                _left = HandInteractor.Create(transform, "Left hand (grab)", true, new VrHandDriver(_rig, true));
                _right = HandInteractor.Create(transform, "Right hand (grab)", false, new VrHandDriver(_rig, false));
            }
            else
            {
                var driver = new DesktopHandDriver(_rig);
                _right = HandInteractor.Create(transform, "Hand (grab, G)", false, driver);
                driver.Hand = _right;
            }
        }
    }
}
