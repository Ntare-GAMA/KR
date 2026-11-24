using System.Collections.Generic;
using KR.Gameplay.Player;
using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // Everyday life inside a named place. Puts staff and customers on the layout's spots and runs the routine:
    //   Bank:   reception gives you a ticket; sit and wait; your number is called to a counter; the teller asks for
    //           your ID (or your student card to open a student account) and serves you.
    //   Clinic: reception registers you with your ID; wait; the nurse calls you into Room 1.
    //   Hotel:  reception checks you in with your ID (or you wait in the lounge); a porter, guests.
    //   Pharmacy, offices: staff at the counter or desks who help you.
    // Lines are everyday and generic; no real bank, clinic or hotel procedures or prices are claimed.
    public sealed class VenueLife : MonoBehaviour
    {
        VenueKind _kind;
        string _name;
        ConversationRunner _runner;
        PlayerRig _rig;
        readonly Dictionary<string, Npc> _staff = new Dictionary<string, Npc>();
        readonly Dictionary<string, GameObject> _people = new Dictionary<string, GameObject>();
        int _ticket;
        float _callAt;
        string _calledTo;
        bool _served;
        SpeechBubble _announce;

        string Id(string what) => $"venue_{_kind.ToString().ToLowerInvariant()}_{what}";

        public static VenueLife Create(GameObject interior, VenueKind kind, string name, InteriorBuilder layout)
        {
            var life = interior.AddComponent<VenueLife>();
            life.Begin(kind, name, layout);
            return life;
        }

        void Begin(VenueKind kind, string name, InteriorBuilder layout)
        {
            _kind = kind;
            _name = string.IsNullOrEmpty(name) ? "the front desk" : name;
            _runner = FindFirstObjectByType<ConversationRunner>();
            _rig = FindFirstObjectByType<PlayerRig>();
            _announce = SpeechBubble.Create(transform);
            RegisterConversations();
            int seed = name != null ? name.GetHashCode() : 1;
            foreach (var spot in layout.Spots)
            {
                var person = PeopleFactory.Make(transform, spot.at, spot.facing, seed++, $"{Role(spot.role)} at {_name}");
                if (person == null)
                    continue;
                _people[spot.role] = person.gameObject;
                var npc = person.gameObject.AddComponent<Npc>();
                npc.Configure($"venue:{spot.role}", Role(spot.role), ChatFor(spot.role), person, _name, 2.2f);
                _staff[spot.role] = npc;
            }
            if (_runner != null)
                _runner.FlagSet += OnFlag;
            Debug.Log($"[Venue] {kind} '{_name}': {_staff.Count} people.");
        }

        void OnDestroy()
        {
            if (_runner != null)
                _runner.FlagSet -= OnFlag;
        }

        static string Role(string role)
        {
            if (role.StartsWith("teller")) return "Teller " + role.Substring(6);
            if (role.StartsWith("customer")) return "Customer";
            if (role.StartsWith("desk")) return "Staff";
            switch (role)
            {
                case "reception": return "Receptionist";
                case "nurse": return "Nurse";
                case "pharmacist": return "Pharmacist";
                case "porter": return "Porter";
                case "guest": return "Hotel guest";
                case "waiting": return "Someone waiting";
                default: return "Staff";
            }
        }

        string ChatFor(string role)
        {
            if (role.StartsWith("teller")) return Id("teller_wait");
            if (role == "reception") return Id("reception");
            if (role == "nurse") return Id("nurse_wait");
            if (role == "pharmacist") return Id("pharmacist");
            if (role == "porter") return Id("porter");
            if (role.StartsWith("desk")) return Id("desk");
            return Id("chat");
        }

        // ---------- conversations ----------

        static DialogueNode N(string id, string speaker, string text, string next = null, string flag = null, params DialogueChoice[] choices) =>
            new DialogueNode { id = id, speaker = speaker, text = text, next = next ?? "", setFlag = flag ?? "", choices = choices };

        static DialogueChoice C(string text, string next, string flag = null) => new DialogueChoice { text = text, next = next, setFlag = flag ?? "" };

        void Add(string what, params DialogueNode[] nodes)
        {
            if (_runner == null)
                return;
            _runner.Register(new Conversation { id = Id(what), npc = "", start = nodes[0].id, nodes = nodes });
        }

        void RegisterConversations()
        {
            Add("chat", N("a", "npc", "Busy in here today. Hope you are not in a hurry."));
            Add("porter", N("a", "npc", "Welcome to {place}! Reception is straight ahead. Can I help with any bags?",
                null, null, C("No thanks, I'm fine.", ""), C("Where can I sit?", "b")),
                N("b", "npc", "The lounge is on your left. Make yourself comfortable."));
            Add("desk", N("a", "npc", "Hello. Please check in at reception first, they will tell you who can help."));
            switch (_kind)
            {
                case VenueKind.Bank:
                    Add("reception",
                        N("a", "npc", "Good day, welcome to {place}. How can I help you?", null, null,
                          C("I'd like to see a teller.", "t"), C("I want to open a student account.", "t"), C("Just looking, thank you.", "x")),
                        N("t", "npc", "Of course. Here is your ticket. Please have a seat, your number will be called to a counter.", null, "venue_ticket"),
                        N("x", "npc", "No problem. The waiting area is just there if you need it."));
                    Add("teller_wait", N("a", "npc", "Hello! Please take a ticket at reception and wait for your number to be called."));
                    Add("teller",
                        N("a", "npc", "Good day! Thank you for waiting. What can I do for you today?", null, null,
                          C("I'd like to make a deposit.", "id"), C("I'd like to withdraw some cash.", "id"), C("I want to open a student account.", "st")),
                        N("id", "npc", "Certainly. May I see your identity card, please?", null, null, C("Here is my ID.", "ok", "venue_show_id")),
                        N("st", "npc", "Lovely. For a student account I need your identity card and your student card.", null, null,
                          C("Here they are.", "ok2", "venue_show_both")),
                        N("ok", "npc", "Thank you, NTARE Shiloh. All done. Here is your receipt. Have a good day!", null, "venue_served"),
                        N("ok2", "npc", "Thank you, NTARE Shiloh. Your student account is open. Welcome!", null, "venue_served"));
                    break;
                case VenueKind.Clinic:
                    Add("reception",
                        N("a", "npc", "Hello, welcome to {place}. Do you have an appointment?", null, null,
                          C("No, I'd like to see a nurse.", "id"), C("I'm just waiting for a friend.", "x")),
                        N("id", "npc", "Alright. Can I see your identity card to register you?", null, null, C("Here is my ID.", "t", "venue_show_id")),
                        N("t", "npc", "Thank you. Please take a seat; the nurse will call your name.", null, "venue_ticket"),
                        N("x", "npc", "Of course, the waiting area is on your left."));
                    Add("nurse_wait", N("a", "npc", "Hello. Please register at reception first, then I'll call you in."));
                    Add("nurse",
                        N("a", "npc", "Come in, NTARE Shiloh. Have a seat. What brings you in today?", null, null,
                          C("Just a routine check-up.", "b"), C("I've had a headache since this morning.", "c")),
                        N("b", "npc", "Your blood pressure and temperature look normal. Drink water and get enough sleep.", null, "venue_served"),
                        N("c", "npc", "I've noted it. Rest, drink water, and come back if it gets worse. For anything serious, see a doctor.", null, "venue_served"));
                    break;
                case VenueKind.Hotel:
                    Add("reception",
                        N("a", "npc", "Good day and welcome to {place}. Are you checking in?", null, null,
                          C("Yes, I have a booking.", "id"), C("I'm meeting someone in the lounge.", "x")),
                        N("id", "npc", "Wonderful. May I see your identity card, please?", null, null, C("Here is my ID.", "k", "venue_show_id")),
                        N("k", "npc", "Thank you. Here is your key card for room 204. The lifts are on your right. Enjoy your stay!", null, "venue_served"),
                        N("x", "npc", "Of course, please make yourself comfortable in the lounge."));
                    break;
                case VenueKind.Pharmacy:
                    Add("pharmacist",
                        N("a", "npc", "Hello, welcome to {place}. Do you have a prescription?", null, null,
                          C("No, just something for a cold.", "b"), C("I'm just looking, thanks.", "")),
                        N("b", "npc", "Here you are. Follow the dose on the box, and see a doctor if you don't feel better in a few days.", null, "venue_served"));
                    break;
                default:
                    Add("reception",
                        N("a", "npc", "Good day, welcome to {place}. Who would you like to see?", null, null,
                          C("I have an appointment.", "id"), C("Just asking for information.", "x")),
                        N("id", "npc", "May I see your identity card for the visitors' book?", null, null, C("Here is my ID.", "ok", "venue_show_id")),
                        N("ok", "npc", "Thank you, NTARE Shiloh. Please wait a moment, someone will come to you.", null, "venue_served"),
                        N("x", "npc", "Of course. The information board is by the door."));
                    break;
            }
        }

        // For the Editor's play-mode check: as if a line had set this flag; and who is here.
        public void Simulate(string flag) => OnFlag(flag, null);
        public int People => _staff.Count;
        public string CalledTo => _calledTo;
        public VenueKind Kind => _kind;

        void OnFlag(string flag, Npc npc)
        {
            switch (flag)
            {
                case "venue_ticket":
                    _ticket = Random.Range(21, 89);
                    _served = false;
                    _callAt = Time.time + Random.Range(12f, 20f);
                    string what = _kind == VenueKind.Bank ? $"Ticket A{_ticket:00}" : "Registered";
                    _rig?.Toast(_kind == VenueKind.Bank ? $"Your ticket: A{_ticket:00}. Have a seat and wait to be called." : "You are registered. Have a seat and wait to be called.", 5f);
                    Phone.Instance?.Remind($"{what} at {_name}");
                    break;
                case "venue_show_id":
                    Wallet.Instance?.Show(Wallet.Card.Identity);
                    break;
                case "venue_show_both":
                    Wallet.Instance?.Show(5f, Wallet.Card.Identity, Wallet.Card.Student);
                    break;
                case "venue_served":
                    _served = true;
                    _ticket = 0;
                    _calledTo = null;
                    break;
            }
        }

        static AudioClip _chime;

        // The "ding-dong" before a number is called, made here (no recorded sound in the project).
        static AudioClip Chime()
        {
            if (_chime != null)
                return _chime;
            const int rate = 22050;
            var data = new float[(int)(rate * 1.2f)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                float f = t < 0.45f ? 659.3f : 523.3f;
                float local = t < 0.45f ? t : t - 0.45f;
                data[i] = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-local * 3.5f) * 0.5f;
            }
            _chime = AudioClip.Create("Venue chime", data.Length, 1, rate, false);
            _chime.SetData(data, 0);
            return _chime;
        }

        void Update()
        {
            if (_ticket <= 0 || _calledTo != null || Time.time < _callAt)
                return;
            // Your turn: the first free counter (its customer steps aside), or the nurse.
            string role = _kind == VenueKind.Clinic ? "nurse" : null;
            if (_kind == VenueKind.Bank)
                for (int k = 1; k <= 4 && role == null; k++)
                    if (_staff.ContainsKey($"teller{k}"))
                        role = $"teller{k}";
            if (role == null || !_staff.TryGetValue(role, out var who))
            {
                _ticket = 0;
                return;
            }
            _calledTo = role;
            string customer = "customer" + role.Substring(role.Length - 1);
            if (_kind == VenueKind.Bank && _people.TryGetValue(customer, out var c) && c != null)
                c.SetActive(false);   // finished and gone
            who.Configure(who.Id, who.DisplayName, Id(_kind == VenueKind.Clinic ? "nurse" : "teller"), who.GetComponent<PersonAnimator>(), _name, 2.4f);
            string call = _kind == VenueKind.Clinic ? "NTARE Shiloh, please come to Room 1." : $"Ticket A{_ticket:00}, please go to counter {role.Substring(role.Length - 1)}.";
            AudioSource.PlayClipAtPoint(Chime(), who.transform.position + Vector3.up * 2f, 0.7f);
            _announce.Say(who.transform, 2.3f, call, 6f);
            _rig?.Toast(call, 6f);
            var phone = Phone.Instance;
            if (phone != null && !_served)
                phone.Remind(call);
        }
    }
}
