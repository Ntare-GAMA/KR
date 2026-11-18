using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KR.Gameplay.Player
{
    // Walking: WASD or left stick relative to where you look, Shift or stick click to run. Drives the "Speed"
    // animator parameter, keeps the feet on the real surface (pavement, kerb, road, terrain) and sits the player in
    // a car seat. First person: the body faces the view direction (in VR only once you turn well away or move).
    // The player has no visible character (only hands, see PlayerHands), so the animator is optional.
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int SeatedId = Animator.StringToHash("Seated");
        static readonly int LocomotionHash = Animator.StringToHash("Locomotion");
        static readonly int TiredHash = Animator.StringToHash("IdleTired");
        static readonly int[] IdleBreakHashes =
            { Animator.StringToHash("IdleBreak0"), Animator.StringToHash("IdleBreak1"), Animator.StringToHash("IdleBreak2"), Animator.StringToHash("IdleBreak3") };

        [SerializeField] PlayerTuning settings;
        [SerializeField] Animator animator;
        [SerializeField] Transform view;
        [SerializeField] LayerMask ground = ~0;
        [SerializeField, Tooltip("Height of the ankle bones above the sole.")] float ankleHeight = 0.07f;

        CharacterController _controller;
        float _fall;
        float _speed;
        Transform _body;
        Vector3 _bodyBase;
        float _bodyOffset;
        Transform[] _feet;
        bool _hasSeated;
        Renderer[] _renderers;
        float _idleTime;
        int _idleBreaks;
        bool _hasTired;

        public bool Frozen { get; set; }
        public bool Seated { get; private set; }
        public float SpeedMultiplier { get; set; } = 1f;
        public bool Tired { get; set; }
        // First person: the body turns with the view instead of towards the walking direction.
        public bool FaceView { get; set; } = true;
        // Degrees the view may turn away before a standing body follows (0 = always; VR uses a dead zone).
        public float FaceDeadZone { get; set; }

        public void Configure(PlayerTuning tuning, Animator body, Transform cameraTransform, LayerMask groundMask)
        {
            settings = tuning;
            animator = body;
            view = cameraTransform;
            ground = groundMask;
        }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            Bind();
        }

        void Bind()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _hasSeated = false;
            _idleBreaks = 0;
            if (animator == null)
                return;
            _body = animator.transform;
            _bodyBase = _body.localPosition;
            var feet = new List<Transform>();
            foreach (var t in _body.GetComponentsInChildren<Transform>())
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("foot") || n.Contains("toe"))
                    feet.Add(t);
            }
            _feet = feet.ToArray();
            foreach (var p in animator.parameters)
                if (p.nameHash == SeatedId)
                    _hasSeated = true;
            while (_idleBreaks < IdleBreakHashes.Length && animator.HasState(0, IdleBreakHashes[_idleBreaks]))
                _idleBreaks++;
            _hasTired = animator.HasState(0, TiredHash);
        }

        // ---------- Animation states (built by PlayerBuilder from the Mixamo set) ----------


        public bool HasState(string state) => animator != null && animator.HasState(0, Animator.StringToHash(state));

        // Cross-fades into a named state; false when the controller has no such state (older builds).
        public bool Play(string state, float fade = 0.25f)
        {
            int hash = Animator.StringToHash(state);
            if (animator == null || !animator.isActiveAndEnabled || !animator.HasState(0, hash))
                return false;
            animator.SetFloat(SpeedId, 0f);
            animator.CrossFadeInFixedTime(hash, fade);
            _idleTime = 0f;
            return true;
        }

        public void BackToLocomotion(float fade = 0.3f)
        {
            if (animator != null && animator.isActiveAndEnabled && animator.HasState(0, LocomotionHash))
                animator.CrossFadeInFixedTime(LocomotionHash, fade);
            _idleTime = 0f;
        }

        public void Face(Vector3 target)
        {
            var to = target - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(to);
        }

        // Room-scale: the headset moved the player's head; carry the body along (collides like walking does).
        public void MoveBy(Vector3 delta)
        {
            delta.y = 0f;
            if (Seated || !_controller.enabled || delta.sqrMagnitude < 1e-6f)
                return;
            _controller.Move(delta);
        }

        public void Teleport(Vector3 position, float yaw)
        {
            _controller.enabled = false;
            transform.SetPositionAndRotation(position + Vector3.up * 0.1f, Quaternion.Euler(0f, yaw, 0f));
            _controller.enabled = true;
            _fall = 0f;
        }

        // Sits in the seat (child of the car) with the seated clip; without one the body is hidden instead.
        public void EnterSeat(Transform seat)
        {
            Seated = true;
            Frozen = true;
            _speed = 0f;
            _controller.enabled = false;
            transform.SetParent(seat, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            if (_body != null)
                _body.localPosition = _bodyBase;
            if (animator != null)
            {
                animator.SetFloat(SpeedId, 0f);
                if (_hasSeated)
                    animator.SetBool(SeatedId, true);
            }
            if (!_hasSeated)
                foreach (var r in _renderers)
                    r.enabled = false;
        }

        public void LeaveSeat(Vector3 position, float yaw)
        {
            transform.SetParent(null, true);
            Seated = false;
            Frozen = false;
            if (animator != null && _hasSeated)
                animator.SetBool(SeatedId, false);
            foreach (var r in _renderers)
                r.enabled = true;
            Teleport(position, yaw);
        }

        void Update()
        {
            if (Seated)
                return;
            // Keyboard or left stick; directions are relative to the camera (the headset in VR).
            // Teleport walking (Settings) moves the player in steps instead; the stick only aims.
            Vector2 input = Frozen || (Comfort.Teleport && VrRig.Active) ? Vector2.zero : RigInput.Move;
            bool run = !Frozen && RigInput.Run;

            float yaw = view != null ? view.eulerAngles.y : transform.eulerAngles.y;
            Vector3 move = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y);
            float target = input.magnitude * (run ? settings.runSpeed : settings.walkSpeed) * SpeedMultiplier;
            _speed = Mathf.MoveTowards(_speed, target, 10f * Time.deltaTime);

            if (FaceView && !Frozen)
            {
                // Strafe and back-step without turning; the body faces the view.
                float off = Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, yaw));
                if (move.sqrMagnitude > 0.001f || off > FaceDeadZone)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, yaw, 0f),
                                                          1f - Mathf.Exp(-settings.turnSharpness * Time.deltaTime));
            }
            else if (move.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(move),
                                                      1f - Mathf.Exp(-settings.turnSharpness * Time.deltaTime));

            _fall = _controller.isGrounded ? -2f : _fall - settings.gravity * Time.deltaTime;
            Vector3 dir = FaceView ? (move.sqrMagnitude > 0.001f ? move.normalized : Vector3.zero) : transform.forward;
            Vector3 velocity = dir * _speed + Vector3.up * _fall;
            if (_controller.enabled)
                _controller.Move(velocity * Time.deltaTime);

            if (animator != null)
            {
                animator.SetFloat(SpeedId, _speed);
                if (!Frozen)
                    IdleBreaks();
            }
        }

        // Standing still for a while: shift weight, look around, stretch; yawn when tired. Moving cancels it.
        void IdleBreaks()
        {
            if (_speed > 0.2f)
            {
                _idleTime = 0f;
                if (!animator.IsInTransition(0) && animator.GetCurrentAnimatorStateInfo(0).shortNameHash != LocomotionHash)
                    BackToLocomotion(0.2f);
                return;
            }
            if (_idleBreaks == 0 && !_hasTired)
                return;
            _idleTime += Time.deltaTime;
            if (_idleTime < settings.idleBreakSeconds)
                return;
            _idleTime = -Random.Range(0f, settings.idleBreakSeconds * 0.5f);
            int hash = Tired && _hasTired ? TiredHash : _idleBreaks > 0 ? IdleBreakHashes[Random.Range(0, _idleBreaks)] : TiredHash;
            animator.CrossFadeInFixedTime(hash, 0.25f);
        }

        // After animation: move the visual body so the lowest foot rests on the surface under the player.
        void LateUpdate()
        {
            if (Seated || _body == null || _feet == null || _feet.Length == 0)
                return;
            if (!Physics.Raycast(transform.position + Vector3.up * 0.8f, Vector3.down, out var hit, 2f, ground, QueryTriggerInteraction.Ignore))
                return;
            float lowest = float.MaxValue;
            foreach (var f in _feet)
                lowest = Mathf.Min(lowest, f.position.y);
            float natural = lowest - _bodyOffset;
            float desired = Mathf.Clamp(hit.point.y + ankleHeight - natural, -0.3f, 0.3f);
            _bodyOffset = Mathf.Lerp(_bodyOffset, desired, 1f - Mathf.Exp(-15f * Time.deltaTime));
            _body.localPosition = _bodyBase + Vector3.up * _bodyOffset;
        }
    }
}
