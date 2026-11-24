using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KR.Editor.Perf
{
    // A text report of what the story scene costs to draw, for the GDD's performance section and to choose where
    // LODs and occlusion culling pay off: per top-level group the renderers, triangles, LOD groups, static flags
    // and instancing; then texture import settings (mipmaps, size, compression) across the project.
    // Menu: Kigali Rush > Performance > Scene report. Output: Logs/perf_report.txt.
    public static class PerfReport
    {
        public const string StoryScene = "Assets/Scenes/PC Only - Story Kimironko Kibagabaga.unity";

        [MenuItem("Kigali Rush/Performance/Scene report")]
        public static void Run()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != StoryScene)
                scene = EditorSceneManager.OpenScene(StoryScene, OpenSceneMode.Single);
            var sb = new StringBuilder();
            sb.AppendLine($"Scene {scene.path}");
            sb.AppendLine("group | renderers | triangles | in LOD group | batching-static | occluder/occludee static | instanced mats");
            long allTris = 0;
            foreach (var root in scene.GetRootGameObjects().OrderBy(r => r.name))
                foreach (var group in Groups(root))
                {
                    var rs = group.GetComponentsInChildren<MeshRenderer>(true);
                    if (rs.Length == 0)
                        continue;
                    long tris = 0;
                    int inLod = 0, batching = 0, occl = 0, inst = 0, mats = 0;
                    var lodRenderers = new HashSet<Renderer>(group.GetComponentsInChildren<LODGroup>(true)
                        .SelectMany(l => l.GetLODs()).SelectMany(l => l.renderers).Where(r => r != null));
                    foreach (var r in rs)
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null)
                            tris += Tris(mf.sharedMesh);
                        if (lodRenderers.Contains(r)) inLod++;
                        var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                        if ((flags & StaticEditorFlags.BatchingStatic) != 0) batching++;
                        if ((flags & (StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic)) != 0) occl++;
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null) continue;
                            mats++;
                            if (m.enableInstancing) inst++;
                        }
                    }
                    allTris += tris;
                    sb.AppendLine($"{Path(group.transform)} | {rs.Length} | {tris:N0} | {inLod} | {batching} | {occl} | {inst}/{mats}");
                }
            sb.AppendLine($"TOTAL triangles in scene meshes: {allTris:N0}");

            sb.AppendLine().AppendLine("Textures: path | size | max | mipmaps | compression | type");
            int noMip = 0, total = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti))
                    continue;
                total++;
                if (!ti.mipmapEnabled)
                {
                    noMip++;
                    var android = ti.GetPlatformTextureSettings("Android");
                    sb.AppendLine($"NO-MIP {path} | {ti.maxTextureSize} | android {(android.overridden ? android.maxTextureSize + " " + android.format : "default")} | {ti.textureType}");
                }
            }
            sb.AppendLine($"{total} textures, {noMip} without mipmaps");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/perf_report.txt", sb.ToString());
            Debug.Log($"[PerfReport] {allTris:N0} triangles; {noMip}/{total} textures without mipmaps. Logs/perf_report.txt");
        }

        // The root itself, plus one level down for the big containers (City, World...), so the report has detail.
        static IEnumerable<GameObject> Groups(GameObject root)
        {
            if (root.transform.childCount > 0 && root.GetComponentsInChildren<MeshRenderer>(true).Length > 200)
                foreach (Transform c in root.transform)
                    yield return c.gameObject;
            else
                yield return root;
        }

        static long Tris(Mesh m)
        {
            long t = 0;
            for (int i = 0; i < m.subMeshCount; i++)
                t += m.GetIndexCount(i) / 3;
            return t;
        }

        static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    }
}
