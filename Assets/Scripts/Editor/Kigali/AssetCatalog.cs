using System.Linq;
using KR.Editor.Util;
using UnityEditor;
using UnityEngine;

namespace KR.Editor.Kigali
{
    // Third-party prefabs the city builders draw from (palms, street lights, buildings, bus stop, road props),
    // and the vehicle-fitting helper. Kept from the former street and blockout builders.
    internal static class AssetCatalog
    {
        const string PalmFolder = "Assets/Next_Spring/Tree_Bundle/Prefabs/Super_Palm";
        const string LightFolder = "Assets/Sat Productions/G-01/Street Lights Pack 01/Prefabs";
        const string BuildingFolder = "Assets/Polyeler/SimpleBuildingPack/Prefabs/Building";
        const string UrbanBuildingPrefab = "Assets/UrbanBuilding/Prefabs/urban_building_Prefab.prefab";
        const string IthappyPrefabs = "Assets/ithappy/Cartoon_City_Free/Prefabs";

        // Packs that ship Built-in pipeline materials; converted to URP before use.
        internal static readonly string[] BuiltInFolders =
            { "Assets/Polyeler", "Assets/FastMesh", "Assets/UrbanBuilding", "Assets/Sat Productions" };

        internal sealed class Catalog
        {
            public GameObject[] Palms, Buildings, Bushes;
            public GameObject StreetLight, UrbanBuilding, BusStop;
        }

        internal static Catalog Load() => new Catalog
        {
            Palms = LoadAll(PalmFolder),
            Buildings = LoadAll(BuildingFolder),
            Bushes = new[] { "Bush_06", "Bush_07", "Bush_10" }.Select(n => Find(IthappyPrefabs, n)).Where(p => p).ToArray(),
            UrbanBuilding = AssetDatabase.LoadAssetAtPath<GameObject>(UrbanBuildingPrefab),
            BusStop = Find(IthappyPrefabs, "Bus_Stop_02"),
            StreetLight = WidestStreetLight(LoadAll(LightFolder)),
        };

        static GameObject[] LoadAll(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning($"[AssetCatalog] Missing folder {folder}");
                return new GameObject[0];
            }
            return AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                                .Where(p => p != null).OrderBy(p => p.name).ToArray();
        }

        static GameObject Find(string folder, string name) => LoadAll(folder).FirstOrDefault(p => p.name == name);

        // Kigali medians use double-arm poles; pick the light whose spread is widest relative to its height.
        static GameObject WidestStreetLight(GameObject[] lights)
        {
            GameObject best = null;
            float bestRatio = 0f;
            foreach (var prefab in lights)
            {
                var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var b = EditorAssetUtil.RendererBounds(temp);
                Object.DestroyImmediate(temp);
                float ratio = Mathf.Max(b.size.x, b.size.z) / Mathf.Max(b.size.y, 0.01f);
                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    best = prefab;
                }
            }
            return best;
        }

        // Scales a vehicle so its long axis equals length, turns it to face +Z if modelled along X,
        // and sits it on y = 0 centred under its parent. Assumes the model's front is +Z or +X.
        public static void FitVehicle(GameObject model, float length)
        {
            var t = model.transform;
            t.localPosition = Vector3.zero;
            t.localScale = Vector3.one;
            var b = EditorAssetUtil.RendererBounds(model);
            if (b.size.x > b.size.z)
            {
                t.localRotation = Quaternion.Euler(0f, -90f, 0f) * t.localRotation;
                b = EditorAssetUtil.RendererBounds(model);
            }
            float longest = Mathf.Max(b.size.z, 0.01f);
            t.localScale *= length / longest;
            b = EditorAssetUtil.RendererBounds(model);
            Vector3 parentPos = t.parent != null ? t.parent.position : Vector3.zero;
            t.position += new Vector3(parentPos.x - b.center.x, parentPos.y - b.min.y, parentPos.z - b.center.z);
        }
    }
}
