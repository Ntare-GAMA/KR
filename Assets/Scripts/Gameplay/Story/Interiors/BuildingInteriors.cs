using System.Collections.Generic;
using KR.Gameplay.Interaction;
using KR.Gameplay.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Story
{
    // Every building in the city can be entered. There are thousands of buildings, so the insides are made when
    // you go in, sized to the building you chose:
    //   - doors appear on the buildings near you, on the side facing the street (a pool of ten, reused);
    //   - take the handle and pull the door open (or press E / A at it) and you step into a furnished interior
    //     built inside that building's walls (InteriorBuilder), with things on the tables you can pick up;
    //   - walk back out through the open door (or press E at it) and the street is as it was.
    // Restaurants and cafes keep their own dining room (RestaurantDoor). Added when the scene loads.
    // Split: BuildingIndex finds buildings and their doors, InteriorBuilder makes the rooms, this class runs the
    // doors and going in and out.
    public sealed class BuildingInteriors : MonoBehaviour
    {
        const float DoorReach = 2.3f, ShowDoors = 32f;
        const int DoorPool = 10;

        sealed class DoorVisual
        {
            public Transform root, hinge;
            public DoorHandle handle;
            public int building = -1;
            public Vector3 at, outward;
        }

        static readonly Unity.Profiling.ProfilerMarker ProfileMarker = new Unity.Profiling.ProfilerMarker("KR.Interiors");

        readonly DoorVisual[] _doors = new DoorVisual[DoorPool];
        readonly List<(float d, int i)> _nearby = new List<(float, int)>();
        BuildingIndex _index;
        InteriorPalette _palette;
        InteriorBuilder _builder;
        PlayerRig _rig;
        Navigator _nav;
        SpeechBubble _prompt;
        float _assignTimer;
        DoorVisual _near;

        // Inside a building.
        int _inside = -1;
        GameObject _interior;
        readonly List<GameObject> _props = new List<GameObject>();
        Renderer[] _hidden;
        Collider[] _disabled;
        Vector3 _doorAt, _doorOut;
        float _floorY;
        Transform _doorLeaf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var nav = Object.FindFirstObjectByType<Navigator>();
            if (nav != null && nav.GetComponent<BuildingInteriors>() == null)
                nav.gameObject.AddComponent<BuildingInteriors>();
        }

        public int Count => _index != null ? _index.Count : 0;
        public bool IsInside => _inside >= 0;

        void Start()
        {
            _rig = Object.FindFirstObjectByType<PlayerRig>();
            _nav = GetComponent<Navigator>();
            var city = GameObject.Find("City buildings");
            if (_rig == null || city == null)
            {
                enabled = false;
                return;
            }
            var restaurantDoors = new List<Vector3>();
            foreach (var d in Object.FindObjectsByType<RestaurantDoor>(FindObjectsSortMode.None))
                restaurantDoors.Add(d.transform.position);
            _index = new BuildingIndex(city, restaurantDoors);
            _palette = new InteriorPalette();
            _builder = new InteriorBuilder(_palette);
            for (int i = 0; i < DoorPool; i++)
                _doors[i] = MakeDoor(i);
            _prompt = SpeechBubble.Create(transform);
            Debug.Log($"[Interiors] {_index.Count} buildings can be entered.");
        }

        // ---------- doors outside ----------

        DoorVisual MakeDoor(int i)
        {
            var root = new GameObject($"Building door {i}").transform;
            root.SetParent(transform, false);
            Piece(root, _palette.Frame, new Vector3(-0.56f, 1.1f, 0f), new Vector3(0.12f, 2.2f, 0.1f));
            Piece(root, _palette.Frame, new Vector3(0.56f, 1.1f, 0f), new Vector3(0.12f, 2.2f, 0.1f));
            Piece(root, _palette.Frame, new Vector3(0f, 2.24f, 0f), new Vector3(1.24f, 0.12f, 0.1f));
            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(root, false);
            hinge.localPosition = new Vector3(-0.5f, 0f, 0f);
            Piece(hinge, _palette.Wood, new Vector3(0.5f, 1.07f, 0f), new Vector3(1.0f, 2.12f, 0.05f));
            Piece(hinge, _palette.Metal, new Vector3(0.88f, 1.0f, 0.05f), new Vector3(0.12f, 0.03f, 0.04f));
            // The handle: a trigger the hand can close on; pull it and the door swings out towards you.
            var grip = new GameObject("Handle").transform;
            grip.SetParent(hinge, false);
            grip.localPosition = new Vector3(0.88f, 1.0f, 0.07f);
            var trigger = grip.gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(0.24f, 0.2f, 0.2f);
            var handle = grip.gameObject.AddComponent<DoorHandle>();
            handle.Configure(hinge, 1f);
            var visual = new DoorVisual { root = root, hinge = hinge, handle = handle };
            handle.Opened += _ => OnDoorPulledOpen(visual);
            root.gameObject.SetActive(false);
            return visual;
        }

        void OnDoorPulledOpen(DoorVisual door)
        {
            if (door.building >= 0 && _inside < 0 && _rig.CurrentMode == PlayerRig.Mode.Walking)
                Enter(door);
        }

        void Update()
        {
            using (ProfileMarker.Auto())
                Tick();
        }

        void Tick()
        {
            if (_rig == null)
                return;
            if (_inside >= 0)
            {
                Inside();
                return;
            }
            bool walking = _rig.CurrentMode == PlayerRig.Mode.Walking && !_rig.Busy && !GamePause.Paused;
            _assignTimer -= Time.deltaTime;
            if (_assignTimer <= 0f)
            {
                _assignTimer = 0.5f;
                Assign(walking);
            }
            _near = null;
            if (!walking)
                return;
            var you = _rig.Player.transform.position;
            float best = DoorReach * DoorReach;
            foreach (var d in _doors)
                if (d.building >= 0)
                {
                    float dd = (d.at - you).sqrMagnitude;
                    if (dd < best)
                    {
                        best = dd;
                        _near = d;
                    }
                }
            if (_near == null)
                return;
            bool carNear = _rig.Car != null && !_rig.Car.Driven && (_rig.Car.transform.position - you).sqrMagnitude < 3.5f * 3.5f;
            if (carNear)
                return;
            _prompt.Say(_near.root, 2.5f, VrRig.Active ? "Pull the handle  <size=80%>(or <b>A</b>)</size>  Go in" : "<b>E</b>  Go in", 0.2f);
            if (RigInput.UsePressed)
                Enter(_near);
        }

        // Doors on the nearest buildings.
        void Assign(bool walking)
        {
            if (walking)
                _index.Nearby(_rig.Player.transform.position, ShowDoors, _nearby);
            else
                _nearby.Clear();
            var keep = new HashSet<int>();
            for (int k = 0; k < _nearby.Count && k < DoorPool; k++)
                keep.Add(_nearby[k].i);
            foreach (var d in _doors)
                if (d.building >= 0 && !keep.Contains(d.building))
                {
                    d.building = -1;
                    d.root.gameObject.SetActive(false);
                }
                else if (d.building >= 0)
                    keep.Remove(d.building);
            foreach (int i in keep)
                foreach (var d in _doors)
                    if (d.building < 0)
                    {
                        if (_index.DoorOf(i, Graph, out d.at, out d.outward))
                        {
                            d.building = i;
                            d.root.SetPositionAndRotation(d.at + d.outward * 0.04f, Quaternion.LookRotation(d.outward));
                            d.handle.ResetDoor();
                            d.root.gameObject.SetActive(true);
                        }
                        break;
                    }
        }

        World.RoadGraph Graph => _nav != null ? _nav.Graph : null;

        // ---------- going in and out ----------

        // Goes into building i as if at its door (the Editor's play-mode checks).
        public bool TryEnter(int i)
        {
            if (_inside >= 0)
                Leave();
            if (_index == null || i < 0 || i >= _index.Count || !_index.DoorOf(i, Graph, out var at, out var outward))
                return false;
            var d = _doors[0];
            d.building = i;
            d.at = at;
            d.outward = outward;
            Enter(d);
            d.building = -1;
            return true;
        }

        // The enterable building nearest p within radius, or -1 (the Editor's play-mode checks).
        public int BuildingNear(Vector3 p, float radius)
        {
            if (_index == null)
                return -1;
            _index.Nearby(p, radius, _nearby);
            return _nearby.Count > 0 ? _nearby[0].i : -1;
        }

        public void ForceLeave()
        {
            if (_inside >= 0)
                Leave();
        }

        void Enter(DoorVisual door)
        {
            int i = door.building;
            var b = _index[i];
            _index.Frame(i, out var centre, out var right, out var fwd, out float hx, out float hz, out float height);
            _doorAt = door.at;
            _doorOut = door.outward;
            VrRig.Fade(0.35f);
            _hidden = b.t.GetComponentsInChildren<Renderer>();
            foreach (var r in _hidden)
                r.enabled = false;
            _disabled = b.t.GetComponentsInChildren<Collider>();
            foreach (var c in _disabled)
                c.enabled = false;
            // The floor level with the highest ground inside, so nothing pokes through it.
            _floorY = door.at.y;
            for (int sx = -1; sx <= 1; sx++)
                for (int sz = -1; sz <= 1; sz++)
                    if (BuildingIndex.Ground(centre + right * (sx * (hx - 0.5f)) + fwd * (sz * (hz - 0.5f)), b.t, out float g))
                        _floorY = Mathf.Max(_floorY, g);
            _floorY += 0.06f;
            // A named place in (or right by) this building: a bank, clinic, hotel... gets its own inside and people.
            var kind = Venue.Near(_nav, centre, Mathf.Max(hx, hz) + 12f, out string venueName);
            if (kind == VenueKind.Restaurant || kind == VenueKind.Shop || kind == VenueKind.Market)
                kind = VenueKind.None;
            _interior = _builder.Build(new InteriorFrame
            {
                centre = centre, right = right, fwd = fwd, hx = hx, hz = hz, height = height,
                doorAt = _doorAt, doorOut = _doorOut, floorY = _floorY, seed = i, venue = kind, venueName = venueName,
            }, transform, out _doorLeaf);
            if (kind != VenueKind.None)
                VenueLife.Create(_interior, kind, venueName, _builder);
            _props.Clear();
            _props.AddRange(_builder.Props);
            _inside = i;
            KR.Gameplay.World.WeatherCycle.Sheltered = true;
            _bounds = (centre, right, fwd, hx, hz);
            door.root.gameObject.SetActive(false);
            float inYaw = Quaternion.LookRotation(-_doorOut).eulerAngles.y;
            _rig.Player.Teleport(new Vector3(_doorAt.x, _floorY, _doorAt.z) - _doorOut * 1.3f, inYaw);
            _rig.DesktopCamera?.LookAlong(inYaw, 4f);   // face into the room, not wherever you were looking outside
            string inside = kind != VenueKind.None ? $"You are in {venueName}. " : "";
            _rig.Toast(inside + (VrRig.Active ? "Grip to pick things up, X to talk to people. Walk back out through the door to leave."
                                              : "G picks things up, Enter talks to people, E sits down. Walk back out through the door to leave."), 5f);
        }

        (Vector3 centre, Vector3 right, Vector3 fwd, float hx, float hz) _bounds;

        void Inside()
        {
            var you = _rig.Player.transform.position;
            var d = you - _bounds.centre;
            float x = Vector3.Dot(d, _bounds.right), z = Vector3.Dot(d, _bounds.fwd);
            bool out_ = Mathf.Abs(x) > _bounds.hx + 0.5f || Mathf.Abs(z) > _bounds.hz + 0.5f || Mathf.Abs(you.y - _floorY) > 6f;
            bool atDoor = (you - (_doorAt - _doorOut * 0.6f)).sqrMagnitude < 1.4f * 1.4f;
            if (atDoor && _rig.CurrentMode == PlayerRig.Mode.Walking && !_rig.Busy)
            {
                _prompt.Say(_doorLeaf != null ? _doorLeaf : _interior.transform, 2.4f, $"<b>{(VrRig.Active ? "A" : "E")}</b>  Go out", 0.2f);
                if (RigInput.UsePressed)
                {
                    VrRig.Fade(0.35f);
                    float outYaw = Quaternion.LookRotation(_doorOut).eulerAngles.y;
                    _rig.Player.Teleport(new Vector3(_doorAt.x, _doorAt.y, _doorAt.z) + _doorOut * 1.2f, outYaw);
                    _rig.DesktopCamera?.LookAlong(outYaw, 4f);
                    out_ = true;
                }
            }
            if (out_)
                Leave();
        }

        void Leave()
        {
            if (_hidden != null)
                foreach (var r in _hidden)
                    if (r != null) r.enabled = true;
            if (_disabled != null)
                foreach (var c in _disabled)
                    if (c != null) c.enabled = true;
            // Things picked up inside go with the room (one still in your hand is dropped first).
            foreach (var p in _props)
                if (p != null)
                    Destroy(p);
            _props.Clear();
            if (_interior != null)
                Destroy(_interior);
            _interior = null;
            _inside = -1;
            KR.Gameplay.World.WeatherCycle.Sheltered = false;
            _assignTimer = 0f;
        }

        void OnDisable()
        {
            if (_inside >= 0)
                Leave();
        }

        static void Piece(Transform parent, Material m, Vector3 local, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
        }
    }
}
