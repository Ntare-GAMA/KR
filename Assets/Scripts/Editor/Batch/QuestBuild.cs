using System.IO;
using KR.Editor.Kigali;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KR.Editor.Batch
{
    // Story Mode on Meta Quest (Android, OpenXR): APK at <project>/Builds/Quest/KigaliRushStory.apk.
    // "Build and run" installs and launches it on the headset connected over USB (developer mode on).
    public static class QuestBuild
    {
        public static string ApkPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Quest", "KigaliRushStory.apk"));

        [MenuItem("Kigali Rush/Build/Build and run on Quest")]
        static void BuildAndRunMenu() => Build(true);

        [MenuItem("Kigali Rush/Build/Build Quest APK only")]
        static void BuildMenu() => Build(false);

        // A development build the Unity Profiler can attach to on the headset (Window > Analysis > Profiler, target
        // the Quest over USB). Slower than a release build: use it for profiling, not for play tests.
        [MenuItem("Kigali Rush/Build/Build and run on Quest (Profiler)")]
        static void ProfilerMenu()
        {
            _menuDevelopment = true;
            try { Build(true); }
            finally { _menuDevelopment = false; }
        }

        static bool _menuDevelopment;
        static bool Development => _menuDevelopment || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-development") >= 0;

        public static bool Build(bool run)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[Quest] Could not switch to Android. Is Android Build Support installed for this Editor?");
                return false;
            }
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
            QuestBoot.Prepare();
            AssetDatabase.SaveAssets();
            // A MonoBehaviour whose class is not in a file of the same name gets saved with its script embedded in
            // the scene (a "MonoScript" object). The editor copes; the headset reports the scene as corrupted and
            // crashes while loading it. Stop the build instead.
            foreach (var scene in QuestBoot.Scenes)
                if (File.ReadAllText(scene).Contains("\n--- !u!115 "))
                {
                    Debug.LogError($"[Quest] {scene} has a script embedded in it (a MonoBehaviour class not in a file of its own name). " +
                                   "Move the class into its own .cs file and rebuild the scene.");
                    return false;
                }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = QuestBoot.Scenes,
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = (run ? BuildOptions.AutoRunPlayer : BuildOptions.None) |
                          (Development ? BuildOptions.Development | BuildOptions.ConnectWithProfiler : BuildOptions.None),
            });
            var sum = report.summary;
            Debug.Log($"[Quest] {sum.result}: {ApkPath}, {sum.totalSize / (1024f * 1024f):0} MB, {sum.totalErrors} errors, {sum.totalTime.TotalMinutes:0.0} min{(Development ? ", development (Profiler)" : "")}");
            if (sum.result != BuildResult.Succeeded)
            {
                Debug.LogError("[Quest] Build failed; see the Console above.");
                return false;
            }
            return true;
        }
    }
}
