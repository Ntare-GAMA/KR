using UnityEngine;

namespace KR.Gameplay.Player
{
    // Teleport walking (a comfort option in Settings, for players who feel sick with smooth movement).
    // Push the left stick forward: an arc from the left hand shows where you would land; let go to step there.
    // Landing needs walkable ground within reach that is not too steep and not inside a wall.
    public sealed class Teleporter : MonoBehaviour
    {
        [SerializeField] PlayerRig rig;
        [SerializeField] Material arcMaterial;
        [SerializeField] float launchSpeed = 8f;
        [SerializeField] float maxStep = 9f;

        LineRenderer _arc;
        readonly Vector3[] _points = new Vector3[40];
        bool _aiming, _valid;
        Vector3 _target;
        int _ground;

        public void Configure(PlayerRig playerRig, Material material)
        {
            rig = playerRig;
            arcMaterial = material;
        }

        // Any floor you could walk on: the roads and the ground, and also building, restaurant and campus floors.
        // Never a vehicle, a person, a slope steeper than 35 degrees or a small piece of furniture (a table, a
        // chair, a bench top).
        bool Walkable(RaycastHit hit)
        {
            var col = hit.collider;
            if (Vector3.Angle(hit.normal, Vector3.up) >= 35f || col.isTrigger || col.attachedRigidbody != null ||
                col is CapsuleCollider || col is CharacterController)
                return false;
            if (((1 << col.gameObject.layer) & _ground) != 0)
                return true;
            var size = col.bounds.size;
            return size.x >= 2.5f || size.z >= 2.5f;
        }

        void Start()
        {
            _ground = LayerMask.GetMask("KR Ground");
            if (_ground == 0)
                _ground = ~(1 << 2);
            var go = new GameObject("Teleport arc");
            go.transform.SetParent(transform, false);
            _arc = go.AddComponent<LineRenderer>();
            _arc.sharedMaterial = arcMaterial;
            _arc.widthMultiplier = 0.02f;
            _arc.positionCount = 0;
            _arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _arc.receiveShadows = false;
        }

        void Update()
        {
            bool active = Comfort.Teleport && VrRig.Active && rig != null && rig.CurrentMode == PlayerRig.Mode.Walking &&
                          !rig.Busy && !GamePause.Paused && rig.VrLeftHand != null;
            if (!active)
            {
                _aiming = false;
                _arc.positionCount = 0;
                return;
            }
            float push = RigInput.Move.y;
            if (push > 0.6f)
            {
                _aiming = true;
                Aim(rig.VrLeftHand);
            }
            else if (_aiming && push < 0.3f)
            {
                _aiming = false;
                _arc.positionCount = 0;
                if (_valid)
                {
                    VrRig.Fade(0.2f);
                    var motor = rig.Player;
                    motor.Teleport(_target, motor.transform.eulerAngles.y);
                }
            }
        }

        // A thrown arc, stopped by the first thing it hits.
        void Aim(Transform hand)
        {
            Vector3 p = hand.position, v = hand.forward * launchSpeed;
            int n = 0;
            _valid = false;
            _points[n++] = p;
            for (int i = 1; i < _points.Length; i++)
            {
                Vector3 next = p + v * 0.05f;
                v += Physics.gravity * 0.05f;
                if (Physics.Linecast(p, next, out var hit, ~(1 << 2), QueryTriggerInteraction.Ignore))
                {
                    _points[n++] = hit.point;
                    bool walkable = Walkable(hit);
                    var from = rig.Player.transform.position;
                    bool close = Vector2.Distance(new Vector2(hit.point.x, hit.point.z), new Vector2(from.x, from.z)) <= maxStep;
                    bool clear = !Physics.CheckCapsule(hit.point + Vector3.up * 0.4f, hit.point + Vector3.up * 1.6f, 0.25f,
                                                       ~(_ground | (1 << 2)), QueryTriggerInteraction.Ignore);
                    _valid = walkable && close && clear;
                    _target = hit.point;
                    break;
                }
                _points[n++] = next;
                p = next;
            }
            _arc.positionCount = n;
            _arc.SetPositions(_points);
            var c = _valid ? new Color(0.3f, 0.9f, 1f, 0.9f) : new Color(1f, 0.35f, 0.3f, 0.7f);
            _arc.startColor = _arc.endColor = c;
        }
    }
}
