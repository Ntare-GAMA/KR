using System;
using System.IO;
using System.Linq;
using KR.Editor.Kigali;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace KR.Editor.XR
{
    // Meta Quest (standalone) project settings and build. Command line:
    //   -buildTarget Android -executeMethod KR.Editor.XR.QuestSetup.ApplyBatch
    //   -buildTarget Android -executeMethod KR.Editor.XR.QuestSetup.BuildApkBatch   (writes Builds/Quest/KigaliRushVR.apk)
    // OpenXR is also enabled for Windows, so the PC build runs on a Quest over Link / Air Link.
    public static class QuestSetup
    {
        // TODO: choose the final package name before any store upload; it can never change afterwards.
        const string PackageName = "com.kigalirush.storyvr";
        const string OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";
        const string MobileRp = "Assets/Settings/Mobile_RPAsset.asset";

        [MenuItem("Kigali Rush/VR/Apply Meta Quest settings")]
        public static void Apply()
        {
            Player();
            EnableOpenXR(BuildTargetGroup.Android);
            EnableOpenXR(BuildTargetGroup.Standalone);
            Rendering();
            AssetDatabase.SaveAssets();
            Debug.Log("[Quest] Settings applied: OpenXR (Meta Quest, Touch controllers), Android ARM64 IL2CPP, API 32+, Vulkan, " +
                      "ASTC, Linear, single-pass instanced, 4x MSAA, no HDR, 1 shadow cascade at 40 m.");
        }

        static void Player()
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.productName = "KR";
            PlayerSettings.SetApplicationIdentifier(android, PackageName);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.SetMobileMTRendering(android, true);
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(StoryModeBuilder.ScenePath, true) };
        }

        // XR Plug-in Management with the OpenXR loader, the Meta Quest feature and the Touch controller profile.
        static void EnableOpenXR(BuildTargetGroup group)
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            var settings = perTarget.SettingsForBuildTarget(group);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                settings.name = $"{group} Settings";
                perTarget.SetSettingsForBuildTarget(group, settings);
                AssetDatabase.AddObjectToAsset(settings, perTarget);
            }
            if (settings.Manager == null)
            {
                var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                manager.name = $"{group} Providers";
                settings.Manager = manager;
                AssetDatabase.AddObjectToAsset(manager, perTarget);
            }
            settings.InitManagerOnStart = true;
            if (!XRPackageMetadataStore.AssignLoader(settings.Manager, OpenXRLoader, group))
                Debug.LogError($"[Quest] Could not assign the OpenXR loader for {group}.");
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(perTarget);

            var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (openxr == null)
            {
                Debug.LogError($"[Quest] No OpenXR settings for {group}; open Project Settings > XR Plug-in Management > OpenXR once and run again.");
                return;
            }
            openxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            // A fresh settings asset has no features until they are discovered; without this nothing gets enabled.
            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(group);
            // Feature types live in optional assemblies, so match them by name.
            foreach (var feature in openxr.GetFeatures())
            {
                string n = feature.GetType().Name;
                if (n == "OculusTouchControllerProfile" || group == BuildTargetGroup.Android && n == "MetaQuestFeature")
                    feature.enabled = true;
            }
            EditorUtility.SetDirty(openxr);
            var on = openxr.GetFeatures().Where(f => f.enabled).Select(f => f.GetType().Name).ToList();
            Debug.Log($"[Quest] OpenXR {group}: {string.Join(", ", on)}");
            if (!on.Contains("OculusTouchControllerProfile") || group == BuildTargetGroup.Android && !on.Contains("MetaQuestFeature"))
                Debug.LogError($"[Quest] OpenXR {group}: Meta Quest support or the Touch controller profile is not enabled.");
        }

        // Quest renders every frame twice at 72-90 Hz: MSAA instead of post AA, no HDR, short single-cascade shadows.
        static void Rendering()
        {
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileRp);
            if (rp == null)
            {
                Debug.LogError($"[Quest] {MobileRp} not found.");
                return;
            }
            rp.msaaSampleCount = 4;
            rp.renderScale = 1f;
            rp.supportsHDR = false;
            rp.shadowDistance = 40f;
            rp.shadowCascadeCount = 1;
            rp.maxAdditionalLightsCount = 2;
            EditorUtility.SetDirty(rp);
        }

        public static void ApplyBatch() => Batch(Apply);

        public static void BuildApkBatch() => Batch(() =>
        {
            Apply();
            string apk = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Quest", "KigaliRushVR.apk"));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { StoryModeBuilder.ScenePath },
                locationPathName = apk,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            var sum = report.summary;
            Debug.Log($"[Quest] Build {sum.result}: {apk}, {sum.totalSize / (1024f * 1024f):0} MB, {sum.totalErrors} errors, {sum.totalTime.TotalMinutes:0.0} min");
            if (sum.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                Debug.LogError("[Quest] APK build failed.");
        });

        static void Batch(Action task)
        {
            int errors = 0;
            void Count(string m, string s, LogType t)
            {
                if (t == LogType.Error || t == LogType.Exception)
                    errors++;
            }
            Application.logMessageReceived += Count;
            try { task(); }
            catch (Exception e) { Debug.LogException(e); errors++; }
            Application.logMessageReceived -= Count;
            Debug.Log($"[Batch] Finished with {errors} error(s).");
            EditorApplication.Exit(errors == 0 ? 0 : 1);
        }
    }
}
