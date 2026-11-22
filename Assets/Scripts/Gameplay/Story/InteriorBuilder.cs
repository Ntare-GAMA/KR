using System.Collections.Generic;
using KR.Gameplay.Interaction;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // The shared materials of the made-on-entry interiors (made once, reused by every building).
    public sealed class InteriorPalette
    {
        public readonly Material Wood, Frame, Ceiling, Fabric, Dark, Metal, Window, Rug, Glass, Fruit;
        public readonly Material[] Walls, Floors;

        public InteriorPalette()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Material M(Color c, float smooth = 0.3f)
            {
                var m = new Material(lit) { enableInstancing = true };
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", smooth);
                return m;
            }
            Wood = M(new Color(0.42f, 0.27f, 0.16f), 0.4f);
            Frame = M(new Color(0.18f, 0.12f, 0.08f), 0.35f);
            Ceiling = M(new Color(0.96f, 0.95f, 0.92f), 0.05f);
            Fabric = M(new Color(0.55f, 0.18f, 0.16f), 0.1f);
            Dark = M(new Color(0.12f, 0.12f, 0.13f), 0.3f);
            Metal = M(new Color(0.6f, 0.62f, 0.64f), 0.6f);
            Rug = M(new Color(0.75f, 0.6f, 0.35f), 0.05f);
            Glass = M(new Color(0.2f, 0.45f, 0.3f), 0.85f);
            Fruit = M(new Color(0.85f, 0.42f, 0.1f), 0.5f);
            Window = new Material(unlit);
            Window.SetColor("_BaseColor", new Color(0.8f, 0.9f, 1f));
            Walls = new[] { M(new Color(0.95f, 0.9f, 0.8f), 0.1f), M(new Color(0.83f, 0.9f, 0.95f), 0.1f),
                            M(new Color(0.86f, 0.93f, 0.84f), 0.1f), M(new Color(0.97f, 0.86f, 0.76f), 0.1f) };
            Floors = new[] { M(new Color(0.62f, 0.33f, 0.22f), 0.45f), M(new Color(0.7f, 0.7f, 0.68f), 0.5f),
                             M(new Color(0.5f, 0.36f, 0.24f), 0.4f) };
        }
    }

    // Where an interior goes: the building's footprint frame, its door and the floor level.
    public struct InteriorFrame
    {
        public Vector3 centre, right, fwd, doorAt, doorOut;
        public float hx, hz, floorY, height;
        public int seed;
    }

    // Builds the inside of one building, sized to it: a home (living room, bedroom, kitchen), a shop or office, or
    // a hall. Walls, floors and big furniture are solid; cups, books, bottles, fruit and balls on the tables and
    // floors can be picked up (Grabbable). Seeded by the building, so a building looks the same each visit.
    public sealed class InteriorBuilder
    {
        const float WallT = 0.15f;

        readonly InteriorPalette _m;
        readonly List<GameObject> _props = new List<GameObject>();
        Transform _room;
        System.Random _rng;
        Vector3 _across, _front, _doorOut;
        float _width, _depth, _doorX;

        public InteriorBuilder(InteriorPalette palette) => _m = palette;

        // Loose things made in the last interior (they leave the room's hierarchy when picked up, so the caller
        // destroys them with the room).
        public IReadOnlyList<GameObject> Props => _props;

        // In "door space": x across the front, z from the front wall (0) to the back wall (depth), y up from the floor.
        // Origin: the middle of the front wall, on the floor.
        Vector3 W(float x, float y, float z) => _front + _across * x + Vector3.up * y - _doorOut * z;

        public GameObject Build(InteriorFrame f, Transform parent, out Transform doorLeaf)
        {
            _props.Clear();
            _rng = new System.Random(f.seed * 7919 + 17);
            _doorOut = f.doorOut;
            var go = new GameObject("Interior");
            _room = go.transform;
            _room.SetParent(parent, false);
            _across = Vector3.Cross(Vector3.up, -_doorOut).normalized;   // right, looking in
            // Size across and deep, from the face the door is on.
            bool doorOnX = Mathf.Abs(Vector3.Dot(_doorOut, f.right)) > 0.7f;
            _width = (doorOnX ? f.hz : f.hx) * 2f - 0.25f;
            _depth = (doorOnX ? f.hx : f.hz) * 2f - 0.25f;
            var mid = f.centre + _doorOut * (doorOnX ? f.hx : f.hz);
            _doorX = Vector3.Dot(f.doorAt - mid, _across);
            _front = new Vector3(mid.x, f.floorY, mid.z);
            float h = Mathf.Clamp(f.height - 0.3f, 2.5f, 3.1f);
            var wall = _m.Walls[_rng.Next(_m.Walls.Length)];
            var floor = _m.Floors[_rng.Next(_m.Floors.Length)];
            float hw = _width * 0.5f;

            // Shell: floor, ceiling, four walls; the front wall has the doorway, the side walls have windows.
            Box(floor, 0f, -0.1f, _depth * 0.5f - 0.06f, _width + 0.3f, 0.2f, _depth + 0.3f, true);
            Box(_m.Ceiling, 0f, h + 0.05f, _depth * 0.5f - 0.06f, _width + 0.3f, 0.1f, _depth + 0.3f, true);
            Box(wall, 0f, h * 0.5f, _depth + 0.02f, _width + 0.3f, h, WallT, true);                   // back
            float dl = _doorX - 0.55f, dr = _doorX + 0.55f;
            Box(wall, (-hw - 0.15f + dl) * 0.5f, h * 0.5f, 0.06f, dl + hw + 0.15f, h, WallT, true);    // front, left of door
            Box(wall, (dr + hw + 0.15f) * 0.5f, h * 0.5f, 0.06f, hw + 0.15f - dr, h, WallT, true);     // front, right of door
            Box(wall, _doorX, (2.2f + h) * 0.5f, 0.06f, 1.1f, h - 2.2f, WallT, true);                  // over the door
            foreach (float side in new[] { -1f, 1f })
            {
                Box(wall, side * (hw + 0.07f), h * 0.5f, _depth * 0.5f, WallT, h, _depth + 0.2f, true);
                for (float z = 1.8f; z < _depth - 1.2f; z += 3.5f)
                    Box(_m.Window, side * (hw - 0.01f), 1.6f, z, 0.03f, 1.1f, 1.2f, false);
            }
            // The door, standing open into the room.
            var leafHinge = new GameObject("Door").transform;
            leafHinge.SetParent(_room, false);
            leafHinge.SetPositionAndRotation(W(_doorX - 0.5f, 0f, 0.08f), Quaternion.LookRotation(-_doorOut) * Quaternion.Euler(0f, -95f, 0f));
            var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(leaf.GetComponent<Collider>());
            leaf.transform.SetParent(leafHinge, false);
            leaf.transform.localPosition = new Vector3(0.5f, 1.06f, 0f);
            leaf.transform.localScale = new Vector3(1.0f, 2.12f, 0.05f);
            leaf.GetComponent<Renderer>().sharedMaterial = _m.Wood;
            doorLeaf = leafHinge;

            float area = _width * _depth;
            if (area < 140f)
                Home(h, wall);
            else if (area < 600f)
                Shop(h);
            else
                Hall(h);
            return go;
        }

        // A home: living room at the front; bedroom and kitchen behind, through doorways.
        void Home(float h, Material wall)
        {
            float hw = _width * 0.5f;
            bool rooms = _depth > 6.5f && _width > 5f;
            float back = rooms ? Mathf.Clamp(_depth * 0.55f, 3.2f, _depth - 3f) : _depth;
            if (rooms)
            {
                // Partition across with a doorway on each half; a wall between bedroom and kitchen.
                float gapL = -hw * 0.5f, gapR = hw * 0.5f;
                Box(wall, (-hw + gapL - 0.45f) * 0.5f, h * 0.5f, back, gapL - 0.45f + hw, h, 0.1f, true);
                Box(wall, 0f, h * 0.5f, back, (gapR - 0.45f) - (gapL + 0.45f), h, 0.1f, true);
                Box(wall, (gapR + 0.45f + hw) * 0.5f, h * 0.5f, back, hw - gapR - 0.45f, h, 0.1f, true);
                Box(wall, 0f, h * 0.5f, (back + _depth) * 0.5f, 0.1f, h, _depth - back, true);
                // Bedroom (left): bed, bedside table, wardrobe.
                float bz = (back + _depth) * 0.5f;
                Bed(-hw * 0.5f, _depth - 1.15f);
                Box(_m.Wood, -hw + 0.3f, 0.3f, _depth - 0.35f, 0.45f, 0.6f, 0.45f, true);
                Prop(-hw + 0.3f, 0.6f, _depth - 0.35f);
                Box(_m.Wood, -0.6f, 1.0f, bz, 0.6f, 2.0f, 1.2f, true);
                Lamp(-hw * 0.5f, bz, h);
                // Kitchen (right): counter along the back, a small table and two chairs.
                Box(_m.Wood, hw * 0.5f, 0.45f, _depth - 0.35f, hw - 0.4f, 0.9f, 0.6f, true);
                Box(_m.Metal, hw * 0.5f, 0.92f, _depth - 0.35f, hw - 0.4f, 0.04f, 0.62f, false);
                Prop(hw * 0.5f - 0.3f, 0.94f, _depth - 0.35f);
                Prop(hw * 0.5f + 0.3f, 0.94f, _depth - 0.35f);
                Table(hw * 0.5f, bz - 0.3f, 0.9f, 0.7f);
                Chair(hw * 0.5f - 0.6f, bz - 0.3f, 90f);
                Chair(hw * 0.5f + 0.6f, bz - 0.3f, -90f);
                Lamp(hw * 0.5f, bz, h);
            }
            // Living room: sofa against a side wall, coffee table, rug, TV unit opposite, a ball on the floor.
            float lz = Mathf.Min(back, _depth) * 0.5f + 0.4f;
            Box(_m.Rug, 0f, 0.005f, lz, Mathf.Min(2.4f, _width - 1.5f), 0.01f, 1.6f, false);
            Sofa(-hw + 0.5f, lz, 90f);
            Box(_m.Wood, 0f, 0.22f, lz, 1.0f, 0.44f, 0.55f, true);
            Prop(-0.2f, 0.44f, lz);
            Prop(0.25f, 0.44f, lz);
            Box(_m.Dark, hw - 0.3f, 0.3f, lz, 0.45f, 0.6f, 1.6f, true);
            Box(_m.Dark, hw - 0.12f, 1.2f, lz, 0.05f, 0.7f, 1.2f, false);
            Ball(0.6f, lz + 1.1f);
            Lamp(0f, lz, h);
        }

        // A shop or office: a counter facing the door, shelves along the sides, two desks.
        void Shop(float h)
        {
            float hw = _width * 0.5f;
            float counterZ = Mathf.Min(_depth - 1.5f, 4f);
            Box(_m.Wood, 0f, 0.5f, counterZ, Mathf.Min(3f, _width - 2f), 1.0f, 0.7f, true);
            Prop(-0.4f, 1.0f, counterZ);
            Prop(0.4f, 1.0f, counterZ);
            for (float z = 1.5f; z < _depth - 1f; z += 2.2f)
                foreach (float side in new[] { -1f, 1f })
                {
                    Box(_m.Metal, side * (hw - 0.3f), 1.0f, z, 0.45f, 2.0f, 1.8f, true);
                    for (int k = 0; k < 4; k++)
                        Box(_rng.Next(2) == 0 ? _m.Fabric : _m.Rug, side * (hw - 0.3f), 0.35f + k * 0.45f, z, 0.35f, 0.25f, 1.5f, false);
                }
            for (float z = counterZ + 2.2f; z < _depth - 1.2f; z += 2.6f)
            {
                Table(-1.2f, z, 1.4f, 0.7f);
                Chair(-1.2f, z - 0.6f, 0f);
                Table(1.2f, z, 1.4f, 0.7f);
                Chair(1.2f, z - 0.6f, 0f);
            }
            for (float z = 2f; z < _depth; z += 5f)
                Lamp(0f, z, h);
        }

        // A large hall: reception desk, waiting seats, rows of desks, plants.
        void Hall(float h)
        {
            float hw = _width * 0.5f;
            Box(_m.Wood, 0f, 0.53f, 4.5f, 3.2f, 1.06f, 0.75f, true);
            Prop(0.6f, 1.06f, 4.5f);
            for (float x = -hw + 1.5f; x < -1.5f; x += 2.4f)
                Sofa(x, 2.2f, 0f);
            for (float z = 8f; z < _depth - 2f; z += 3.2f)
                for (float x = -hw + 2f; x < hw - 1.5f; x += 3.2f)
                {
                    Table(x, z, 1.6f, 0.8f);
                    Chair(x, z - 0.65f, 0f);
                }
            for (float z = 3f; z < _depth; z += 7f)
                for (float x = -hw + 4f; x < hw; x += 8f)
                    Lamp(x, z, h);
            foreach (float side in new[] { -1f, 1f })
            {
                Box(_m.Dark, side * (hw - 0.6f), 0.25f, 1.2f, 0.5f, 0.5f, 0.5f, true);
                Box(_m.Fabric, side * (hw - 0.6f), 0.9f, 1.2f, 0.7f, 0.8f, 0.7f, false);
            }
        }

        void Bed(float x, float z)
        {
            Box(_m.Wood, x, 0.22f, z, 1.5f, 0.44f, 2.0f, true);
            Box(_m.Ceiling, x, 0.5f, z + 0.1f, 1.45f, 0.14f, 1.75f, false);
            Box(_m.Fabric, x, 0.58f, z + 0.25f, 1.45f, 0.04f, 1.35f, false);
            Box(_m.Wood, x, 0.6f, z + 1.0f, 1.5f, 1.2f, 0.08f, true);
        }

        void Sofa(float x, float z, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            BoxR(_m.Fabric, x, 0.22f, z, new Vector3(1.9f, 0.44f, 0.85f), rot, Vector3.zero, true);
            BoxR(_m.Fabric, x, 0.62f, z, new Vector3(1.9f, 0.45f, 0.18f), rot, new Vector3(0f, 0f, 0.34f), true);
        }

        // A table, with something on it more often than not.
        void Table(float x, float z, float w, float d)
        {
            Box(_m.Wood, x, 0.74f, z, w, 0.05f, d, true);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    Box(_m.Frame, x + sx * (w * 0.5f - 0.06f), 0.36f, z + sz * (d * 0.5f - 0.06f), 0.05f, 0.72f, 0.05f, false);
            if (_rng.NextDouble() < 0.7)
                Prop(x - w * 0.2f, 0.765f, z);
            if (_rng.NextDouble() < 0.4)
                Prop(x + w * 0.22f, 0.765f, z + d * 0.15f);
        }

        void Chair(float x, float z, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            BoxR(_m.Frame, x, 0.45f, z, new Vector3(0.44f, 0.05f, 0.44f), rot, Vector3.zero, false);
            BoxR(_m.Frame, x, 0.75f, z, new Vector3(0.44f, 0.55f, 0.04f), rot, new Vector3(0f, 0f, -0.2f), false);
            foreach (float sx in new[] { -0.18f, 0.18f })
                foreach (float sz in new[] { -0.18f, 0.18f })
                    BoxR(_m.Frame, x, 0.22f, z, new Vector3(0.04f, 0.44f, 0.04f), rot, new Vector3(sx, 0f, sz), false);
        }

        void Lamp(float x, float z, float h)
        {
            Box(_m.Window, x, h - 0.03f, z, 0.5f, 0.04f, 0.5f, false);
            var l = new GameObject("Lamp").AddComponent<Light>();
            l.transform.SetParent(_room, false);
            l.transform.position = W(x, h - 0.4f, z);
            l.type = LightType.Point;
            l.range = 7f;
            l.intensity = 1.6f;
            l.color = new Color(1f, 0.94f, 0.84f);
            l.shadows = LightShadows.None;
        }

        // Something small to pick up, standing on a surface at height top: a cup, a book, a bottle or a fruit.
        void Prop(float x, float top, float z)
        {
            int kind = _rng.Next(4);
            GameObject go;
            float half;
            switch (kind)
            {
                case 0:
                    go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    go.name = "Cup";
                    go.transform.localScale = new Vector3(0.08f, 0.05f, 0.08f);
                    go.GetComponent<Renderer>().sharedMaterial = _m.Ceiling;
                    half = 0.05f;
                    break;
                case 1:
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "Book";
                    go.transform.localScale = new Vector3(0.16f, 0.03f, 0.23f);
                    go.GetComponent<Renderer>().sharedMaterial = _rng.Next(2) == 0 ? _m.Fabric : _m.Rug;
                    half = 0.015f;
                    break;
                case 2:
                    go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    go.name = "Bottle";
                    go.transform.localScale = new Vector3(0.07f, 0.12f, 0.07f);
                    go.GetComponent<Renderer>().sharedMaterial = _m.Glass;
                    half = 0.12f;
                    break;
                default:
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = "Fruit";
                    go.transform.localScale = Vector3.one * 0.08f;
                    go.GetComponent<Renderer>().sharedMaterial = _m.Fruit;
                    half = 0.04f;
                    break;
            }
            Loose(go, W(x, top + half + 0.005f, z), 0.3f);
        }

        void Ball(float x, float z)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Ball";
            go.transform.localScale = Vector3.one * 0.22f;
            go.GetComponent<Renderer>().sharedMaterial = _m.Ceiling;
            Loose(go, W(x, 0.12f, z), 0.43f);
        }

        void Loose(GameObject go, Vector3 at, float mass)
        {
            go.transform.SetParent(_room, false);
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-_doorOut) * Quaternion.Euler(0f, (float)_rng.NextDouble() * 360f, 0f));
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Grabbable.Make(go, false, false);
            go.GetComponent<Rigidbody>().mass = mass;
            _props.Add(go);
        }

        void Box(Material m, float x, float y, float z, float w, float h, float d, bool solid) =>
            BoxR(m, x, y, z, new Vector3(w, h, d), Quaternion.identity, Vector3.zero, solid);

        // A box in door space (x across, y up, z into the room), turned by rot about its own centre.
        void BoxR(Material m, float x, float y, float z, Vector3 size, Quaternion rot, Vector3 offset, bool solid)
        {
            if (size.x <= 0.01f || size.y <= 0.01f || size.z <= 0.01f)
                return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            if (!solid)
                Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_room, false);
            var look = Quaternion.LookRotation(-_doorOut) * rot;
            go.transform.SetPositionAndRotation(W(x, y, z) + look * offset, look);
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
