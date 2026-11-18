using System.Collections.Generic;
using System.IO;
using KR.Editor.Util;
using UnityEditor;
using UnityEngine;

namespace KR.Editor.Characters
{
    // Copies the curated Mixamo clips (MixamoCatalog) from SourceArt into the project and imports them as
    // Humanoid. The full download (2,187 clips, 1.1 GB) stays outside Assets so Unity never imports it.
    internal static class MixamoImport
    {
        [MenuItem("Kigali Rush/Characters/Import Mixamo Animations (curated set)")]
        public static void Import()
        {
            string src = MixamoCatalog.SourceFolder;
            if (!Directory.Exists(src))
            {
                Debug.LogError($"[Mixamo] Source folder not found: {src}");
                return;
            }
            EditorAssetUtil.EnsureFolder(MixamoCatalog.Folder);
            string dest = Path.GetFullPath(Path.Combine(Application.dataPath, "..", MixamoCatalog.Folder));
            int copied = 0, present = 0;
            var missing = new List<string>();
            foreach (var name in MixamoCatalog.AllClips())
            {
                string from = Path.Combine(src, name + ".fbx"), to = Path.Combine(dest, name + ".fbx");
                if (File.Exists(to)) { present++; continue; }
                if (!File.Exists(from)) { missing.Add(name); continue; }
                File.Copy(from, to);
                copied++;
            }
            AssetDatabase.Refresh();
            MixamoCatalog.ClearCache();
            Debug.Log($"[Mixamo] {copied} clips imported, {present} already in the project, into {MixamoCatalog.Folder}.");
            if (missing.Count > 0)
                Debug.LogWarning($"[Mixamo] Not in the download (skipped): {string.Join(", ", missing)}");
        }
    }

    // Import settings for everything in the curated folder: Humanoid, no materials, loop and root settings from
    // the catalogue. Mixamo names every take "mixamo.com", so clips are renamed after their file.
    internal sealed class MixamoPostprocessor : AssetPostprocessor
    {
        bool Ours => assetPath.StartsWith(MixamoCatalog.Folder + "/") && assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);

        void OnPreprocessModel()
        {
            if (!Ours)
                return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.animationCompression = ModelImporterAnimationCompression.Optimal;
        }

        void OnPreprocessAnimation()
        {
            if (!Ours)
                return;
            var mi = (ModelImporter)assetImporter;
            var defs = mi.defaultClipAnimations;
            if (defs == null || defs.Length == 0)
                return;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            bool loop = MixamoCatalog.IsLooped(name), locomotion = MixamoCatalog.IsLocomotion(name);
            foreach (var d in defs)
            {
                d.name = name;
                d.loopTime = loop;
                d.loopPose = loop;
                d.lockRootRotation = true;
                d.keepOriginalOrientation = true;
                d.lockRootHeightY = true;
                d.keepOriginalPositionY = true;
                d.lockRootPositionXZ = !locomotion;
                d.keepOriginalPositionXZ = !locomotion;
            }
            mi.clipAnimations = defs;
        }
    }
}
