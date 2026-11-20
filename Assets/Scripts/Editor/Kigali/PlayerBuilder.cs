using System;
using System.Collections.Generic;
using System.Linq;
using KR.Editor.GIS;
using KR.Editor.Util;
using KR.Gameplay.Player;
using UnityEditor;
using UnityEngine;

namespace KR.Editor.Kigali
{
    // Adds the player to the Story scene: collision shells for the city near the hubs, the first-person player
    // (no visible character, just a pair of hands), a home near Kibagabaga with the player's car (PolyDrive Bavaria
    // Sport on real physics) in the yard, the desktop first-person camera and the PlayerRig.
    internal static class PlayerBuilder
    {
        public sealed class Context
        {
            public Terrain Terrain;
            public Transform Root;
            public string OutDir;
            public PcShowcaseBuilder.Catalog Cat;
            // City objects carry their own colliders (CityObjects); no merged collision shells for them then.
            public bool ObjectColliders;
            public System.Random Rng;
            public Vector3[] Hubs;
            public string[] HubNames;
            public DistrictImporter.Road[] Roads;
            public List<PcShowcaseBuilder.Solid> Solids;
            public ScatterCollector Scatter;
            public List<Vector3> TreeTrunks;
            public Camera Camera;
        }

        const string SettingsDir = "Assets/Settings/Player";
        const string HomeHub = "Kibagabaga";
        const float ShellRadius = 1900f;
        const float ShellChunk = 256f;
        const string GroundLayer = "KR Ground";
        const string PolyDrivePrefab = "Assets/PolyDrive - FREE/Prefabs/Bavaria_Sport-3.prefab";
        const string HandsPrefab = "Assets/PolyOne/Free VR Hands/Prefabs/Free Pack - VR Hands ( Rigged ).prefab";
        const string HandsMeshDir = "Assets/World/PC/kimironko_kibagabaga/Player";
        public const string SatnavMount = "Satnav mount";

        public static PlayerRig Build(Context c)
        {
            var root = new GameObject("Player").transform;
            root.SetParent(c.Root, false);
            var roads = new RoadIndex(c.Roads);
            EditorAssetUtil.EnsureFolder(SettingsDir);
            EditorAssetUtil.EnsureFolder($"{c.OutDir}/Player");
            var tuning = EditorAssetUtil.LoadOrCreate<PlayerTuning>($"{SettingsDir}/PlayerTuning.asset");

            // Roads, kerbs, footpaths and the terrain are "ground": feet and wheels follow them.
            int groundLayer = EditorAssetUtil.EnsureLayer(GroundLayer);
            var roadGroup = c.Root.Find(PcRoadBuilder.GroupName);
            if (roadGroup != null)
                EditorAssetUtil.SetLayerRecursively(roadGroup.gameObject, groundLayer);
            c.Terrain.gameObject.layer = groundLayer;
            Physics.SyncTransforms();

            BuildShells(c, root);

            int hub = Array.FindIndex(c.HubNames, n => n.IndexOf(HomeHub, StringComparison.OrdinalIgnoreCase) >= 0);
            if (hub < 0)
                hub = 0;
            if (!PickHome(c, roads, c.Hubs[hub], c.Hubs[hub] + Vector3.forward * 5000f, out var door, out var outward))
                throw new InvalidOperationException($"[Player] No house found near the {c.HubNames[hub]} hub.");
            var side = Vector3.Cross(Vector3.up, outward);
            float homeYaw = Quaternion.LookRotation(outward).eulerAngles.y;
            var home = Ground(c, door + outward * 1.5f);
            // Park at the kerb in front of the house, on the right-hand side of the road facing the traffic flow
            // (Rwanda drives on the right), so the car starts on level tarmac instead of a sloping yard.
            Vector3 park = door + side * 6.5f + outward * 3f;
            float parkYaw = homeYaw + 90f;
            var doorFlat = new Vector2(door.x, door.z);
            if (roads.NearestDirection(doorFlat, 45f, out var kerb, out var roadDir))
            {
                var dir = new Vector3(roadDir.x, 0f, roadDir.y).normalized;
                var toHouse = new Vector3(doorFlat.x - kerb.x, 0f, doorFlat.y - kerb.y);
                if (Vector3.Dot(Vector3.Cross(Vector3.up, dir), toHouse) < 0f)
                    dir = -dir;
                var candidate = new Vector3(kerb.x, 0f, kerb.y) + Vector3.Cross(Vector3.up, dir) * 2.2f;
                if (!InsideSolid(c, candidate, 1.2f))
                {
                    park = candidate;
                    parkYaw = Quaternion.LookRotation(dir).eulerAngles.y;
                }
            }
            var car = Car(c, root, park, parkYaw);

            var player = SpawnPlayer(c, root, tuning, home, homeYaw, groundLayer);
            var eyes = c.Camera.gameObject.AddComponent<FirstPersonCamera>();
            eyes.enabled = false;

            var rig = root.gameObject.AddComponent<PlayerRig>();
            rig.Configure(tuning, player, eyes, car, home, homeYaw);
            eyes.Configure(tuning, rig);
            AddHands(root, rig);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Player] Home near {c.HubNames[hub]} at {home:F0}; car {(car != null ? "in the yard" : "missing")}.");
            return rig;
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        // On the walkable surface: pavement, kerb or road where there is one, otherwise the terrain.
        static Vector3 Ground(Context c, Vector3 p)
        {
            p.y = PcRoadBuilder.SurfaceHeight(c.Terrain, p);
            return p;
        }

        static bool InsideSolid(Context c, Vector3 p, float margin)
        {
            foreach (var s in c.Solids)
            {
                if (Mathf.Abs(s.Centre.x - p.x) > 60f || Mathf.Abs(s.Centre.z - p.z) > 60f)
                    continue;
                var local = Quaternion.Inverse(Quaternion.Euler(0f, s.Yaw, 0f)) * (p - s.Centre);
                if (Mathf.Abs(local.x) < s.W * 0.5f + margin && Mathf.Abs(local.z) < s.D * 0.5f + margin)
                    return true;
            }
            return false;
        }

        // A house 250-700 m from the hub, close to a road, with a clear front yard.
        static bool PickHome(Context c, RoadIndex roads, Vector3 hub, Vector3 treasure, out Vector3 door, out Vector3 outward)
        {
            door = outward = Vector3.zero;
            float bestScore = float.MaxValue;
            foreach (var s in c.Solids)
            {
                if (!s.House)
                    continue;
                float d = Flat(s.Centre, hub);
                if (d < 250f || d > 700f || Flat(s.Centre, treasure) < 200f)
                    continue;
                var centre2 = new Vector2(s.Centre.x, s.Centre.z);
                if (!roads.Nearest(centre2, 30f, false, out var r, out _, out _))
                    continue;
                var dir = new Vector3(r.x - centre2.x, 0f, r.y - centre2.y).normalized;
                var rot = Quaternion.Euler(0f, s.Yaw, 0f);
                float extent = Mathf.Abs(Vector3.Dot(dir, rot * Vector3.right)) * s.W * 0.5f +
                               Mathf.Abs(Vector3.Dot(dir, rot * Vector3.forward)) * s.D * 0.5f;
                var candidate = s.Centre + dir * (extent + 1.8f);
                var side = Vector3.Cross(Vector3.up, dir);
                if (InsideSolid(c, candidate, 0.4f) || InsideSolid(c, candidate + side * 6.5f + dir * 3f, 1.5f))
                    continue;
                float score = Mathf.Abs(d - 400f);
                if (score < bestScore)
                {
                    bestScore = score;
                    door = candidate;
                    outward = dir;
                }
            }
            return bestScore < float.MaxValue;
        }

        // The player's own car: the PolyDrive Bavaria Sport on real physics (Rigidbody + four WheelColliders), with
        // pivots added so the wheels spin and steer, the steering wheel turns and the pedals press, plus the
        // driver's targets: hand grips at 9 and 3 on the steering-wheel rim, pedal foot points and the hip point.
        static DrivableCar Car(Context c, Transform parent, Vector3 p, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PolyDrivePrefab);
            if (prefab == null)
            {
                Debug.LogError($"[Player] {PolyDrivePrefab} not found; no car for the player.");
                return null;
            }
            var holder = new GameObject("Your car (PolyDrive Bavaria Sport, physics)").transform;
            holder.SetParent(parent, false);
            holder.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var model = EditorAssetUtil.InstantiateUnpacked(prefab, holder);
            EditorAssetUtil.StripRuntimeComponents(model);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            var carMaterials = ConvertCarMaterials(model, $"{c.OutDir}/Player/CarMaterials");
            Transform Find(string name) => model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
            Bounds RB(Transform t) => t != null && t.TryGetComponent<Renderer>(out var r) ? r.bounds : new Bounds(t != null ? t.position : Vector3.zero, Vector3.zero);

            // Length along +Z with the steering wheel ahead of the centre: that is forward.
            var body = EditorAssetUtil.RendererBounds(model);
            if (body.size.x > body.size.z)
            {
                model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                body = EditorAssetUtil.RendererBounds(model);
            }
            var steer = Find("SteeringWheel");
            if (steer != null && RB(steer).center.z < body.center.z)
            {
                model.transform.localRotation *= Quaternion.Euler(0f, 180f, 0f);
                body = EditorAssetUtil.RendererBounds(model);
            }
            if (body.size.z < 3.2f || body.size.z > 6f)
            {
                AssetCatalog.FitVehicle(model, 4.6f);
                body = EditorAssetUtil.RendererBounds(model);
            }
            model.transform.position += Vector3.up * -body.min.y;   // tyres on y = 0
            body = EditorAssetUtil.RendererBounds(model);
            if (steer != null && RB(steer).center.x > body.center.x)
                Debug.LogWarning("[Player] The car's steering wheel is on the right; Rwanda drives on the right, so a left-hand-drive car is expected.");
            Debug.Log($"[Player] Car {prefab.name}: {body.size.z:0.00} m long, {body.size.x:0.00} m wide, {body.size.y:0.00} m high.");

            // Wheels: one pivot per corner at the wheel centre; every LOD of that wheel goes under it.
            string[] corners = { "FL", "FR", "RL", "RR" };
            var pivots = new Transform[4];
            var physics = new WheelCollider[4];
            for (int i = 0; i < 4; i++)
            {
                var lod0 = Find($"Wheel_{corners[i]}_LOD0");
                if (lod0 == null)
                {
                    Debug.LogError($"[Player] Wheel_{corners[i]}_LOD0 missing in {prefab.name}.");
                    continue;
                }
                var wb = RB(lod0);
                var pivot = new GameObject($"Wheel {corners[i]}").transform;
                pivot.SetParent(model.transform, false);
                pivot.SetPositionAndRotation(wb.center, holder.rotation);
                for (int l = 0; l < 4; l++)
                {
                    var part = Find($"Wheel_{corners[i]}_LOD{l}");
                    if (part != null)
                        part.SetParent(pivot, true);
                }
                pivots[i] = pivot;

                const float travel = 0.18f;
                var wcGo = new GameObject($"Wheel collider {corners[i]}");
                wcGo.transform.SetParent(holder, false);
                wcGo.transform.position = wb.center + Vector3.up * travel * 0.5f;   // the wheel rests at its modelled height
                var wc = wcGo.AddComponent<WheelCollider>();
                wc.radius = wb.extents.y;
                wc.mass = 20f;
                wc.suspensionDistance = travel;
                wc.forceAppPointDistance = 0.1f;
                wc.suspensionSpring = new JointSpring { spring = 38000f, damper = 4200f, targetPosition = 0.5f };
                wc.forwardFriction = new WheelFrictionCurve
                    { extremumSlip = 0.4f, extremumValue = 1f, asymptoteSlip = 0.8f, asymptoteValue = 0.6f, stiffness = 1.5f };
                wc.sidewaysFriction = new WheelFrictionCurve
                    { extremumSlip = 0.25f, extremumValue = 1f, asymptoteSlip = 0.5f, asymptoteValue = 0.75f, stiffness = 1.7f };
                physics[i] = wc;
            }

            // Steering wheel: pivot at its centre, local Z along the column towards the driver (the thinnest axis
            // of the mesh), local Y towards 12 o'clock. Grips at 9 and 3, as driving schools teach.
            Transform wheelPivot = null, gripL = null, gripR = null;
            Vector3 wheelCentre = new Vector3(body.center.x - body.extents.x * 0.4f, body.min.y + body.size.y * 0.62f, body.center.z + body.extents.z * 0.1f);
            if (steer != null && steer.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
            {
                var mb = mf.sharedMesh.bounds;
                Vector3 e = Vector3.Scale(mb.extents, Abs(steer.lossyScale));
                Vector3 axisLocal = e.x <= e.y && e.x <= e.z ? Vector3.right : e.y <= e.z ? Vector3.up : Vector3.forward;
                Vector3 axis = steer.TransformDirection(axisLocal).normalized;
                if (Vector3.Dot(axis, -holder.forward) < 0f)
                    axis = -axis;
                float rim = axisLocal == Vector3.right ? Mathf.Max(e.y, e.z) : axisLocal == Vector3.up ? Mathf.Max(e.x, e.z) : Mathf.Max(e.x, e.y);
                wheelCentre = steer.TransformPoint(mb.center);
                wheelPivot = new GameObject("Steering wheel pivot").transform;
                wheelPivot.SetParent(model.transform, false);
                wheelPivot.SetPositionAndRotation(wheelCentre, Quaternion.LookRotation(axis, holder.up));
                steer.SetParent(wheelPivot, true);
                // LookRotation(towards driver, up): local +X is the car's left.
                gripL = Point("Left hand grip", wheelPivot, new Vector3(rim + 0.025f, 0.02f, 0.03f));
                gripR = Point("Right hand grip", wheelPivot, new Vector3(-(rim + 0.025f), 0.02f, 0.03f));
            }
            else
                Debug.LogWarning("[Player] No SteeringWheel mesh; hands use an estimated wheel position.");

            // Satnav screen on the centre of the dashboard, tilted up and turned towards the driver.
            var satnav = Point(SatnavMount, holder, holder.InverseTransformPoint(
                wheelCentre + holder.right * 0.46f - holder.up * 0.02f + holder.forward * 0.14f));
            satnav.localRotation = Quaternion.Euler(20f, 15f, 0f);

            // Pedals hinge at their top edge (about the car's X axis).
            Transform Pedal(string name, out Transform foot)
            {
                foot = null;
                var t = Find(name);
                if (t == null)
                    return null;
                var pb = RB(t);
                var hinge = new GameObject($"{name} hinge").transform;
                hinge.SetParent(model.transform, false);
                hinge.SetPositionAndRotation(new Vector3(pb.center.x, pb.max.y, pb.center.z), holder.rotation);
                t.SetParent(hinge, true);
                // The ankle sits above and behind the pad when the ball of the foot is on it.
                foot = Point($"{name} foot", hinge, hinge.InverseTransformPoint(pb.center + Vector3.up * 0.09f - holder.forward * 0.13f));
                return hinge;
            }
            var accel = Pedal("Pedal_Accelerator", out var accFoot);
            var brake = Pedal("Pedal_Brake", out var brakeFoot);

            // Driver's hips: behind and below the steering wheel, in line with it (left seat, right-hand traffic).
            var hip = Point("Driver hip point", holder, holder.InverseTransformPoint(wheelCentre - holder.forward * 0.62f - holder.up * 0.4f));
            var seat = Point("Driver seat", holder, hip.localPosition - Vector3.up * 0.5f);
            Transform rest;
            if (accFoot != null)
                rest = Point("Right foot rest", holder, holder.InverseTransformPoint(accFoot.position - holder.forward * 0.1f - holder.up * 0.03f));
            else
            {
                accFoot = Point("Accelerator foot (estimated)", holder, hip.localPosition + new Vector3(0.18f, -0.25f, 0.85f));
                rest = accFoot;
            }
            if (brakeFoot == null)
                brakeFoot = accFoot;

            // Body: a lower box clear of the road (the wheels touch the ground) and a cabin box.
            const float clearance = 0.36f;
            var slide = new PhysicsMaterial("Car body (slides)")
            {
                dynamicFriction = 0f, staticFriction = 0f, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            string slidePath = $"{c.OutDir}/Player/CarBody_Slide.physicMaterial";
            AssetDatabase.DeleteAsset(slidePath);
            AssetDatabase.CreateAsset(slide, slidePath);
            float lowerTop = body.min.y + body.size.y * 0.55f;
            var lower = holder.gameObject.AddComponent<BoxCollider>();
            lower.center = new Vector3(body.center.x, (body.min.y + clearance + lowerTop) * 0.5f, body.center.z);
            lower.size = new Vector3(body.size.x * 0.96f, lowerTop - body.min.y - clearance, body.size.z * 0.98f);
            lower.sharedMaterial = slide;
            var cabin = holder.gameObject.AddComponent<BoxCollider>();
            cabin.center = new Vector3(body.center.x, (lowerTop + body.max.y) * 0.5f, body.center.z - body.size.z * 0.05f);
            cabin.size = new Vector3(body.size.x * 0.82f, body.max.y - lowerTop, body.size.z * 0.5f);
            cabin.sharedMaterial = slide;

            var rb = holder.gameObject.AddComponent<Rigidbody>();
            rb.mass = 1500f;   // a mid-size saloon (CarImpacts.PlayerCarMass sets it at runtime too)
            rb.linearDamping = 0.02f;
            rb.angularDamping = 0.6f;
            rb.centerOfMass = new Vector3(body.center.x, body.min.y + 0.42f, body.center.z + 0.05f);
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var car = holder.gameObject.AddComponent<DrivableCar>();
            car.Configure(seat, pivots, physics, wheelPivot, gripL, gripR, accel, brake, accFoot, brakeFoot, rest, hip,
                          new Vector2(body.extents.x, body.extents.z));

            // Headlights: one spot light between the lamps lighting the road ahead, and the lamp materials.
            var beamGo = new GameObject("Headlight beam");
            beamGo.transform.SetParent(holder, false);
            beamGo.transform.localPosition = new Vector3(body.center.x, body.min.y + 0.68f, body.max.z - 0.15f);
            beamGo.transform.localRotation = Quaternion.Euler(7f, 0f, 0f);
            var beam = beamGo.AddComponent<Light>();
            beam.type = LightType.Spot;
            beam.color = new Color(1f, 0.95f, 0.85f);
            beam.range = 55f;
            beam.spotAngle = 80f;
            beam.innerSpotAngle = 40f;
            beam.intensity = 6f;
            beam.shadows = LightShadows.None;
            beam.renderMode = LightRenderMode.ForcePixel;
            beam.enabled = false;
            Material Mat(string n) => carMaterials.TryGetValue(n, out var m) ? m : null;
            var heads = new[] { Mat("LowBeam"), Mat("High_LowBeam"), Mat("HighBeam") }.Where(m => m != null).ToArray();
            foreach (var m in heads.Append(Mat("TailLight")).Append(Mat("ReverseLight")).Where(m => m != null))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                EditorUtility.SetDirty(m);
            }
            holder.gameObject.AddComponent<CarLights>().Configure(car, beam, heads, Mat("TailLight"), Mat("ReverseLight"));
            holder.gameObject.AddComponent<CarAudio>().Configure(SourcedAssets.Sound("engine_start"), SourcedAssets.Sound("engine_idle"),
                SourcedAssets.Sound("engine_rev"), SourcedAssets.Sound("horn"), SourcedAssets.Sound("door_open"),
                SourcedAssets.Sound("door_close"), SourcedAssets.Sound("skid"), SourcedAssets.Sound("crash"), SourcedAssets.Sound("door_slam"));
            Debug.Log($"[Player] Car lights: {heads.Length} headlamp materials, tail {(Mat("TailLight") != null)}, reverse {(Mat("ReverseLight") != null)}.");
            holder.SetPositionAndRotation(Ground(c, p) + Vector3.up * 0.25f, Quaternion.Euler(0f, yaw, 0f));
            return car;
        }

        // PolyDrive ships Built-in pipeline materials (Standard and a double-sided variant), which URP draws as
        // magenta. Each one gets a URP Lit copy in the project (the package files stay untouched): colour, albedo,
        // normal map, metallic, smoothness, emission, cut-out and transparency carry over; the double-sided ones
        // render both faces. The main body paint is purple.
        static readonly Color PurplePaint = new Color(0.46f, 0.13f, 0.66f);

        static Dictionary<string, Material> ConvertCarMaterials(GameObject model, string dir)
        {
            EditorAssetUtil.EnsureFolder(dir);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var done = new Dictionary<Material, Material>();
            Material Convert(Material src)
            {
                if (src == null || src.shader == lit)
                    return src;
                if (done.TryGetValue(src, out var have))
                    return have;
                string path = $"{dir}/{new string(src.name.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_').ToArray())}.mat";
                AssetDatabase.DeleteAsset(path);
                var m = new Material(lit) { name = src.name, enableInstancing = true };
                Color col = src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
                bool body = src.name.StartsWith("Body_Color_#1");
                if (body)
                    col = PurplePaint;
                m.SetColor("_BaseColor", col);
                if (src.HasProperty("_MainTex") && src.GetTexture("_MainTex") != null)
                {
                    m.SetTexture("_BaseMap", src.GetTexture("_MainTex"));
                    m.SetTextureScale("_BaseMap", src.GetTextureScale("_MainTex"));
                    m.SetTextureOffset("_BaseMap", src.GetTextureOffset("_MainTex"));
                }
                if (src.HasProperty("_BumpMap") && src.GetTexture("_BumpMap") != null)
                {
                    m.SetTexture("_BumpMap", src.GetTexture("_BumpMap"));
                    m.SetFloat("_BumpScale", src.HasProperty("_BumpScale") ? src.GetFloat("_BumpScale") : 1f);
                    m.EnableKeyword("_NORMALMAP");
                }
                if (src.HasProperty("_MetallicGlossMap") && src.GetTexture("_MetallicGlossMap") != null)
                {
                    m.SetTexture("_MetallicGlossMap", src.GetTexture("_MetallicGlossMap"));
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                m.SetFloat("_Metallic", body ? 0.55f : src.HasProperty("_Metallic") ? src.GetFloat("_Metallic") : 0f);
                m.SetFloat("_Smoothness", body ? 0.8f : src.HasProperty("_Glossiness") ? src.GetFloat("_Glossiness") : 0.5f);
                if (src.IsKeywordEnabled("_EMISSION") && src.HasProperty("_EmissionColor"))
                {
                    m.SetColor("_EmissionColor", src.GetColor("_EmissionColor"));
                    if (src.HasProperty("_EmissionMap") && src.GetTexture("_EmissionMap") != null)
                        m.SetTexture("_EmissionMap", src.GetTexture("_EmissionMap"));
                    m.EnableKeyword("_EMISSION");
                }
                // Standard's render modes: 0 opaque, 1 cut-out, 2 fade, 3 transparent.
                int mode = src.HasProperty("_Mode") ? Mathf.RoundToInt(src.GetFloat("_Mode")) : 0;
                if (mode == 1)
                {
                    m.SetFloat("_AlphaClip", 1f);
                    m.SetFloat("_Cutoff", src.HasProperty("_Cutoff") ? src.GetFloat("_Cutoff") : 0.5f);
                    m.EnableKeyword("_ALPHATEST_ON");
                }
                else if (mode >= 2)
                {
                    m.SetFloat("_Surface", 1f);
                    m.SetFloat("_Blend", 0f);
                    m.SetFloat("_ZWrite", 0f);
                    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                if (src.name.StartsWith("Glass") && mode >= 2)
                {
                    // Clear glass: almost no tint, and no sky reflection or highlight laid over the view
                    // (on a raked windscreen those read as a milky tint, twice over on double-sided glass).
                    m.SetColor("_BaseColor", new Color(0.9f, 0.94f, 0.96f, 0.04f));
                    m.SetFloat("_Smoothness", 0.3f);
                    m.SetFloat("_BlendModePreserveSpecular", 0f);
                    m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    m.SetFloat("_SpecularHighlights", 0f);
                    m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    m.SetFloat("_EnvironmentReflections", 0f);
                    m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    m.SetFloat("_ReceiveShadows", 0f);
                    m.EnableKeyword("_RECEIVE_SHADOWS_OFF");
                }
                if (src.shader != null && src.shader.name.Contains("DoubleSided"))
                    m.SetFloat("_Cull", 0f);
                AssetDatabase.CreateAsset(m, path);
                done[src] = m;
                return m;
            }
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = r.sharedMaterials.Select(Convert).ToArray();
            Debug.Log($"[Player] Car: {done.Count} PolyDrive materials converted to URP Lit (body paint purple).");
            var byName = new Dictionary<string, Material>();
            foreach (var kv in done)
                byName[kv.Key.name] = kv.Value;
            return byName;
        }

        static Transform Point(string name, Transform parent, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        // The player has no visible character: a capsule that walks (CharacterController) with first-person eyes.
        // The only body you see is the pair of hands (PlayerHands).
        static PlayerMotor SpawnPlayer(Context c, Transform parent, PlayerTuning settings, Vector3 home, float homeYaw, int groundLayer)
        {
            var go = new GameObject("Player");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(home, Quaternion.Euler(0f, homeYaw, 0f));
            var cc = go.AddComponent<CharacterController>();
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.height = 1.75f;
            cc.radius = 0.3f;
            cc.stepOffset = 0.45f;
            cc.slopeLimit = 55f;
            var motor = go.AddComponent<PlayerMotor>();
            motor.Configure(settings, null, c.Camera.transform, 1 << groundLayer);
            go.AddComponent<Footsteps>().Configure(SourcedAssets.Sound("footsteps"));
            go.layer = 2;    // Ignore Raycast
            go.SetActive(false);
            return motor;
        }

        // PolyOne "Free Pack - VR Hands": one rigged mesh with both hands, driven by PlayerHands.
        static void AddHands(Transform parent, PlayerRig rig)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandsPrefab);
            if (prefab == null)
            {
                Debug.LogError($"[Player] {HandsPrefab} not found; the player has no hands.");
                return;
            }
            var hands = EditorAssetUtil.InstantiateUnpacked(prefab, parent);
            hands.name = "Player hands (PolyOne Free VR Hands)";
            EditorAssetUtil.StripRuntimeComponents(hands);
            foreach (var a in hands.GetComponentsInChildren<Animator>(true))
                UnityEngine.Object.DestroyImmediate(a);   // PlayerHands poses the bones; the idle clip would fight it
            var skin = hands.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skin != null)
                Rebind(skin, $"{OutDirOf(skin)}");
            if (skin != null)
            {
                skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                skin.enabled = false;
            }
            EditorAssetUtil.SetLayerRecursively(hands, 2);
            hands.AddComponent<PlayerHands>().Configure(rig, skin);
            Debug.Log("[Player] Hands: PolyOne Free VR Hands (steering wheel and VR controllers).");
        }

        static string OutDirOf(SkinnedMeshRenderer skin) => HandsMeshDir;

        // The two hands are moved independently, but part of each arm is skinned to the rig's shared "Root" bone,
        // which then stretches the arm across the car. A copy of the mesh moves every weight on a bone outside
        // J_Left / J_Right to whichever of those two the vertex is nearer, so each hand is one rigid piece.
        static void Rebind(SkinnedMeshRenderer skin, string dir)
        {
            var bones = skin.bones;
            var left = bones.FirstOrDefault(b => b != null && b.name == "J_Left");
            var right = bones.FirstOrDefault(b => b != null && b.name == "J_Right");
            int li = Array.IndexOf(bones, left), ri = Array.IndexOf(bones, right);
            if (li < 0 || ri < 0 || skin.sharedMesh == null)
            {
                Debug.LogWarning("[Player] Hands: J_Left / J_Right not found among the skin's bones; mesh left as it is.");
                return;
            }
            bool[] onSide = bones.Select(b => b != null && (b.IsChildOf(left) || b.IsChildOf(right))).ToArray();
            var mesh = UnityEngine.Object.Instantiate(skin.sharedMesh);
            mesh.name = "VR Hands (each hand rigid)";
            var bind = mesh.bindposes;
            Vector3 lp = bind[li].inverse.GetColumn(3), rp = bind[ri].inverse.GetColumn(3);
            var verts = mesh.vertices;
            var weights = mesh.boneWeights;
            int moved = 0;
            int Fix(int bone, float w, int target)
            {
                if (w <= 0f || bone < 0 || bone >= onSide.Length || onSide[bone])
                    return bone;
                moved++;
                return target;
            }
            for (int v = 0; v < weights.Length; v++)
            {
                int target = (verts[v] - lp).sqrMagnitude <= (verts[v] - rp).sqrMagnitude ? li : ri;
                var w = weights[v];
                w.boneIndex0 = Fix(w.boneIndex0, w.weight0, target);
                w.boneIndex1 = Fix(w.boneIndex1, w.weight1, target);
                w.boneIndex2 = Fix(w.boneIndex2, w.weight2, target);
                w.boneIndex3 = Fix(w.boneIndex3, w.weight3, target);
                weights[v] = w;
            }
            mesh.boneWeights = weights;
            EditorAssetUtil.EnsureFolder(dir);
            string path = $"{dir}/VRHands_Rigid.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            skin.sharedMesh = mesh;
            Debug.Log($"[Player] Hands: {moved} vertex weights moved off the shared root onto their own hand.");
        }

        // Collision for everything the instanced city draws near the hubs: buildings, parked cars, bus shelters,
        // benches, bins, signs, lamp posts and tree trunks, merged into 256 m mesh colliders.
        static void BuildShells(Context c, Transform parent)
        {
            var chunks = new Dictionary<(int, int), (List<Vector3> v, List<int> t)>();
            var corners = new Vector3[8];
            int[] faces = { 0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0, 4, 7, 6, 4, 6, 5 };
            bool Near(Vector3 p) => c.Hubs.Any(h => Flat(h, p) < ShellRadius);
            void AddBox(Vector3 at)
            {
                var key = ((int)(at.x / ShellChunk), (int)(at.z / ShellChunk));
                if (!chunks.TryGetValue(key, out var mesh))
                    chunks[key] = mesh = (new List<Vector3>(), new List<int>());
                int start = mesh.v.Count;
                mesh.v.AddRange(corners);
                foreach (int f in faces)
                    mesh.t.Add(start + f);
            }
            // Corners: bottom ring (-x-z, +x-z, +x+z, -x+z) then the top ring.
            void Upright(Vector3 baseCentre, float yaw, float w, float d, float bottom, float top)
            {
                var rot = Quaternion.Euler(0f, yaw, 0f);
                for (int y = 0; y < 2; y++)
                    for (int k = 0; k < 4; k++)
                    {
                        float sx = k == 0 || k == 3 ? -0.5f : 0.5f, sz = k < 2 ? -0.5f : 0.5f;
                        var q = baseCentre + rot * new Vector3(sx * w, 0f, sz * d);
                        q.y = y == 0 ? bottom : top;
                        corners[y * 4 + k] = q;
                    }
                AddBox(baseCentre);
            }

            int buildings = 0, props = 0;
            foreach (var s in c.ObjectColliders ? new List<PcShowcaseBuilder.Solid>() : c.Solids)
                if (Near(s.Centre))
                {
                    Upright(s.Centre, s.Yaw, s.W, s.D, s.Bottom, s.Top);
                    buildings++;
                }

            // Parked cars are scattered as "trees" (for their cull distance) but must be solid all over, not a thin trunk.
            var vehicles = new HashSet<GameObject>(c.Cat != null ? c.Cat.Vehicles : new GameObject[0]);
            if (c.Scatter != null && !c.ObjectColliders)
                foreach (var (prefab, m, category) in c.Scatter.Placed)
                {
                    if (category == ScatterCollector.Category.House || category == ScatterCollector.Category.Large)
                        continue;
                    Vector3 at = m.GetColumn(3);
                    if (!Near(at))
                        continue;
                    var b = c.Scatter.PrefabBounds(prefab);
                    float height = m.MultiplyVector(Vector3.up * b.size.y).magnitude;
                    if (height < 0.45f)
                        continue;   // manholes, kerb details, litter
                    var foot = m.MultiplyPoint3x4(new Vector3(b.center.x, b.min.y, b.center.z));
                    if (category == ScatterCollector.Category.Tree && !vehicles.Contains(prefab))
                        Upright(foot, 0f, 0.5f, 0.5f, foot.y - 0.3f, foot.y + Mathf.Min(height, 4f));
                    else if (category == ScatterCollector.Category.Lamp)
                        Upright(foot, 0f, 0.3f, 0.3f, foot.y - 0.3f, foot.y + height);
                    else
                    {
                        for (int y = 0; y < 2; y++)
                            for (int k = 0; k < 4; k++)
                            {
                                float sx = k == 0 || k == 3 ? b.min.x : b.max.x, sz = k < 2 ? b.min.z : b.max.z;
                                corners[y * 4 + k] = m.MultiplyPoint3x4(new Vector3(sx, y == 0 ? b.min.y : b.max.y, sz));
                            }
                        AddBox(at);
                    }
                    props++;
                }

            if (c.TreeTrunks != null && !c.ObjectColliders)
                foreach (var t in c.TreeTrunks)
                    if (Near(t))
                    {
                        float y = c.Terrain.SampleHeight(t) + c.Terrain.transform.position.y;
                        Upright(new Vector3(t.x, y, t.z), 0f, 0.6f, 0.6f, y - 0.3f, y + 4f);
                        props++;
                    }

            var group = new GameObject($"Collision shells ({buildings} buildings, {props} props; invisible)").transform;
            group.SetParent(parent, false);
            foreach (var kv in chunks)
            {
                var mesh = new Mesh { name = $"Shells_{kv.Key.Item1}_{kv.Key.Item2}" };
                mesh.indexFormat = kv.Value.v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(kv.Value.v);
                mesh.SetTriangles(kv.Value.t, 0);
                mesh.RecalculateBounds();
                string path = $"{c.OutDir}/Player/{mesh.name}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                var go = new GameObject(mesh.name);
                go.transform.SetParent(group, false);
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                go.isStatic = true;
            }
        }

        // ---------- Road lookup ----------

        sealed class RoadIndex
        {
            const float Cell = 50f;
            readonly List<(Vector2 a, Vector2 b, string name)> _segments = new List<(Vector2, Vector2, string)>();
            readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();

            public RoadIndex(DistrictImporter.Road[] roads)
            {
                foreach (var r in roads)
                {
                    if (r.pts == null)
                        continue;
                    // Road points are (x, y, z) triples.
                    for (int i = 0; i + 5 < r.pts.Length; i += 3)
                    {
                        var a = new Vector2(r.pts[i], r.pts[i + 2]);
                        var b = new Vector2(r.pts[i + 3], r.pts[i + 5]);
                        int index = _segments.Count;
                        _segments.Add((a, b, r.name));
                        int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / Cell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / Cell);
                        int z0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) / Cell), z1 = Mathf.FloorToInt(Mathf.Max(a.y, b.y) / Cell);
                        for (int z = z0; z <= z1; z++)
                            for (int x = x0; x <= x1; x++)
                            {
                                if (!_cells.TryGetValue(Key(x, z), out var list))
                                    _cells[Key(x, z)] = list = new List<int>();
                                list.Add(index);
                            }
                    }
                }
            }

            static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

            public bool NearestDirection(Vector2 p, float maxDistance, out Vector2 point, out Vector2 direction)
            {
                point = p;
                direction = Vector2.up;
                float best = maxDistance;
                bool found = false;
                int r = Mathf.CeilToInt(maxDistance / Cell);
                int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.y / Cell);
                for (int z = cz - r; z <= cz + r; z++)
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        if (!_cells.TryGetValue(Key(x, z), out var list))
                            continue;
                        foreach (int i in list)
                        {
                            var s = _segments[i];
                            var ab = s.b - s.a;
                            if (ab.sqrMagnitude < 1e-4f)
                                continue;
                            float t = Mathf.Clamp01(Vector2.Dot(p - s.a, ab) / ab.sqrMagnitude);
                            var q = s.a + ab * t;
                            float d = Vector2.Distance(p, q);
                            if (d < best)
                            {
                                best = d;
                                point = q;
                                direction = ab.normalized;
                                found = true;
                            }
                        }
                    }
                return found;
            }

            public bool Nearest(Vector2 p, float maxDistance, bool namedOnly, out Vector2 point, out string name, out float distance)
            {
                point = p;
                name = null;
                distance = maxDistance;
                bool found = false;
                int r = Mathf.CeilToInt(maxDistance / Cell);
                int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.y / Cell);
                for (int z = cz - r; z <= cz + r; z++)
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        if (!_cells.TryGetValue(Key(x, z), out var list))
                            continue;
                        foreach (int i in list)
                        {
                            var s = _segments[i];
                            if (namedOnly && string.IsNullOrEmpty(s.name))
                                continue;
                            var ab = s.b - s.a;
                            float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(p - s.a, ab) / ab.sqrMagnitude);
                            var q = s.a + ab * t;
                            float d = Vector2.Distance(p, q);
                            if (d < distance)
                            {
                                distance = d;
                                point = q;
                                name = s.name;
                                found = true;
                            }
                        }
                    }
                return found;
            }
        }
    }
}
