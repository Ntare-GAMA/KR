using KR.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using KR.Gameplay.Player;
using KR.Gameplay.World;
using UnityEngine;
using UnityEngine.UI;

namespace KR.Gameplay.Story
{
    // A table you can be shown to: where you sit, where the plate and the drink go, where the waitress stands.
    [Serializable]
    public sealed class DiningSeat
    {
        public Transform seat, plate, drink, waiter;
    }

    // Eating in. Ask the staff at the door of any cafe or restaurant to eat inside ("dine_in"): you step into the
    // dining room (one room serves every restaurant; its board shows the name of the one you entered). The
    // waitress comes to the door, greets you and walks you to a table; sit (Enter / X), order from her ("svc_dine"),
    // she brings your food and drink from the counter on a tray and puts them in front of you; eat and drink a few
    // bites at a time, ask for the bill ("svc_dine_bill"), stand up and leave by the door. Leave any time by the door.
    public sealed class Restaurant : MonoBehaviour
    {
        [SerializeField] PlayerRig rig;
        [SerializeField] ConversationRunner runner;
        [SerializeField] StoryDirector director;
        [SerializeField] Npc waitress;
        [SerializeField] Transform entry, exitDoor, home, aisle;
        [SerializeField] Text board;
        [SerializeField] DiningSeat[] seats;
        [SerializeField, Tooltip("Plated dishes by order flag suffix, e.g. brochette, fish, stew, plate.")] string[] dishKeys;
        [SerializeField] GameObject[] dishes;
        [SerializeField, Tooltip("Drinks by order flag suffix: tea, juice, water.")] string[] drinkKeys;
        [SerializeField] GameObject[] drinks;
        [SerializeField] AudioSource music;
        [SerializeField] AudioClip doorSound;
        [SerializeField] float walkSpeed = 1.15f;

        RestaurantDoor _door, _pendingDoor;
        Npc _pendingStaff;
        bool _inside, _seated;
        DiningSeat _seat;
        string _dishKey, _drinkKey;
        GameObject _dish, _drink;
        int _dishBites, _drinkSips;
        Coroutine _visit;
        SpeechBubble _say, _prompt;
        PersonAnimator _anim;
        Vector3 _homePos;
        Quaternion _homeRot;

        public bool Inside => _inside;

        public void SetDoorSound(AudioClip clip) => doorSound = clip;

        AudioSource _doorSrc;

        // Its own source: the music stops on the way out, and that would cut the door off.
        void DoorSound()
        {
            // A hinged door or a sliding one, whichever this visit gets.
            if (UnityEngine.Random.value < 0.5f && SoundBank.Get("door_restaurant") != null)
            {
                SoundBank.Play("door_restaurant", rig.Player.transform.position, 0.8f, false);
                return;
            }
            if (doorSound == null)
                return;
            if (_doorSrc == null)
            {
                _doorSrc = gameObject.AddComponent<AudioSource>();
                _doorSrc.playOnAwake = false;
                _doorSrc.spatialBlend = 0f;
            }
            _doorSrc.PlayOneShot(doorSound, 0.8f);
        }

        public void Configure(PlayerRig playerRig, ConversationRunner conversations, StoryDirector story, Npc server,
                              Transform entryPoint, Transform exitPoint, Transform counterSpot, Transform aisleCentre, Text nameBoard,
                              DiningSeat[] tables, string[] dishFlags, GameObject[] dishItems, string[] drinkFlags, GameObject[] drinkItems,
                              AudioSource backgroundMusic)
        {
            rig = playerRig;
            runner = conversations;
            director = story;
            waitress = server;
            entry = entryPoint;
            exitDoor = exitPoint;
            home = counterSpot;
            aisle = aisleCentre;
            board = nameBoard;
            seats = tables;
            dishKeys = dishFlags;
            dishes = dishItems;
            drinkKeys = drinkFlags;
            drinks = drinkItems;
            music = backgroundMusic;
        }

        void Start()
        {
            if (rig == null || runner == null || waitress == null)
            {
                enabled = false;
                return;
            }
            runner.FlagSet += OnFlag;
            runner.Finished += OnFinished;
            _anim = waitress.GetComponent<PersonAnimator>();
            _homePos = home.position;
            _homeRot = home.rotation;
            _say = SpeechBubble.Create(transform);
            _prompt = SpeechBubble.Create(transform);
            foreach (var g in dishes) if (g != null) g.SetActive(false);
            foreach (var g in drinks) if (g != null) g.SetActive(false);
            ResetWaitress();
        }

        void OnDestroy()
        {
            if (runner == null)
                return;
            runner.FlagSet -= OnFlag;
            runner.Finished -= OnFinished;
        }

        void OnFlag(string flag, Npc npc)
        {
            if (flag == "dine_in")
            {
                _pendingDoor = RestaurantDoor.For(npc);
                _pendingStaff = npc;
                return;
            }
            if (!_inside || npc != waitress || flag == null || !flag.StartsWith("order_"))
                return;
            string key = flag.Substring(6);
            if (Array.IndexOf(drinkKeys, key) >= 0)
                _drinkKey = key;
            else if (Array.IndexOf(dishKeys, key) >= 0)
                _dishKey = key;
        }

        void OnFinished(string conversation, Npc npc)
        {
            if (_pendingDoor != null && npc == _pendingStaff && !_inside)
            {
                var door = _pendingDoor;
                _pendingDoor = null;
                Enter(door);
            }
        }

        public void Enter(RestaurantDoor door)
        {
            if (_inside || door == null)
                return;
            _door = door;
            _visit = StartCoroutine(Visit());
        }

        AudioSource _murmur, _cafe, _kitchen;

        // The room's sound: chatter from the other tables, and cutlery and plates from the kitchen counter.
        void RoomSound()
        {
            if (_murmur == null)
            {
                _murmur = SoundBank.Loop(transform, "murmur", false);
                _cafe = SoundBank.Loop(transform, "cafe", false);
                _kitchen = SoundBank.Loop(transform, "kitchen", true, 2f, 14f);
                if (_kitchen != null)
                    _kitchen.transform.position = home.position + Vector3.up;
            }
            SoundBank.Fade(_murmur, _inside ? 0.3f : 0f, 0.6f);
            SoundBank.Fade(_cafe, _inside ? 0.18f : 0f, 0.6f);
            SoundBank.Fade(_kitchen, _inside ? 0.45f : 0f, 0.6f);
        }

        void Update()
        {
            RoomSound();
            // A conversation ending frees the player; while seated at the table they stay put.
            if (_seated && !runner.Talking)
                rig.Busy = true;
            if (!_inside || _seated || rig.CurrentMode != PlayerRig.Mode.Walking)
                return;
            // The way out, any time you are on your feet.
            if (Near(exitDoor.position, 1.6f))
            {
                _prompt.Say(exitDoor, 2.2f, $"<b>{Key}</b>  Leave {_door.PlaceName}", 0.3f);
                if (RigInput.ConfirmPressed && !runner.Talking)
                    Leave();
            }
        }

        string Key => VrRig.Active ? "X" : "Enter";

        bool Near(Vector3 p, float r)
        {
            var d = p - rig.Player.transform.position;
            d.y = 0f;
            return d.magnitude < r;
        }

        IEnumerator Visit()
        {
            VrRig.Fade(0.6f);
            yield return new WaitForSeconds(0.25f);
            _inside = true;
            board.text = _door.PlaceName;
            rig.Player.Teleport(entry.position, entry.eulerAngles.y);
            if (music != null)
                music.Play();
            DoorSound();
            ClearTable();
            ResetWaitress();
            yield return new WaitForSeconds(0.8f);

            // She comes to the door, welcomes you and shows you to a table.
            yield return Walk(entry.position + entry.forward * 1.7f);
            Face(rig.Player.transform.position);
            _anim?.Greet(rig.Player.transform.position);
            Say($"Muraho! Welcome to {_door.PlaceName}. Follow me, I'll show you to a table.", 4f);
            yield return new WaitForSeconds(2.2f);
            _seat = seats[UnityEngine.Random.Range(0, seats.Length)];
            yield return Walk(_seat.waiter.position);
            Face(_seat.seat.position);
            _anim?.PlayAction(0);
            Say("Here you are. Have a seat.", 3.5f);

            // You sit when you are ready.
            while (!_seated)
            {
                if (rig.CurrentMode == PlayerRig.Mode.Walking && Near(_seat.seat.position, 1.5f) && !runner.Talking)
                {
                    _prompt.Say(_seat.plate, 0.4f, $"<b>{Key}</b>  Sit down", 0.3f);
                    if (RigInput.ConfirmPressed)
                        SitDown();
                }
                yield return null;
            }

            // The order.
            yield return new WaitForSeconds(0.8f);
            Face(rig.Player.transform.position);
            _dishKey = _drinkKey = null;
            runner.Begin(waitress, "svc_dine");
            yield return null;
            while (runner.Talking)
                yield return null;
            if (_dishKey == null && _drinkKey == null)
            {
                Say("No problem. Call me when you are ready.", 3f);
                yield return Walk(_homePos);
                Face(_homePos + _homeRot * Vector3.forward);
                yield return WaitForCall("Call the waitress");
                yield return Walk(_seat.waiter.position);
                Face(rig.Player.transform.position);
                runner.Begin(waitress, "svc_dine");
                yield return null;
                while (runner.Talking)
                    yield return null;
            }

            // To the counter, then back with the tray.
            if (_dishKey != null || _drinkKey != null)
            {
                Say("Coming right up!", 2.5f);
                yield return Walk(_homePos);
                Face(_homePos + _homeRot * Vector3.forward);
                yield return new WaitForSeconds(7f);
                _dish = Pick(dishKeys, dishes, _dishKey);
                _drink = Pick(drinkKeys, drinks, _drinkKey);
                Carry(_dish, new Vector3(0.12f, 1.05f, 0.38f));
                Carry(_drink, new Vector3(-0.12f, 1.05f, 0.38f));
                yield return Walk(_seat.waiter.position);
                Face(_seat.seat.position);
                Serve(_dish, _seat.plate);
                Serve(_drink, _seat.drink);
                Handheld(_dish, true);
                Handheld(_drink, false);
                Say("Enjoy your meal!", 3f);
                _dishBites = _dish != null ? 3 : 0;
                _drinkSips = _drink != null ? 3 : 0;
                yield return new WaitForSeconds(1f);
                yield return Walk(_homePos);
                Face(_homePos + _homeRot * Vector3.forward);

                // Eat and drink: pick it up and bring it to your mouth (a bite or a sip each time), or press the key.
                _bitFood = _bitDrink = false;
                while (_dishBites > 0 || _drinkSips > 0)
                {
                    bool next = _dishBites > 0;
                    var shown = next ? _dish : _drink;
                    _prompt.Say(shown.transform, 0.35f, VrRig.Active
                        ? $"Pick it up and bring it to your mouth  <size=80%>(or <b>{Key}</b>)</size>"
                        : $"<b>{Key}</b>  {(next ? "Eat" : "Drink")}  <size=80%>(G picks it up)</size>", 0.3f);
                    int which = _bitFood && _dishBites > 0 ? 1 : _bitDrink && _drinkSips > 0 ? 2 : RigInput.ConfirmPressed ? (next ? 1 : 2) : 0;
                    _bitFood = _bitDrink = false;
                    if (which != 0)
                    {
                        bool food = which == 1;
                        var item = food ? _dish : _drink;
                        if (food) _dishBites--; else _drinkSips--;
                        var at = item.transform.position;
                        if (food)
                            SoundBank.Play(UnityEngine.Random.value < 0.5f ? "bite" : "cutlery", at, 0.8f, false);
                        else
                            SoundBank.Play("sip", at, 0.8f, false);
                        int left = food ? _dishBites : _drinkSips;
                        if (left == 0)
                            item.SetActive(false);
                        else
                            item.transform.localScale *= 0.8f;
                    }
                    yield return null;
                }
                director?.SetFlag("ate_out");
            }

            // The bill, then off you go.
            yield return WaitForCall("Ask for the bill");
            yield return Walk(_seat.waiter.position);
            Face(rig.Player.transform.position);
            runner.Begin(waitress, "svc_dine_bill");
            yield return null;
            while (runner.Talking)
                yield return null;
            StandUp();
            Say("Murakoze! Come again soon.", 3f);
            yield return Walk(_homePos);
            Face(_homePos + _homeRot * Vector3.forward);
            _visit = null;
        }

        IEnumerator WaitForCall(string label)
        {
            while (true)
            {
                if (_seated)
                {
                    _prompt.Say(_seat.plate, 0.45f, $"<b>{Key}</b>  {label}", 0.3f);
                    if (RigInput.ConfirmPressed)
                        yield break;
                }
                else if (!_inside)
                    yield break;
                yield return null;
            }
        }

        void SitDown()
        {
            _seated = true;
            rig.Busy = true;
            SoundBank.Play("chair", _seat.seat.position, 0.8f);
            rig.Player.EnterSeat(_seat.seat);
        }

        void StandUp()
        {
            if (!_seated)
                return;
            _seated = false;
            rig.Busy = false;
            SoundBank.Play("chair", _seat.seat.position, 0.8f);
            var at = _seat.waiter.position + (_seat.seat.position - _seat.waiter.position) * 0.4f;
            rig.Player.LeaveSeat(at, _seat.seat.eulerAngles.y + 180f);
        }

        void Leave()
        {
            if (_visit != null)
                StopCoroutine(_visit);
            _visit = null;
            StandUp();
            VrRig.Fade(0.6f);
            DoorSound();
            rig.Player.Teleport(_door.Outside, _door.OutsideYaw);
            if (music != null)
                music.Stop();
            _inside = false;
            ClearTable();
            ResetWaitress();
            _prompt.Hide();
            _say.Hide();
        }

        void ResetWaitress()
        {
            waitress.transform.SetPositionAndRotation(_homePos, _homeRot);
        }

        void ClearTable()
        {
            foreach (var g in dishes) if (g != null) { g.SetActive(false); g.transform.SetParent(transform, true); }
            foreach (var g in drinks) if (g != null) { g.SetActive(false); g.transform.SetParent(transform, true); }
            _dish = _drink = null;
        }

        static GameObject Pick(string[] keys, GameObject[] items, string key)
        {
            if (key == null)
                return null;
            int i = Array.IndexOf(keys, key);
            return i >= 0 && i < items.Length ? items[i] : null;
        }

        void Carry(GameObject item, Vector3 local)
        {
            if (item == null)
                return;
            item.transform.SetParent(waitress.transform, false);
            item.transform.localPosition = local;
            item.transform.localRotation = Quaternion.identity;
            item.transform.localScale = Vector3.one;
            item.SetActive(true);
        }

        bool _bitFood, _bitDrink;

        // Served food and drink can be picked up; let go and it goes back on the table. Bringing it to your mouth
        // counts as a bite or a sip.
        void Handheld(GameObject item, bool food)
        {
            if (item == null)
                return;
            var grab = KR.Gameplay.Interaction.Grabbable.Make(item, true, true);
            grab.RememberHome();
            var edible = item.GetOrAdd<KR.Gameplay.Interaction.Edible>();
            edible.Bitten -= OnDishBitten;
            edible.Bitten -= OnDrinkBitten;
            edible.Bitten += food ? OnDishBitten : OnDrinkBitten;
        }

        void OnDishBitten(KR.Gameplay.Interaction.Edible e) => _bitFood = true;
        void OnDrinkBitten(KR.Gameplay.Interaction.Edible e) => _bitDrink = true;

        void Serve(GameObject item, Transform spot)
        {
            if (item == null || spot == null)
                return;
            item.transform.SetParent(spot, false);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
            item.transform.localScale = Vector3.one;
            // Plates are set down; tea is poured; juice and water bottles are opened.
            string sound = item == _dish ? "plate_down" : _drinkKey == "tea" ? "pour" : "bottle_open";
            SoundBank.Play(sound, spot.position, 0.9f);
        }

        // Walks the waitress to a point along the aisle (out to the aisle, along it, then across), at walking pace.
        IEnumerator Walk(Vector3 target)
        {
            var path = new List<Vector3>();
            if (aisle != null)
            {
                Vector3 a = aisle.InverseTransformPoint(waitress.transform.position), b = aisle.InverseTransformPoint(target);
                path.Add(aisle.TransformPoint(new Vector3(0f, a.y, a.z)));
                path.Add(aisle.TransformPoint(new Vector3(0f, a.y, b.z)));
            }
            path.Add(target);
            foreach (var p in path)
            {
                var goal = new Vector3(p.x, waitress.transform.position.y, p.z);
                while (true)
                {
                    var to = goal - waitress.transform.position;
                    float d = to.magnitude;
                    if (d < 0.05f)
                        break;
                    float step = walkSpeed * Time.deltaTime;
                    waitress.transform.position += to / d * Mathf.Min(step, d);
                    if (d > 0.1f)
                        waitress.transform.rotation = Quaternion.Slerp(waitress.transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 8f);
                    yield return null;
                }
            }
        }

        void Face(Vector3 p)
        {
            var d = p - waitress.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.01f)
                waitress.transform.rotation = Quaternion.LookRotation(d);
        }

        void Say(string line, float seconds) => _say.Say(waitress.transform, 2.15f, line, seconds);
    }
}
