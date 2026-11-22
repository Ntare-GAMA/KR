using KR.Core;
using KR.Editor.Util;
using KR.Gameplay.Player;
using KR.Gameplay.Story;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;

namespace KR.Editor.Kigali
{
    // Adds the headset rig to the Story scene: rig root > camera offset > Main Camera (head pose) and two hand poses
    // (controller tracking only: nothing is drawn; the character's own hands follow them), a comfort vignette on
    // the head, and the world-space Story HUD.
    // The scene stays playable on a desktop: without an active headset VrRig leaves the follow camera in charge.
    internal static class VrBuilder
    {
        const string Out = "Assets/World/VR";

        public static void Add(PlayerRig playerRig, StoryDirector director)
        {
            var cam = Camera.main;
            if (cam == null || playerRig == null)
            {
                Debug.LogError("[VR] The scene needs a Main Camera and the player rig.");
                return;
            }
            EditorAssetUtil.EnsureFolder(Out);
            var old = GameObject.Find("VR Rig");
            if (old != null)
                Object.DestroyImmediate(old);

            var root = new GameObject("VR Rig");
            var offset = new GameObject("Camera Offset").transform;
            offset.SetParent(root.transform, false);

            // Head: driven by the headset pose only while VR is active (VrRig enables it).
            var headPose = cam.gameObject.GetOrAdd<TrackedPoseDriver>();
            headPose.positionAction = new InputAction("Head position", binding: "<XRHMD>/centerEyePosition");
            headPose.rotationAction = new InputAction("Head rotation", binding: "<XRHMD>/centerEyeRotation");
            headPose.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            headPose.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            headPose.enabled = false;

            var left = Hand("Left Hand", "LeftHand", offset);
            var right = Hand("Right Hand", "RightHand", offset);

            var vignette = Vignette(cam.transform);
            var desktop = cam.GetComponent<FirstPersonCamera>();
            root.AddComponent<VrRig>().Configure(playerRig, offset, cam, headPose, left, right, desktop, vignette);

            if (director != null)
                director.gameObject.AddComponent<VrHud>().Configure(director, playerRig, cam);
            Debug.Log("[VR] Headset rig, controller hands, comfort vignette and world-space HUD added.");
        }

        static GameObject Hand(string name, string usage, Transform parent)
        {
            var hand = new GameObject(name);
            hand.transform.SetParent(parent, false);
            var pose = hand.AddComponent<TrackedPoseDriver>();
            pose.positionAction = new InputAction($"{name} position", binding: $"<XRController>{{{usage}}}/devicePosition");
            pose.rotationAction = new InputAction($"{name} rotation", binding: $"<XRController>{{{usage}}}/deviceRotation");
            pose.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            hand.SetActive(false);
            return hand;
        }

        // A quad just in front of the eyes with a radial alpha ramp: clear in the middle, dark at the edges.
        // VrRig sets its overall alpha from movement speed.
        static Renderer Vignette(Transform head)
        {
            var tex = EditorAssetUtil.GeneratedTexture($"{Out}/T_Vignette.png", 256, (x, y) =>
            {
                float dx = (x - 127.5f) / 127.5f, dy = (y - 127.5f) / 127.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                return new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.95f, r)));
            });
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
            if (importer != null && (!importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp))
            {
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetFloat("_Surface", 1f);                      // transparent
            mat.SetFloat("_Blend", 0f);                        // alpha
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_ZTest", (float)CompareFunction.Always);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f));
            mat.renderQueue = (int)RenderQueue.Overlay;
            string matPath = $"{Out}/M_VR_Vignette.mat";
            AssetDatabase.DeleteAsset(matPath);
            AssetDatabase.CreateAsset(mat, matPath);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.name = "Comfort vignette";
            quad.transform.SetParent(head, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.12f);
            quad.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
            var r = quad.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
            return r;
        }
    }
}
