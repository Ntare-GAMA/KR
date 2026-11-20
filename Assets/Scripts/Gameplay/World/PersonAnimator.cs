using System.Collections.Generic;
using UnityEngine;

namespace KR.Gameplay.World
{
    // Animation for a background person (Mixamo clips on a humanoid avatar). All people share one base controller;
    // each gets an override with their own clips for the slots Idle, Move, Talk, Greet and Action0-2.
    //   Being moved (Wanderer, path walkers) -> Move, played at the speed the person actually travels.
    //   Standing -> Idle, with one of their actions every so often (look around, nod, kick the ball, a dance move).
    //   SetTalking -> Talk loop; Greet -> a wave towards someone.
    public sealed class PersonAnimator : MonoBehaviour
    {
        public static readonly string[] Slots = { "Idle", "Move", "Talk", "Greet", "Action0", "Action1", "Action2" };
        const int FirstAction = 4;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int MoveScaleId = Animator.StringToHash("MoveScale");
        static readonly int TalkingId = Animator.StringToHash("Talking");
        static readonly int IdleHash = Animator.StringToHash("Idle");
        static readonly int GreetHash = Animator.StringToHash("Greet");
        static readonly int[] ActionHashes = { Animator.StringToHash("Action0"), Animator.StringToHash("Action1"), Animator.StringToHash("Action2") };
        static readonly Dictionary<string, AnimatorOverrideController> Shared = new Dictionary<string, AnimatorOverrideController>();

        [SerializeField] Animator animator;
        [SerializeField] RuntimeAnimatorController baseController;
        [SerializeField, Tooltip("Placeholder clip of each slot in the base controller (order = Slots).")] AnimationClip[] placeholders;
        [SerializeField, Tooltip("This person's clip for each slot (order = Slots).")] AnimationClip[] clips;
        [SerializeField, Range(0, 3)] int actionCount;
        [SerializeField, Tooltip("Root speed of the Move clip, m/s.")] float moveClipSpeed = 1.4f;
        [SerializeField] Vector2 actionEvery = new Vector2(7f, 16f);

        Vector3 _last;
        float _speed;
        float _baseSpeed = 1f;
        float _nextAction;
        bool _talking, _speaking;
        float _level, _smoothLevel, _speechTime;
        Transform _head;

        public void Configure(Animator body, RuntimeAnimatorController controller, AnimationClip[] slotPlaceholders,
                              AnimationClip[] slotClips, int actions, float clipSpeed, Vector2 every)
        {
            animator = body;
            baseController = controller;
            placeholders = slotPlaceholders;
            clips = slotClips;
            actionCount = actions;
            moveClipSpeed = clipSpeed;
            actionEvery = every;
        }

        void Awake()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (animator == null || baseController == null || clips == null || placeholders == null)
            {
                enabled = false;
                return;
            }
            animator.runtimeAnimatorController = Override();
            _baseSpeed = animator.speed;
        }

        void OnEnable()
        {
            _last = transform.position;
            _speed = 0f;
            _nextAction = Time.time + Random.Range(1f, actionEvery.y);
            // Start each person at a different point of their idle so crowds do not move in step.
            if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
                animator.Play(IdleHash, 0, Random.value);
        }

        AnimatorOverrideController Override()
        {
            var key = new System.Text.StringBuilder().Append(baseController.GetInstanceID());
            foreach (var c in clips)
                key.Append(',').Append(c != null ? c.GetInstanceID() : 0);
            if (Shared.TryGetValue(key.ToString(), out var shared) && shared != null)
                return shared;
            shared = new AnimatorOverrideController(baseController);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            for (int i = 0; i < placeholders.Length && i < clips.Length; i++)
                if (placeholders[i] != null)
                    pairs.Add(new KeyValuePair<AnimationClip, AnimationClip>(placeholders[i], clips[i] != null ? clips[i] : clips[0]));
            shared.ApplyOverrides(pairs);
            Shared[key.ToString()] = shared;
            return shared;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;
            Vector3 p = transform.position, d = p - _last;
            _last = p;
            d.y = 0f;
            float v = d.magnitude / dt;
            if (v > 15f)
                v = 0f;   // teleport or floating-origin shift, not movement
            _speed = Mathf.Lerp(_speed, v, 1f - Mathf.Exp(-8f * dt));
            if (!animator.isActiveAndEnabled)
                return;
            animator.SetFloat(SpeedId, _speed);
            // Walking: the clip runs at exactly the pace the body moves (no per-person variation), so the feet
            // stay planted. Standing: the person's own slight variation, so crowds don't move in step.
            bool moving = _speed > 0.1f;
            animator.speed = moving ? 1f : _baseSpeed;
            animator.SetFloat(MoveScaleId, Mathf.Clamp(_speed / Mathf.Max(0.1f, moveClipSpeed), 0.2f, 3f));
            if (actionCount == 0 || _talking || _speed > 0.1f || Time.time < _nextAction)
                return;
            PlayAction(Random.Range(0, actionCount));
        }

        public void PlayAction(int index)
        {
            if (index < 0 || index >= actionCount || animator == null || !animator.isActiveAndEnabled)
                return;
            animator.CrossFadeInFixedTime(ActionHashes[index], 0.2f);
            _nextAction = Time.time + Random.Range(actionEvery.x, actionEvery.y);
        }

        public void Greet(Vector3 towards)
        {
            Face(towards);
            if (animator != null && animator.isActiveAndEnabled)
                animator.CrossFadeInFixedTime(GreetHash, 0.2f);
            _nextAction = Time.time + actionEvery.x;
        }

        public void SetTalking(bool on, Vector3 towards)
        {
            _talking = on;
            _speaking = on;
            _level = 0f;
            if (on)
                Face(towards);
            if (animator != null && animator.isActiveAndEnabled)
                animator.SetBool(TalkingId, on);
        }

        // In a conversation, called every frame: the Talk gestures run only while this person's line is being
        // spoken (still while you answer), and the head moves with the loudness of the voice (0..1).
        public void Speak(bool speaking, float level)
        {
            _level = speaking ? level : 0f;
            if (speaking == _speaking || !_talking)
                return;
            _speaking = speaking;
            if (animator != null && animator.isActiveAndEnabled)
                animator.SetBool(TalkingId, speaking);
        }

        void LateUpdate()
        {
            if (!_talking || animator == null || !animator.isActiveAndEnabled)
                return;
            if (_head == null)
            {
                if (!animator.isHuman)
                    return;
                _head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (_head == null)
                    return;
            }
            float dt = Time.deltaTime;
            _smoothLevel = Mathf.Lerp(_smoothLevel, _level, 1f - Mathf.Exp(-14f * dt));
            _speechTime += dt;
            // Small nods on the stressed syllables and a slow sway, after the animation has posed the head.
            float nod = _smoothLevel * 6f * (0.65f + 0.35f * Mathf.Sin(_speechTime * 6.3f));
            float sway = _smoothLevel * 3f * Mathf.Sin(_speechTime * 1.9f);
            _head.rotation = Quaternion.AngleAxis(nod, transform.right) * Quaternion.AngleAxis(sway, Vector3.up) * _head.rotation;
        }

        void Face(Vector3 target)
        {
            var to = target - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(to);
        }
    }
}
