using System.Collections.Generic;
using KR.Gameplay.Interaction;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // The insides of named places (owner, 2026-11-22: a bank where you sit, talk to a receptionist and wait for a
    // teller; clinics, hospitals and hotels you can go into and deal with people). The layout gives the staff and
    // customers their spots (VenueLife puts people there and runs the routine) and the seats you can use.
    public sealed partial class InteriorBuilder
    {
        // Where people stand or sit in this interior: a role (reception, teller1, nurse, guest...), a world
        // position and the way they face.
        public struct Spot
        {
            public string role;
            public Vector3 at, facing;
        }

        readonly List<Spot> _spots = new List<Spot>();
        readonly List<SeatSpot> _seats = new List<SeatSpot>();

        public IReadOnlyList<Spot> Spots => _spots;
        public IReadOnlyList<SeatSpot> Seats => _seats;

        Vector3 Dir(Quaternion rot, Vector3 local) => Quaternion.LookRotation(-_doorOut) * rot * local;

        // A seat on the floor at (x, z) door space, facing localForward turned by rot.
        void Seat(float x, float z, Quaternion rot, Vector3 localForward)
        {
            var go = new GameObject("Seat");
            go.transform.SetParent(_room, false);
            go.transform.SetPositionAndRotation(W(x, 0f, z), Quaternion.LookRotation(Dir(rot, localForward)));
            _seats.Add(go.AddComponent<SeatSpot>());
        }

        // A person's spot at (x, z), facing towards (tx, tz).
        void Person(string role, float x, float z, float tx, float tz)
        {
            var at = W(x, 0f, z);
            var to = W(tx, 0f, tz) - at;
            to.y = 0f;
            _spots.Add(new Spot { role = role, at = at, facing = to.sqrMagnitude > 0.01f ? to.normalized : -_doorOut });
        }

        // A sign: dark text on a pale board, readable from the room.
        void Sign(string text, float x, float y, float z, float size = 0.05f)
        {
            var go = new GameObject("Sign " + text);
            go.transform.SetParent(_room, false);
            go.transform.SetPositionAndRotation(W(x, y, z), Quaternion.LookRotation(-_doorOut));   // read from the doorway side
            var t = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            t.text = text;
            t.fontSize = 64;
            t.characterSize = size;
            t.anchor = TextAnchor.MiddleCenter;
            t.color = new Color(0.1f, 0.12f, 0.16f);
        }

        void Venue(VenueKind kind, float h)
        {
            // Big buildings: the public area is at most 14 m deep and 18 m wide, closed off by walls (the back office
            // and side rooms), so the counter, the seats and the people are together, as in a real banking hall.
            const float MaxDepth = 14f, MaxWidth = 18f;
            var wall = _m.Walls[0];
            float h2 = h * 0.5f;
            if (_depth > MaxDepth + 0.5f)
            {
                Box(wall, 0f, h2, MaxDepth + 0.05f, _width + 0.3f, h, WallT, true);
                _depth = MaxDepth;
            }
            if (_width > MaxWidth + 0.5f)
            {
                float hw = _width * 0.5f, keep = MaxWidth * 0.5f;
                // Keep the doorway inside the public area.
                float centre = Mathf.Clamp(_doorX, -hw + keep, hw - keep);
                foreach (float side in new[] { -1f, 1f })
                    Box(wall, centre + side * (keep + 0.05f), h2, _depth * 0.5f, WallT, h, _depth + 0.1f, true);
                _front += _across * centre;
                _doorX -= centre;
                _width = MaxWidth;
            }
            switch (kind)
            {
                case VenueKind.Bank: Bank(h); break;
                case VenueKind.Clinic: Clinic(h); break;
                case VenueKind.Pharmacy: Pharmacy(h); break;
                case VenueKind.Hotel: Hotel(h); break;
                default: Office(h); break;
            }
        }

        // Bank: reception by the door, a ticket machine, rows of waiting chairs, and a counter of teller windows.
        void Bank(float h)
        {
            float hw = _width * 0.5f;
            float counterZ = _depth - 1.6f;
            int windows = Mathf.Clamp(Mathf.FloorToInt((_width - 1.5f) / 2.2f), 1, 4);
            float span = windows * 2.2f;
            Box(_m.Wood, 0f, 0.55f, counterZ, span + 0.4f, 1.1f, 0.6f, true);
            for (int k = 0; k <= windows; k++)
                Box(_m.Window, -span * 0.5f + k * 2.2f, 1.6f, counterZ, 0.04f, 1.0f, 0.6f, false);   // glass dividers
            Box(_m.Window, 0f, 1.75f, counterZ - 0.28f, span, 0.7f, 0.02f, false);                  // glass screen
            for (int k = 0; k < windows; k++)
            {
                float x = -span * 0.5f + 1.1f + k * 2.2f;
                Person($"teller{k + 1}", x, counterZ + 0.75f, x, 0f);
                Sign($"{k + 1}", x, 2.35f, counterZ - 0.3f, 0.06f);
                Person($"customer{k + 1}", x, counterZ - 0.85f, x, counterZ);
                Lamp(x, counterZ - 1f, h);
            }
            Sign("TELLERS", 0f, 2.75f, counterZ - 0.3f, 0.05f);
            // Reception and the ticket machine near the door.
            float rx = -hw + 1.6f;
            Box(_m.Wood, rx, 0.53f, 2.4f, 1.6f, 1.06f, 0.6f, true);
            Person("reception", rx, 3.1f, rx, 0f);
            Sign("RECEPTION", rx, 1.9f, 2.1f, 0.035f);
            Box(_m.Dark, hw - 0.6f, 0.75f, 1.6f, 0.45f, 1.5f, 0.4f, true);
            Sign("TICKETS", hw - 0.6f, 1.7f, 1.38f, 0.03f);
            // Waiting chairs facing the counter.
            float rows = Mathf.Max(3.5f, counterZ - 3.2f);
            for (float z = 4f; z <= rows; z += 1.3f)
                for (float x = -1.6f; x <= 1.61f; x += 0.8f)
                    Chair(x, z, 0f);
            Person("waiting", -2.6f, rows - 0.4f, 0f, counterZ);
            Lamp(0f, 3f, h);
        }

        // Clinic or hospital: reception, a waiting area along a wall, and a consulting room behind a partition.
        void Clinic(float h)
        {
            float hw = _width * 0.5f;
            Box(_m.Ceiling, 0f, 0.53f, 2.8f, 2.4f, 1.06f, 0.7f, true);
            Person("reception", 0f, 3.6f, 0f, 0f);
            Sign("RECEPTION", 0f, 1.9f, 2.42f, 0.035f);
            for (float z = 1.8f; z < _depth * 0.55f; z += 0.8f)
                Chair(-hw + 0.5f, z, 90f);
            Person("waiting", -hw + 0.6f, _depth * 0.55f + 0.4f, 0f, 2f);
            // Consulting room at the back, through a doorway in a partition.
            float wallZ = Mathf.Max(5f, _depth - 3.8f);
            Box(_m.Walls[0], -1.2f - (hw - 1.2f) * 0.5f, h * 0.5f, wallZ, hw - 0.2f, h, 0.1f, true);
            Box(_m.Walls[0], 1.2f + (hw - 1.2f) * 0.5f, h * 0.5f, wallZ, hw - 0.2f, h, 0.1f, true);
            Sign("ROOM 1", 0f, 2.3f, wallZ - 0.06f, 0.035f);
            Box(_m.Ceiling, hw - 1.3f, 0.35f, wallZ + 1.7f, 0.8f, 0.7f, 1.9f, true);   // examination bed
            Box(_m.Wood, -hw + 1.2f, 0.38f, wallZ + 1.4f, 1.2f, 0.76f, 0.7f, true);    // desk
            Chair(-hw + 1.2f, wallZ + 0.8f, 0f);
            Person("nurse", -hw + 1.2f, wallZ + 2.1f, 0f, wallZ);
            Lamp(0f, 3f, h);
            Lamp(0f, wallZ + 1.6f, h);
        }

        void Pharmacy(float h)
        {
            float hw = _width * 0.5f;
            float counterZ = Mathf.Min(_depth - 1.8f, 4f);
            Box(_m.Wood, 0f, 0.55f, counterZ, Mathf.Min(3.4f, _width - 1.6f), 1.1f, 0.6f, true);
            for (float x = -hw + 0.8f; x < hw - 0.7f; x += 1.7f)
            {
                Box(_m.Metal, x, 1.1f, counterZ + 1.3f, 1.5f, 2.2f, 0.4f, true);
                for (int k = 0; k < 4; k++)
                    Box(k % 2 == 0 ? _m.Ceiling : _m.Glass, x, 0.4f + k * 0.5f, counterZ + 1.15f, 1.3f, 0.18f, 0.1f, false);
            }
            Person("pharmacist", 0f, counterZ + 0.7f, 0f, 0f);
            Person("customer1", 0.8f, counterZ - 0.8f, 0.8f, counterZ);
            Sign("PHARMACY", 0f, 2.4f, counterZ + 1.05f, 0.05f);
            Lamp(0f, counterZ, h);
        }

        // Hotel lobby: a long reception desk, a lounge with sofas and a coffee table, lift doors, a porter.
        void Hotel(float h)
        {
            float hw = _width * 0.5f;
            float deskZ = _depth - 2f;
            Box(_m.Wood, 0f, 0.55f, deskZ, Mathf.Min(4f, _width - 2f), 1.1f, 0.7f, true);
            Person("reception", 0f, deskZ + 0.8f, 0f, 0f);
            Sign("RECEPTION", 0f, 2.3f, deskZ + 0.6f, 0.045f);
            Box(_m.Rug, -hw * 0.5f, 0.005f, _depth * 0.45f, Mathf.Min(3.2f, hw), 0.01f, 2.4f, false);
            Sofa(-hw * 0.5f, _depth * 0.45f + 1.1f, 0f);
            Sofa(-hw * 0.5f, _depth * 0.45f - 1.1f, 180f);
            Box(_m.Wood, -hw * 0.5f, 0.22f, _depth * 0.45f, 1.0f, 0.44f, 0.6f, true);
            Prop(-hw * 0.5f, 0.44f, _depth * 0.45f);
            Person("guest", -hw * 0.5f + 1.6f, _depth * 0.45f, -hw * 0.5f, _depth * 0.45f);
            foreach (float x in new[] { hw - 1.1f, hw - 2.4f })
                Box(_m.Metal, x, 1.1f, _depth - 0.1f, 1.0f, 2.2f, 0.06f, false);       // lift doors
            Sign("LIFTS", hw - 1.75f, 2.5f, _depth - 0.15f, 0.04f);
            Person("porter", hw - 1.2f, 2.2f, 0f, 0.5f);
            Lamp(0f, deskZ - 0.5f, h);
            Lamp(-hw * 0.5f, _depth * 0.45f, h);
        }

        // Any other office (or a police station): a reception desk, chairs, and desks with staff.
        void Office(float h)
        {
            float hw = _width * 0.5f;
            Box(_m.Wood, 0f, 0.53f, 2.6f, 2.2f, 1.06f, 0.7f, true);
            Person("reception", 0f, 3.4f, 0f, 0f);
            Sign("RECEPTION", 0f, 1.9f, 2.22f, 0.035f);
            for (float x = -hw + 0.8f; x < -1.4f; x += 0.8f)
                Chair(x, 1.4f, 90f);
            int desk = 0;
            for (float z = 5.5f; z < _depth - 1.2f && desk < 4; z += 2.8f)
                for (float x = -1.4f; x <= 1.41f && desk < 4; x += 2.8f)
                {
                    Table(x, z, 1.4f, 0.7f);
                    Person($"desk{++desk}", x, z + 0.7f, x, z);
                }
            Lamp(0f, 3f, h);
        }
    }
}
