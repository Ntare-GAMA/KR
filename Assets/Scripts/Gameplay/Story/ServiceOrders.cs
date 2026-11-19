using KR.Gameplay.Player;
using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // Orders at cafes, restaurants and bars. Ordering in a conversation (flags order_tea, order_plate, order_water,
    // order_soda) puts the item on the business's terrace table nearest you when the conversation ends; walk up and
    // press Enter / X to drink or eat it. Places without a table (the ALU canteen counter) hand it over to take away.
    // One cup, plate and bottle are reused (no objects created while playing).
    public sealed class ServiceOrders : MonoBehaviour
    {
        [SerializeField] PlayerRig rig;
        [SerializeField] ConversationRunner runner;
        [SerializeField] StoryDirector director;
        [SerializeField] GameObject cup, plate, bottle;
        [SerializeField] float reach = 1.8f;

        string _order;
        Npc _from;
        GameObject _served;
        string _servedOrder;
        float _consumeUntil;
        SpeechBubble _prompt;

        public void Configure(PlayerRig playerRig, ConversationRunner conversations, StoryDirector story,
                              GameObject cupItem, GameObject plateItem, GameObject bottleItem)
        {
            rig = playerRig;
            runner = conversations;
            director = story;
            cup = cupItem;
            plate = plateItem;
            bottle = bottleItem;
        }

        void Start()
        {
            if (runner == null || rig == null)
            {
                enabled = false;
                return;
            }
            runner.FlagSet += OnFlag;
            runner.Finished += OnFinished;
            foreach (var item in new[] { cup, plate, bottle })
                if (item != null)
                    item.SetActive(false);
            _prompt = SpeechBubble.Create(transform);
        }

        void OnDestroy()
        {
            if (runner != null)
            {
                runner.FlagSet -= OnFlag;
                runner.Finished -= OnFinished;
            }
        }

        void OnFlag(string flag, Npc npc)
        {
            // Orders taken inside the dining room are served there (Restaurant), not on the terrace.
            if (npc != null && npc.Id == "waitress")
                return;
            if (flag != null && flag.StartsWith("order_"))
            {
                _order = flag.Substring(6);
                _from = npc;
            }
        }

        void OnFinished(string conversation, Npc npc)
        {
            if (_order == null || npc != _from)
                return;
            string order = _order;
            _order = null;
            var item = order == "plate" ? plate : order == "tea" ? cup : bottle;
            var table = ServiceTable.For(npc);
            var top = table != null ? table.NearestTop(rig.Player.transform.position) : null;
            if (item == null || top == null)
            {
                rig.Toast($"{What(order)} to take away. Enjoy!", 4f);
                director?.SetFlag("ate_out");
                return;
            }
            if (_served != null)
                _served.SetActive(false);
            item.transform.SetPositionAndRotation(top.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            item.SetActive(true);
            SoundBank.Play(order == "plate" ? "plate_down" : order == "tea" ? "pour" : "bottle_open", top.position, 0.9f);
            _served = item;
            _servedOrder = order;
            _consumeUntil = 0f;
            rig.Toast($"Your {What(order).ToLowerInvariant()} is on the table.", 4f);
        }

        void Update()
        {
            if (_served == null || !_served.activeSelf)
                return;
            // Drinking or eating: the item rises a little, then is gone.
            if (_consumeUntil > 0f)
            {
                _served.transform.position += Vector3.up * (0.25f * Time.deltaTime);
                if (Time.time >= _consumeUntil)
                {
                    _served.SetActive(false);
                    _served = null;
                }
                return;
            }
            if (rig.CurrentMode != PlayerRig.Mode.Walking || (runner != null && runner.Talking))
                return;
            var you = rig.Player.transform.position;
            var d = _served.transform.position - you;
            d.y = 0f;
            if (d.magnitude > reach)
                return;
            string key = VrRig.Active ? "X" : "Enter";
            string verb = _servedOrder == "plate" ? "eat" : "drink";
            _prompt.Say(_served.transform, 0.45f, $"<b>{key}</b>  {verb} your {What(_servedOrder).ToLowerInvariant()}", 0.3f);
            if (RigInput.ConfirmPressed && (runner == null || !runner.WantsConfirm))
            {
                _consumeUntil = Time.time + 1.2f;
                SoundBank.Play(_servedOrder == "plate" ? "bite" : "sip", _served.transform.position, 0.8f, false);
                _prompt.Hide();
                rig.Toast(_servedOrder == "plate" ? "You eat. Delicious!" : "You drink. Just what you needed.", 3f);
                director?.SetFlag("ate_out");
            }
        }

        static string What(string order) =>
            order == "tea" ? "African tea" : order == "plate" ? "Plate" : order == "soda" ? "Soda" : "Water";
    }
}
