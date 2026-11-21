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
        AudioSource _voice;
        readonly float[] _samples = new float[256];
        float _lineStart, _lineSeconds;
        bool _revealed;
        readonly Dictionary<Npc, string> _gender = new Dictionary<Npc, string>();
        Text _panelText, _labelText;
        // The conversation panel in the KR menu style: a red name tab, the line, one panel per reply.
        const int MaxRows = 5;
        const float PanelW = 780f, PanelH = 600f, RowH = 58f, RowGap = 8f, HintH = 44f;
        Text _name, _sub, _hint;
        readonly Image[] _rowBg = new Image[MaxRows], _rowFrame = new Image[MaxRows], _rowChip = new Image[MaxRows];
        readonly Text[] _rowText = new Text[MaxRows], _rowKey = new Text[MaxRows];
        readonly StringBuilder _sb = new StringBuilder();

        // True while X belongs to the conversation (a character is in range, or you are talking).
        public bool WantsConfirm => _near != null || _with != null;
        public bool Talking => _with != null;
        // Raised when any conversation ends (id, the character), e.g. the accident scene waits for it.
        public event System.Action<string, Npc> Finished;
        // Raised when a line or a reply sets a flag (flag, the character), e.g. an order at a cafe.
        public event System.Action<string, Npc> FlagSet;

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
            _panel = MakeCanvas("Conversation panel", new Vector2(PanelW, PanelH), 0.0011f, 30, out _panelText);
            BuildPanel();
            _label = MakeCanvas("Talk label", new Vector2(360f, 70f), 0.0016f, 34, out _labelText);
            var voice = new GameObject("Voice");
            voice.transform.SetParent(transform, false);
            _voice = voice.AddComponent<AudioSource>();
            _voice.playOnAwake = false;
            _voice.spatialBlend = 0.8f;
            _voice.minDistance = 2f;
            _voice.maxDistance = 15f;
        }

        // Spoken line, if recorded: Resources/Story/VO/<conversation>_<node>_<f|m>.wav (for lines shared by many
        // characters), else <conversation>_<node>.wav. Made by Tools/voiceover/make_voice_lines.py; silent if missing.
        // The subtitle is typed out in step with the voice (or at reading pace when the line has no recording);
        // replies appear once the line has been said. X during a line shows it all at once.
        void Speak()
        {
            _voice.Stop();
            _voice.clip = null;
            _lineStart = Time.unscaledTime;
            _lineSeconds = 0f;
            _revealed = false;
            if (_node == null || _node.speaker == "you" || _with == null || _current == null)
            {
                _revealed = true;
                return;
            }
            string key = $"Story/VO/{_current.id}_{_node.id}";
            var clip = Resources.Load<AudioClip>($"{key}_{GenderOf(_with)}") ?? Resources.Load<AudioClip>(key);
            if (clip == null)
            {
                _lineSeconds = Mathf.Min(4f, (_node.text?.Length ?? 0) * 0.03f);
                return;
            }
            _voice.transform.position = _with.HeadPosition;
            _voice.clip = clip;
            _voice.Play();
        }

        // 0..1: how much of the current line has been said. Text runs slightly ahead of the voice so the last
        // word is on screen as it is spoken.
        float Said()
        {
            if (_revealed)
                return 1f;
            float t;
            if (_voice.clip != null)
                t = _voice.isPlaying ? _voice.time / Mathf.Max(0.1f, _voice.clip.length) * 1.1f : 1f;
            else
                t = _lineSeconds <= 0f ? 1f : (Time.unscaledTime - _lineStart) / _lineSeconds;
            if (t >= 1f)
                _revealed = true;
            return Mathf.Clamp01(t);
        }

        // Loudness of the voice now (0..1), for the speaker's head and gestures.
        float VoiceLevel()
        {
            if (_voice.clip == null || !_voice.isPlaying)
                return 0f;
            _voice.GetOutputData(_samples, 0);
            float sum = 0f;
            for (int i = 0; i < _samples.Length; i++)
                sum += _samples[i] * _samples[i];
            return Mathf.Clamp01(Mathf.Sqrt(sum / _samples.Length) * 7f);
        }

        // "f" or "m" from the character's model (the women's models are named Woman / female).
        string GenderOf(Npc npc)
        {
            if (_gender.TryGetValue(npc, out var g))
                return g;
            g = "m";
            foreach (var r in npc.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name.ToLowerInvariant();
                if (n.Contains("woman") || n.Contains("female") || n.Contains("girl"))
                {
                    g = "f";
                    break;
                }
            }
            string who = npc.name.ToLowerInvariant();
            if (who.Contains("woman") || who.Contains("waitress"))
                g = "f";
            return _gender[npc] = g;
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
            Fill(bg.rectTransform, 0f);
            KR.Gameplay.UI.KrTheme.Skin(bg);
            text = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Text>();
            text.transform.SetParent(go.transform, false);
            text.font = KR.Gameplay.UI.KrTheme.Font(KR.Gameplay.UI.KrTheme.Weight.SemiBold);
            text.fontSize = font + 4;   // Rajdhani is narrow: a little larger reads the same
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
                float best = float.MaxValue;
                Vector3 p = rig.Player.transform.position;
                var all = Npc.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var npc = all[i];
                    float r = npc.Range > 0f ? npc.Range : talkRange;
                    float d = (npc.transform.position - p).sqrMagnitude;
                    if (d < r * r && d < best)
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
            _labelText.text = $"<color=#3ee6ff><b>{(VrRig.Active ? "X" : "Enter")}</b></color>  Talk to {_near.DisplayName}";
        }

        // The milestone's conversation for this character if one is running, otherwise their everyday chat.
        void Begin(Npc npc) => Begin(npc, null);

        // Starts a conversation with this character; with an id, that conversation instead of the usual one.
        public void Begin(Npc npc, string conversationId)
        {
            if (npc == null || _with != null)
                return;
            string id = conversationId ?? npc.ChatConversation;
            var m = director.Current;
            if (conversationId == null && m != null && m.kind == MissionKind.Talk && !string.IsNullOrEmpty(m.conversationId) &&
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
            {
                director.SetFlag(_node.setFlag);
                FlagSet?.Invoke(_node.setFlag, _with);
            }
            Speak();
        }

        void Converse()
        {
            bool saying = _question == null && _node != null && Said() < 1f;
            int choices = _question != null ? (_answered ? 0 : _question.options.Length) : _node != null && !saying ? _node.choices.Length : 0;
            if (choices > 0)
                _choice = (_choice - RigInput.NavY + choices) % choices;
            if (RigInput.ConfirmPressed)
            {
                if (saying)
                    _revealed = true;
                else
                    Advance();
            }
            if (_with == null)
                return;
            bool npcLine = _question == null && _node != null && _node.speaker != "you";
            _with.Speaking(npcLine && (_voice.isPlaying || saying), npcLine ? VoiceLevel() : 0f);
            PlacePanel();
            Show();
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
                {
                    director.SetFlag(c.setFlag);
                    FlagSet?.Invoke(c.setFlag, _with);
                }
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

        void BuildPanel()
        {
            var root = _panel.transform;
            var tab = UI.KrTheme.Panel(root, "Name tab", UI.KrTheme.Red);
            UI.KrTheme.Place(tab.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -16f), new Vector2(330f, 50f));
            _name = UI.KrTheme.Label(tab.transform, "Name", 32, UI.KrTheme.Weight.Bold, UI.KrTheme.Text, TextAnchor.MiddleLeft);
            UI.KrTheme.Stretch(_name.rectTransform, 0f);
            _name.rectTransform.offsetMin = new Vector2(16f, 0f);
            _name.horizontalOverflow = HorizontalWrapMode.Overflow;
            _sub = UI.KrTheme.Label(root, "Where", 22, UI.KrTheme.Weight.SemiBold, UI.KrTheme.Muted, TextAnchor.MiddleLeft);
            UI.KrTheme.Place(_sub.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(362f, -16f), new Vector2(400f, 50f));

            UI.KrTheme.Style(_panelText, 33, UI.KrTheme.Weight.SemiBold, UI.KrTheme.Text);
            for (int i = 0; i < MaxRows; i++)
            {
                var bg = UI.KrTheme.Panel(root, $"Reply {i + 1}", UI.KrTheme.InkSoft);
                var chip = UI.KrTheme.Panel(bg.transform, "Number", UI.KrTheme.RedDeep);
                UI.KrTheme.Place(chip.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(42f, 42f));
                var key = UI.KrTheme.Label(chip.transform, "Key", 26, UI.KrTheme.Weight.Bold, UI.KrTheme.Text, TextAnchor.MiddleCenter);
                UI.KrTheme.Stretch(key.rectTransform, 0f);
                var label = UI.KrTheme.Label(bg.transform, "Text", 27, UI.KrTheme.Weight.SemiBold, UI.KrTheme.Text, TextAnchor.MiddleLeft);
                UI.KrTheme.Stretch(label.rectTransform, 0f);
                label.rectTransform.offsetMin = new Vector2(62f, 2f);
                label.rectTransform.offsetMax = new Vector2(-12f, -2f);
                _rowBg[i] = bg;
                _rowChip[i] = chip;
                _rowKey[i] = key;
                _rowText[i] = label;
                _rowFrame[i] = UI.KrTheme.Outline(bg.transform, UI.KrTheme.Accent);
                bg.gameObject.SetActive(false);
            }
            _hint = UI.KrTheme.Label(root, "Hint", 22, UI.KrTheme.Weight.SemiBold, UI.KrTheme.Muted, TextAnchor.MiddleLeft);
            UI.KrTheme.Place(_hint.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(22f, 8f), new Vector2(PanelW - 44f, HintH));
        }

        // Lays out the panel for the current line or question: the line above, the replies stacked at the bottom.
        void Show()
        {
            string confirm = VrRig.Active ? "X" : "Enter";
            int rows = 0;
            string body, hint;
            if (_question != null)
            {
                _name.text = $"QUESTION {_asked + (_answered ? 0 : 1)}";
                _sub.text = "Draft, under review";
                body = _question.text;
                if (_answered)
                    body += $"\n\n<size=25>{_question.explanation}</size>\n<size=21><color={UI.KrTheme.MutedHex}>Source: {_question.source}</color></size>";
                rows = Mathf.Min(MaxRows, _question.options.Length);
                for (int i = 0; i < rows; i++)
                {
                    bool right = _answered && i == _question.correct, wrong = _answered && i == _choice && i != _question.correct;
                    Row(i, _question.options[i], !_answered && i == _choice,
                        right ? new Color(0.25f, 0.85f, 0.45f) : wrong ? UI.KrTheme.Red : (Color?)null,
                        right ? "OK" : wrong ? "X" : (i + 1).ToString());
                }
                hint = _answered ? $"{confirm}: continue" : $"Left stick or arrows to choose, {confirm} to answer";
            }
            else
            {
                _name.text = (_node.speaker == "you" ? "You" : _with.DisplayName).ToUpperInvariant();
                _sub.text = _node.speaker == "you" || string.IsNullOrEmpty(_with.Place) ? "" : _with.Place;
                string line = Fill(_node.text) ?? "";
                float said = Said();
                int shown = said >= 1f ? line.Length : Mathf.Clamp(Mathf.CeilToInt(line.Length * said), 0, line.Length);
                // The rest of the line is laid out invisibly so the words do not jump while it is typed.
                body = shown < line.Length ? line.Substring(0, shown) + "<color=#00000000>" + line.Substring(shown) + "</color>" : line;
                if (said >= 1f && _node.choices.Length > 0)
                {
                    rows = Mathf.Min(MaxRows, _node.choices.Length);
                    for (int i = 0; i < rows; i++)
                        Row(i, Fill(_node.choices[i].text), i == _choice, null, (i + 1).ToString());
                    hint = $"Left stick or arrows to choose, {confirm} to reply";
                }
                else if (said < 1f)
                    hint = $"{confirm}: skip";
                else
                    hint = $"{confirm}: {(string.IsNullOrEmpty(_node.next) && (_node.quiz == null || _node.quiz.Length == 0) ? "end" : "continue")}";
            }
            for (int i = rows; i < MaxRows; i++)
                if (_rowBg[i].gameObject.activeSelf)
                    _rowBg[i].gameObject.SetActive(false);
            // Replies from the bottom up, above the hint; the line fills what is left under the name tab.
            float bottom = 8f + HintH + 6f;
            for (int i = rows - 1; i >= 0; i--)
            {
                UI.KrTheme.Place(_rowBg[i].rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, bottom), new Vector2(PanelW - 36f, RowH));
                bottom += RowH + RowGap;
            }
            var r = _panelText.rectTransform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(24f, bottom + 4f);
            r.offsetMax = new Vector2(-24f, -84f);
            _panelText.verticalOverflow = VerticalWrapMode.Truncate;
            _panelText.text = body;
            _hint.text = hint;
        }

        void Row(int i, string text, bool selected, Color? mark, string key)
        {
            if (!_rowBg[i].gameObject.activeSelf)
                _rowBg[i].gameObject.SetActive(true);
            _rowText[i].text = text;
            _rowKey[i].text = key;
            _rowFrame[i].enabled = selected || mark.HasValue;
            _rowFrame[i].color = mark ?? UI.KrTheme.Accent;
            _rowChip[i].color = mark ?? (selected ? UI.KrTheme.Red : UI.KrTheme.RedDeep);
            _rowBg[i].color = selected ? new Color(0.16f, 0.07f, 0.1f, 0.95f) : UI.KrTheme.InkSoft;
        }

        // Lines can name the business and the character: {place}, {name}.
        string Fill(string line)
        {
            if (string.IsNullOrEmpty(line) || line.IndexOf('{') < 0 || _with == null)
                return line;
            return line.Replace("{place}", string.IsNullOrEmpty(_with.Place) ? "here" : _with.Place)
                       .Replace("{name}", _with.DisplayName);
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
            if (_voice != null)
                _voice.Stop();
            rig.Busy = false;
            rig.Player.Frozen = false;
            if (npc != null)
                npc.Talking(false, Vector3.zero);
            if (conversation != null)
            {
                director.OnConversationFinished(conversation.id, _correct, _asked);
                Finished?.Invoke(conversation.id, npc);
            }
        }
    }
}
