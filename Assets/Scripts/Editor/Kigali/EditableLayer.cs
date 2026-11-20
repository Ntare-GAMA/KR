using System;
using KR.Editor.Util;
using UnityEditor;
using UnityEngine;

namespace KR.Editor.Kigali
{
    // Hand-editable parts of the generated city: road markings, signboards, street names, house numbers.
    // In the scene every piece is an ordinary, independent object (no prefab link), so one click selects one zebra
    // bar, one stop line or one sign. Edit them right there in the scene.
    // Your edits survive rebuilds: before a build replaces the scene, Keep() saves each layer as it is in the scene
    // to Assets/World/Editable/<layer>.prefab, and the build places that saved copy instead of generating it again.
    // To start a layer again from the map data: delete its prefab (or Kigali Rush > Editable > Regenerate all) and
    // build again.
    internal static class EditableLayer
    {
        public const string Folder = "Assets/World/Editable";

        // The layer's prefab placed under parent; generated (generate gets the path for its mesh assets) only if
        // there is no prefab yet.
        public static GameObject Use(string name, Transform parent, Func<string, GameObject> generate)
        {
            EditorAssetUtil.EnsureFolder(Folder);
            string path = $"{Folder}/{name}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                var made = generate($"{Folder}/{name} meshes.asset");
                made.name = name;
                prefab = PrefabUtility.SaveAsPrefabAsset(made, path);
                UnityEngine.Object.DestroyImmediate(made);
                Debug.Log($"[Editable] Generated {path}. Edit it by hand; builds keep your changes.");
            }
            else
                Debug.Log($"[Editable] Using {path} (hand edits kept). Delete it to generate it again.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
            instance.transform.SetParent(parent, false);
            // Independent objects: clicking a bar selects that bar, not the whole layer.
            // Only the layer itself is unpacked: a tree or lamp inside keeps its link to its model, so one click
            // selects that whole tree or lamp.
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            return instance;
        }

        // Saves every editable layer found in the open scene (with your hand edits) back to its prefab. Builds call
        // this before they replace the scene or the objects holding the layers.
        public static void Keep()
        {
            EditorAssetUtil.EnsureFolder(Folder);
            int kept = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t.name != name || PrefabUtility.IsPartOfPrefabAsset(t))
                        continue;
                    var go = t.gameObject;
                    if (PrefabUtility.IsPartOfPrefabInstance(go))
                        PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(go), PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                    var parent = go.transform.parent;
                    go.transform.SetParent(null, true);
                    PrefabUtility.SaveAsPrefabAsset(go, path);
                    go.transform.SetParent(parent, true);
                    kept++;
                    break;
                }
            }
            if (kept > 0)
                Debug.Log($"[Editable] Kept {kept} layer(s) from the scene, with your edits.");
        }

        [MenuItem("Kigali Rush/Editable/Show the editable layers")]
        static void Show()
        {
            EditorAssetUtil.EnsureFolder(Folder);
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(Folder);
        }

        // For a scene built before the layers were unpacked: splits them into independent objects in place.
        [MenuItem("Kigali Rush/Editable/Make every piece selectable on its own (this scene)")]
        static void UnpackInScene()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Folder }))
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (t.name == name && PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject))
                    {
                        PrefabUtility.UnpackPrefabInstance(t.gameObject, PrefabUnpackMode.OutermostRoot, InteractionMode.UserAction);
                        n++;
                    }
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log($"[Editable] {n} layer(s) split into independent objects. Save the scene (Ctrl+S).");
        }

        [MenuItem("Kigali Rush/Editable/Regenerate all (discards hand edits)")]
        static void RegenerateAll()
        {
            if (!EditorUtility.DisplayDialog("Regenerate editable layers",
                    "Delete every editable layer prefab? Your hand edits to road markings, signs, street names and house numbers " +
                    "are lost; the next build generates them again from the data.", "Delete", "Cancel"))
                return;
            foreach (var guid in AssetDatabase.FindAssets("", new[] { Folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!AssetDatabase.IsValidFolder(p))
                    AssetDatabase.DeleteAsset(p);
            }
            Debug.Log("[Editable] All layers removed; build again to regenerate them.");
        }

        // Puts a world-space mesh's pivot at its centre (so the object moves and rotates about itself in the editor).
        public static Vector3 Recentre(Mesh mesh)
        {
            var v = mesh.vertices;
            var c = mesh.bounds.center;
            for (int i = 0; i < v.Length; i++)
                v[i] -= c;
            mesh.vertices = v;
            mesh.RecalculateBounds();
            return c;
        }
    }
}
