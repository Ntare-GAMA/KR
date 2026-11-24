using System.Collections.Generic;
using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // One emergency vehicle (ambulance, police car or detainee van): a hidden copy of the city's own vehicle of that
    // kind, with roof beacons and a siren, that follows a route on the street network. It only knows how to drive
    // itself; EmergencyResponse decides where each one goes and what happens when it gets there.
    public sealed class EmergencyVehicle
    {
        public Transform Body { get; private set; }
        public AudioSource Siren { get; private set; }
        public bool Active { get; private set; }
        public float Speed { get; private set; }

        readonly List<Vector3> _route = new List<Vector3>();
        Renderer[] _beacons;
        int _next;

        static Material _red, _blue, _off;
        static readonly RaycastHit[] Hits = new RaycastHit[8];

        // A copy of the first vehicle in the traffic whose name contains kind; null (and a warning) if there is none.
        public static EmergencyVehicle Create(RoadNetworkTraffic traffic, string kind, string name, Transform parent)
        {
            Transform source = null;
            if (traffic != null)
                for (int i = 0; i < traffic.Count && source == null; i++)
                {
                    var b = traffic.Body(i);
                    if (b != null && b.name.IndexOf(kind, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        source = b;
                }
            if (source == null)
            {
                Debug.LogWarning($"[Emergency] No {kind} in the city's traffic; that response is skipped.");
                return null;
            }
            var go = Object.Instantiate(source.gameObject, parent);
            go.name = name;
            var v = new EmergencyVehicle { Body = go.transform };
            var box = go.GetComponent<BoxCollider>();
            Vector3 top = box != null ? box.center + Vector3.up * box.size.y * 0.5f : new Vector3(0f, 1.8f, 0f);
            float half = box != null ? Mathf.Min(0.45f, box.size.x * 0.25f) : 0.4f;
            var beacons = new List<Renderer>();
            foreach (float side in new[] { -1f, 1f })
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(b.GetComponent<Collider>());
                b.name = "Beacon";
                b.transform.SetParent(go.transform, false);
                b.transform.localPosition = top + new Vector3(side * half, 0.07f, 0f);
                b.transform.localScale = new Vector3(0.3f, 0.12f, 0.2f);
                beacons.Add(b.GetComponent<Renderer>());
            }
            v._beacons = beacons.ToArray();
            v.Siren = SoundBank.Loop(go.transform, "siren", true, 8f, 140f);   // heard only once the vehicle is close enough to see
            go.SetActive(false);
            // Traffic stops for it (and passes it once it is parked at the scene); ignored while it is hidden.
            RoadNetworkTraffic.AddObstacle(go.transform, 1.1f);
            return v;
        }

        // Appears at start facing target, siren on, routed to target.
        public void SetOut(Vector3 start, Vector3 target, RoadGraph graph)
        {
            Body.gameObject.SetActive(true);
            Place(start, (target - start).normalized);
            Plan(graph, target);
            Speed = 0f;
            Active = true;
            if (Siren != null)
            {
                Siren.volume = 0.9f;
                if (!Siren.isPlaying)
                    Siren.Play();
            }
        }

        // Put away: hidden, siren off.
        public void Stand()
        {
            Active = false;
            if (Siren != null)
                Siren.Stop();
            Body.gameObject.SetActive(false);
        }

        public void StopSiren()
        {
            if (Siren != null)
                Siren.Stop();
        }

        public void Halt() => Speed = 0f;

        // Heads straight for one point (close in on the player without the street network).
        public void Direct(Vector3 point)
        {
            _route.Clear();
            _next = 0;
            _route.Add(point);
        }

        public void Plan(RoadGraph graph, Vector3 target)
        {
            _route.Clear();
            _next = 0;
            if (graph != null)
            {
                // Emergency vehicles may use any street, either way.
                var path = graph.Route(graph.Nearest(Body.position), graph.Nearest(target), false);
                if (path != null)
                    _route.AddRange(path);
            }
            _route.Add(target);
            // Carry on from the route point just past the one nearest the vehicle, never back to a point behind it
            // (re-planning every second and a half used to send it round in circles).
            int nearest = 0;
            float best = float.MaxValue;
            for (int k = 0; k < _route.Count; k++)
            {
                float d = Flat(_route[k] - Body.position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = k;
                }
            }
            _next = Mathf.Min(nearest + 1, _route.Count - 1);
            if (_next < _route.Count - 1 && Vector3.Dot(Flat(_route[_next] - Body.position), Flat(Body.forward)) < -0.5f &&
                Flat(_route[_next] - Body.position).magnitude < 12f)
                _next++;
        }

        // Moves along the route at up to topSpeed, keeping `clear` metres free ahead; true once at the end (within
        // arrive metres of it).
        public bool Drive(float topSpeed, float arrive, float clear, float dt)
        {
            if (_route.Count == 0)
                return true;
            while (_next < _route.Count - 1 && Flat(_route[_next] - Body.position).magnitude < 4f)
                _next++;
            Vector3 goal = _route[Mathf.Min(_next, _route.Count - 1)];
            Vector3 to = Flat(goal - Body.position);
            bool last = _next >= _route.Count - 1;
            float remaining = to.magnitude;
            if (last && remaining < arrive)
            {
                Speed = Mathf.MoveTowards(Speed, 0f, 12f * dt);
                _next = _route.Count;
                return true;
            }
            // Never into another emergency vehicle or your car: stop a few metres behind whatever is ahead; near the
            // end of the route that counts as arrived, so it parks behind it instead of waiting there for ever.
            if (clear < 1f)
            {
                Speed = Mathf.MoveTowards(Speed, 0f, 14f * dt);
                if (last && remaining < arrive + 14f)
                {
                    _next = _route.Count;
                    return true;
                }
                return false;
            }
            // Slow for the end of the route and for sharp turns.
            float target = Mathf.Min(topSpeed, Mathf.Sqrt(2f * 7f * clear));
            if (last)
                target = Mathf.Min(target, Mathf.Sqrt(2f * 6f * Mathf.Max(0f, remaining - arrive)));
            float turn = Vector3.Angle(Flat(Body.forward), to);
            target *= Mathf.Lerp(1f, 0.4f, Mathf.InverseLerp(20f, 90f, turn));
            Speed = Mathf.MoveTowards(Speed, target, (Speed < target ? 6f : 10f) * dt);
            if (to.sqrMagnitude > 0.01f)
            {
                var dir = Vector3.RotateTowards(Flat(Body.forward).normalized, to.normalized, Mathf.Deg2Rad * 120f * dt, 0f);
                Place(Body.position + dir * Speed * dt, dir);
            }
            return false;
        }

        // Red and blue beacons alternating while it is out.
        public void Flash()
        {
            if (!Active || _beacons == null)
                return;
            if (_red == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                _red = new Material(shader) { name = "Beacon red" };
                _red.SetColor("_BaseColor", new Color(1f, 0.08f, 0.05f) * 3f);
                _blue = new Material(shader) { name = "Beacon blue" };
                _blue.SetColor("_BaseColor", new Color(0.1f, 0.3f, 1f) * 3f);
                _off = new Material(shader) { name = "Beacon off" };
                _off.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.1f));
            }
            bool a = Mathf.Repeat(Time.time * 2.5f, 1f) < 0.5f;
            _beacons[0].sharedMaterial = a ? _red : _off;
            _beacons[1].sharedMaterial = a ? _off : _blue;
        }

        // On the road surface (never on another vehicle or a person), level with it: the ground is sampled under
        // all four wheels, on every layer (roads and kerbs are not all on the default layer), from well above, so
        // the vehicle never sinks into a raised road or pavement.
        void Place(Vector3 p, Vector3 forward)
        {
            var flat = Flat(forward);
            var rot = flat.sqrMagnitude > 0.001f ? Quaternion.LookRotation(flat) : Body.rotation;
            var right = rot * Vector3.right;
            var fwd = rot * Vector3.forward;
            float front = Surface(p + fwd * 1.6f, p.y), back = Surface(p - fwd * 1.6f, p.y);
            float left = Surface(p - right * 0.85f, p.y), rightY = Surface(p + right * 0.85f, p.y);
            float y = Mathf.Max(Surface(p, p.y), (front + back) * 0.5f, (left + rightY) * 0.5f);
            var pitch = Quaternion.FromToRotation(fwd, (p + fwd * 1.6f + Vector3.up * front - (p - fwd * 1.6f + Vector3.up * back)).normalized);
            Body.SetPositionAndRotation(new Vector3(p.x, y, p.z), Quaternion.Slerp(rot, pitch * rot, 0.8f));
        }

        // The highest walkable surface under p within reach of the current height (2.5 m above, 6 m below).
        static float Surface(Vector3 p, float currentY)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, currentY + 2.5f, p.z), Vector3.down, Hits, 8.5f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider.attachedRigidbody != null || h.normal.y < 0.75f || h.collider is CapsuleCollider || h.collider is CharacterController)
                    continue;
                if (h.point.y > best)
                    best = h.point.y;
            }
            return best > float.MinValue ? best : currentY;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
