using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // The officer who gets out of the responding police car: steps out on the driver's side, walks to you (or the
    // scene), says the police's lines one after another (asking for your driving licence and ID, writing a ticket;
    // each line can trigger something, such as the cards or the ticket on screen), then walks back and gets in again.
    // The body is a copy of the city's own uniformed patrol officer (vest, cap, animations); copied once and reused.
    public sealed class OfficerOnFoot
    {
        enum Step { Hidden, ToYou, Talking, Back }

        const float WalkSpeed = 1.5f, LineSeconds = 5.5f;

        readonly Transform _body;
        readonly PersonAnimator _anim;
        readonly SpeechBubble _say;
        Step _step;
        Transform _car;
        Vector3 _goal;
        string[] _lines;
        System.Action<int> _onLine;
        int _index;
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
        public void GetOut(Transform car, Vector3 target, string line) => GetOut(car, target, new[] { line }, null);

        // Lines said in turn once beside you; onLine(i) runs as line i is said.
        public void GetOut(Transform car, Vector3 target, string[] lines, System.Action<int> onLine)
        {
            _car = car;
            _lines = lines;
            _onLine = onLine;
            var door = Ground(car.position - car.right * 1.4f);
            var to = target - door;
            to.y = 0f;
            _goal = to.magnitude > 2.6f ? target - to.normalized * 2.4f : door;   // a polite distance, the face and the words in view
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
                        _index = -1;
                        _until = 0f;
                        _anim?.SetTalking(true, you);
                    }
                    break;
                case Step.Talking:
                    Face(you);
                    if (Time.time > _until && _index + 1 < _lines.Length)
                    {
                        _index++;
                        _until = Time.time + LineSeconds;
                        _say.Say(_body, 1.95f, _lines[_index], LineSeconds);
                        _onLine?.Invoke(_index);
                    }
                    else if (Time.time > _until)
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
