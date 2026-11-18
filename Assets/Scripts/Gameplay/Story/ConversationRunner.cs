using System.Collections.Generic;
using System.Text;
using KR.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace KR.Gameplay.Story
{
    // Authored, branching conversations (Resources/Story/story_script.json; every line is written by a person,
    // nothing is generated). Walk up to a character: a small "X Talk" label appears above them; press X.
    // The conversation panel stands beside the character, never in front of your face. Left stick up/down picks
    // a reply, X confirms. Quiz questions inside a conversation are asked the same way; the result goes to the
    // story (a Talk milestone needs 2 of 3 right).
    public sealed class ConversationRunner : MonoBehaviour
    {
        [SerializeField] StoryDirector director;
        [SerializeField] PlayerRig rig;
        [SerializeField] float talkRange = 2.6f;

        ScriptFile _script;
        readonly Dictionary<string, Conversation> _conversations = new Dictionary<string, Conversation>();
        readonly Dictionary<string, QuizQuestion> _quiz = new Dictionary<string, QuizQuestion>();
        Npc[] _npcs = new Npc[0];

        Npc _near, _with;
        Conversation _current;
        DialogueNode _node;
        int _choice;
        // Quiz state inside a conversation.
        readonly Queue<QuizQuestion> _questions = new Queue<QuizQuestion>();
        QuizQuestion _question;
        bool _answered;
        int _correct, _asked;

        Canvas _panel, _label;
        Text _panelText, _labelText;
        readonly StringBuilder _sb = new StringBuilder();

        // True while X belongs to the conversation (a character is in range, or you are talking).
        public bool WantsConfirm => _near != null || _with != null;
        public bool Talking => _with != null;

        public void Configure(StoryDirector story, PlayerRig playerRig)
        {
            director = story;
            rig = playerRig;
        }

        void Start()
        {
            var asset = Resources.Load<TextAsset>("Story/story_script");
            _script = asset != null ? JsonUtility.FromJson<ScriptFile>(asset.text) : new ScriptFile();
            foreach (var c in _script.conversations)
                if (!string.IsNullOrEmpty(c.id))
                    _conversations[c.id] = c;
            foreach (var q in _script.quiz)
                if (!string.IsNullOrEmpty(q.id))
                    _quiz[q.id] = q;
            _npcs = FindObjectsByType<Npc>(FindObjectsSortMode.None);
            _panel = MakeCanvas("Conversation panel", new Vector2(760f, 520f), 0.0011f, 30, out _panelText);
            _label = MakeCanvas("Talk label", new Vector2(360f, 70f), 0.0016f, 34, out _labelText);
        }

        Canvas MakeCanvas(string name, Vector2 size, float scale, int font, out Text text)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = size;
            go.transform.localScale = Vector3.one * scale;
            var bg = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
            bg.transform.SetParent(go.transform, false);
            bg.color = new Color(0.03f, 0.04f, 0.05f, 0.88f);
            Fill(bg.rectTransform, 0f);
            text = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Text>();
            text.transform.SetParent(go.transform, false);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = font;
            text.supportRichText = true;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            Fill(text.rectTransform, 18f);
            go.SetActive(false);
            return canvas;
        }

        static void Fill(RectTransform r, float pad)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(pad, pad);
            r.offsetMax = new Vector2(-pad, -pad);
        }

        void Update()
        {
            if (rig == null || director == null)
                return;
            if (_with != null)
            {
                Converse();
                return;
            }
            // Someone to talk to: on foot, close, not paused, hands free.
            _near = null;
            if (rig.CurrentMode == PlayerRig.Mode.Walking && !GamePause.Paused && !rig.Busy)
            {
                float best = talkRange * talkRange;
                Vector3 p = rig.Player.transform.position;
                foreach (var npc in _npcs)
                {
                    if (npc == null)
                        continue;
                    float d = (npc.transform.position - p).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        _near = npc;
                    }
                }
            }
            ShowLabel();
            if (_near != null && RigInput.ConfirmPressed)
                Begin(_near);
        }

        void ShowLabel()
        {
            bool show = _near != null;
            if (_label.gameObject.activeSelf != show)
                _label.gameObject.SetActive(show);
            if (!show)
                return;
            var cam = Camera.main;
            Vector3 pos = _near.HeadPosition + Vector3.up * 0.45f;
            _label.transform.SetPositionAndRotation(pos, cam != null ? Quaternion.LookRotation(pos - cam.transform.position) : Quaternion.identity);
            _labelText.alignment = TextAnchor.MiddleCenter;
            _labelText.text = $"<b>{(VrRig.Active ? "X" : "Enter")}</b>  Talk to {_near.DisplayName}";
        }

        // The milestone's conversation for this character if one is running, otherwise their everyday chat.
        void Begin(Npc npc)
        {
            string id = npc.ChatConversation;
            var m = director.Current;
            if (m != null && m.kind == MissionKind.Talk && !string.IsNullOrEmpty(m.conversationId) &&
                _conversations.TryGetValue(m.conversationId, out var mission) && mission.npc == npc.Id &&
                (director.State == StoryDirector.Phase.Rushing || director.State == StoryDirector.Phase.Dwelling))
                id = m.conversationId;
            if (string.IsNullOrEmpty(id) || !_conversations.TryGetValue(id, out _current))
            {
                rig.Toast($"{npc.DisplayName} smiles and waves.", 3f);
                return;
            }
            _with = npc;
            _near = null;
            _label.gameObject.SetActive(false);
            _correct = _asked = 0;
            _questions.Clear();
            _question = null;
            rig.Busy = true;
            rig.Player.Frozen = true;
            npc.Talking(true, rig.Player.transform.position);
            Go(_current.start);
            _panel.gameObject.SetActive(true);
        }

        void Go(string nodeId)
        {
            _node = null;
            _choice = 0;
            if (!string.IsNullOrEmpty(nodeId))
                foreach (var n in _current.nodes)
                    if (n.id == nodeId)
                        _node = n;
            if (_node == null)
            {
                if (!string.IsNullOrEmpty(nodeId))
                    Debug.LogWarning($"[Conversation] {_current.id}: missing node '{nodeId}'; ending.");
                End();
                return;
            }
            if (!string.IsNullOrEmpty(_node.setFlag))
                director.SetFlag(_node.setFlag);
        }

        void Converse()
        {
            int choices = _question != null ? (_answered ? 0 : _question.options.Length) : _node != null ? _node.choices.Length : 0;
            if (choices > 0)
                _choice = (_choice - RigInput.NavY + choices) % choices;
            if (RigInput.ConfirmPressed)
                Advance();
            if (_with == null)
                return;
            PlacePanel();
            _panelText.text = Compose();
        }

        void Advance()
        {
            if (_question != null)
            {
                if (!_answered)
                {
                    _answered = true;
                    _asked++;
                    if (_choice == _question.correct)
                        _correct++;
                    return;
                }
                NextQuestion();
                return;
            }
            if (_node.choices.Length > 0)
            {
                var c = _node.choices[_choice];
                if (!string.IsNullOrEmpty(c.setFlag))
                    director.SetFlag(c.setFlag);
                QueueQuiz(_node);
                if (_question == null)
                    Go(c.next);
                else
                    _afterQuiz = c.next;
                return;
            }
            QueueQuiz(_node);
            if (_question == null)
                Go(_node.next);
            else
                _afterQuiz = _node.next;
        }

        string _afterQuiz;

        void QueueQuiz(DialogueNode node)
        {
            if (node.quiz == null || node.quiz.Length == 0 || _questions.Count > 0)
                return;
            foreach (var id in node.quiz)
                if (_quiz.TryGetValue(id, out var q))
                    _questions.Enqueue(q);
            NextQuestion();
        }

        void NextQuestion()
        {
            _answered = false;
            _choice = 0;
            if (_questions.Count > 0)
            {
                _question = _questions.Dequeue();
                return;
            }
            bool wasQuiz = _question != null;
            _question = null;
            if (wasQuiz)
                Go(_afterQuiz);
        }

        string Compose()
        {
            _sb.Clear();
            string confirm = VrRig.Active ? "X" : "Enter";
            if (_question != null)
            {
                _sb.Append($"<b>Question {_asked + (_answered ? 0 : 1)}</b>  <size=22><color=#999999>(draft, under review)</color></size>\n{_question.text}\n\n");
                for (int i = 0; i < _question.options.Length; i++)
                {
                    string mark = _answered ? (i == _question.correct ? "<color=#66ff88>✔ </color>" : i == _choice ? "<color=#ff7766>✘ </color>" : "   ")
                                            : (i == _choice ? "<b>> </b>" : "   ");
                    _sb.Append(mark).Append(_question.options[i]).Append('\n');
                }
                if (_answered)
                    _sb.Append($"\n<size=24>{_question.explanation}\n<color=#999999>Source: {_question.source}</color></size>\n\n{confirm}: continue");
                else
                    _sb.Append($"\n<size=24>Left stick to choose, {confirm} to answer.</size>");
                return _sb.ToString();
            }
            string who = _node.speaker == "you" ? "You" : _with.DisplayName;
            _sb.Append($"<b>{who}</b>\n{_node.text}\n\n");
            if (_node.choices.Length > 0)
            {
                for (int i = 0; i < _node.choices.Length; i++)
                    _sb.Append(i == _choice ? "<b>> " : "   ").Append(_node.choices[i].text).Append(i == _choice ? "</b>\n" : "\n");
                _sb.Append($"\n<size=24>Left stick to choose, {confirm} to reply.</size>");
            }
            else
                _sb.Append($"<size=24>{confirm}: {(string.IsNullOrEmpty(_node.next) && (_node.quiz == null || _node.quiz.Length == 0) ? "end" : "continue")}</size>");
            return _sb.ToString();
        }

        // Beside the character at their head height, turned towards you.
        void PlacePanel()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            Vector3 toYou = cam.transform.position - _with.HeadPosition;
            toYou.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, toYou.normalized);
            Vector3 pos = _with.HeadPosition + side * 0.75f + toYou.normalized * 0.3f;
            _panel.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - cam.transform.position));
        }

        void End()
        {
            var npc = _with;
            var conversation = _current;
            _with = null;
            _current = null;
            _node = null;
            _panel.gameObject.SetActive(false);
            rig.Busy = false;
            rig.Player.Frozen = false;
            if (npc != null)
                npc.Talking(false, Vector3.zero);
            if (conversation != null)
                director.OnConversationFinished(conversation.id, _correct, _asked);
        }
    }
}
