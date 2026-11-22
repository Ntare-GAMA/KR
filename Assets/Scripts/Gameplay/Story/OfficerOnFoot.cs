using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // The officer who gets out of the responding police car: steps out on the driver's side, walks to you (or the
    // scene), says what the police say, waits a moment, then walks back and gets in again. The body is a copy of the
    // city's own patrol officer (uniform, cap, animations); copied once and reused.
    public sealed class OfficerOnFoot
    {
        enum Step { Hidden, ToYou, Talking, Back }

        const float WalkSpeed = 1.5f, TalkSeconds = 7f;

        readonly Transform _body;
        readonly PersonAnimator _anim;
        readonly SpeechBubble _say;
        Step _step;
        Transform _car;
        Vector3 _goal;
        string _line;
        float _until;

        public bool Busy => _step != Step.Hidden;

        OfficerOnFoot(Transform body, SpeechBubble say)
        {
            _body = body;
            _anim = body.GetComponent<PersonAnimator>();
            _say = say;
        }

        // A copy of the first police officer in the city, or null (with a warning) if there is none.
        public static OfficerOnFoot Create(Transform parent)
        {
            // A uniformed officer (vest and cap): the city's patrol or traffic officers, never a story character
            // such as Officer Mukamana, who is in her own clothes.
            PersonAnimator template = null;
            foreach (var want in new[] { "Police officer on patrol", "Traffic officer" })
            {
                foreach (var p in Object.FindObjectsByType<PersonAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (p.name.StartsWith(want))
                    {
                        template = p;
                        break;
                    }
                if (template != null)
                    break;
            }
            if (template == null)
            {
                Debug.LogWarning("[Emergency] No police officer character in the city; the police car's officer stays inside.");
                return null;
            }
            var go = Object.Instantiate(template.gameObject, parent);
            go.name = "Police officer (responding)";
            // Only the body and its animation: no post, whistle, conversation or patrol route.
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(mb is PersonAnimator))
                    Object.Destroy(mb);
            go.SetActive(false);
            return new OfficerOnFoot(go.transform, SpeechBubble.Create(parent));
        }

        // Out of the car (driver's side) towards target, to say line there.
        public void GetOut(Transform car, Vector3 target, string line)
        {
            _car = car;
            _line = line;
            var door = Ground(car.position - car.right * 1.4f);
            var to = target - door;
            to.y = 0f;
            _goal = to.magnitude > 1.8f ? target - to.normalized * 1.6f : door;
            _body.SetPositionAndRotation(door, Quaternion.LookRotation(to.sqrMagnitude > 0.01f ? to : car.forward));
            _body.gameObject.SetActive(true);
            _step = Step.ToYou;
        }

        public void Tick(float dt, Vector3 you)
        {
            switch (_step)
            {
                case Step.ToYou:
                    if (Walk(_goal, dt))
                    {
                        _step = Step.Talking;
                        _until = Time.time + TalkSeconds;
                        _anim?.SetTalking(true, you);
                        _say.Say(_body, 2.1f, _line, TalkSeconds);
                    }
                    break;
                case Step.Talking:
                    Face(you);
                    if (Time.time > _until)
                    {
                        _anim?.SetTalking(false, Vector3.zero);
                        _goal = Ground(_car.position - _car.right * 1.4f);
                        _step = Step.Back;
                    }
                    break;
                case Step.Back:
                    if (Walk(_goal, dt))
                    {
                        _body.gameObject.SetActive(false);
                        _step = Step.Hidden;
                    }
                    break;
            }
        }

        bool Walk(Vector3 goal, float dt)
        {
            var to = goal - _body.position;
            to.y = 0f;
            if (to.magnitude < 0.3f)
                return true;
            var step = to.normalized * Mathf.Min(WalkSpeed * dt, to.magnitude);
            _body.position = Ground(_body.position + step);
            _body.rotation = Quaternion.RotateTowards(_body.rotation, Quaternion.LookRotation(to), 360f * dt);
            return false;
        }

        void Face(Vector3 p)
        {
            var to = p - _body.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                _body.rotation = Quaternion.RotateTowards(_body.rotation, Quaternion.LookRotation(to), 180f * Time.deltaTime);
        }

        static Vector3 Ground(Vector3 p) =>
            Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 8f, 1 << 0, QueryTriggerInteraction.Ignore) &&
            hit.collider.attachedRigidbody == null ? hit.point : p;
    }
}
