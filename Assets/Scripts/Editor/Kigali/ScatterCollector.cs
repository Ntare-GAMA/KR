using System.Collections.Generic;
using System.IO;
using KR.Gameplay.World;
using UnityEditor;
using UnityEngine;

namespace KR.Editor.Kigali
{
    // Collects prefab placements as matrices, splits each prefab into (mesh, submesh, material) parts and writes
    // them for InstancedScatter: one binary TextAsset plus the part table. No GameObjects per instance.
    internal sealed class ScatterCollector
    {
        public enum Category { House, Large, Tree, Prop, Lamp }

        // Cull distance and shadow casting per category.
        static readonly (float cull, bool shadows)[] CategorySettings =
        {
            // House, Large, Tree, Prop, Lamp. Quest budgets: short cull distances, shadows only on buildings.
            // The camera clips at 950 m inside thick haze, so nothing needs to draw further than that.
            (600f, true), (900f, true), (350f, false), (150f, false), (220f, false),
        };

        sealed class PrefabParts
        {
            public Bounds Bounds;
            public readonly List<(Mesh mesh, int submesh, Material material, Matrix4x4 local)> Parts =
                new List<(Mesh, int, Material, Matrix4x4)>();
        }

        readonly Dictionary<GameObject, PrefabParts> _prefabs = new Dictionary<GameObject, PrefabParts>();
        readonly Dictionary<(Mesh, int, Material, Category), int> _partIndex = new Dictionary<(Mesh, int, Material, Category), int>();
        readonly List<InstancedScatter.Part> _parts = new List<InstancedScatter.Part>();
        readonly List<Dictionary<int, List<Matrix4x4>>> _data = new List<Dictionary<int, List<Matrix4x4>>>();
        readonly float _chunkSize;
        readonly int _chunksX, _chunksZ;
        readonly Bounds[] _chunkBounds;
        readonly bool[] _chunkUsed;

        public int Count { get; private set; }

        // Every placement, so City Life can give street props collision boxes.
        public readonly List<(GameObject prefab, Matrix4x4 matrix, Category category)> Placed =
            new List<(GameObject, Matrix4x4, Category)>();

        public ScatterCollector(float sizeX, float sizeZ, float chunkSize)
        {
            _chunkSize = chunkSize;
            _chunksX = Mathf.CeilToInt(sizeX / chunkSize);
            _chunksZ = Mathf.CeilToInt(sizeZ / chunkSize);
            _chunkBounds = new Bounds[_chunksX * _chunksZ];
            _chunkUsed = new bool[_chunkBounds.Length];
        }

        public Bounds PrefabBounds(GameObject prefab) => Get(prefab).Bounds;

        PrefabParts Get(GameObject prefab)
        {
            if (_prefabs.TryGetValue(prefab, out var cached))
                return cached;
            var result = new PrefabParts();
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            temp.transform.localScale = Vector3.one;

            var excluded = new HashSet<Renderer>();
            foreach (var group in temp.GetComponentsInChildren<LODGroup>())
            {
                var lods = group.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers)
                        if (r != null)
                            excluded.Add(r);
            }

            bool any = false;
            foreach (var mf in temp.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                var mesh = mf.sharedMesh;
                if (mr == null || !mr.enabled || mesh == null || excluded.Contains(mr))
                    continue;
                var mats = mr.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var mat = mats.Length == 0 ? null : mats[Mathf.Min(s, mats.Length - 1)];
                    if (mat != null)
                        result.Parts.Add((mesh, s, mat, mf.transform.localToWorldMatrix));
                }
                var b = mr.bounds;
                if (!any) { result.Bounds = b; any = true; }
                else result.Bounds.Encapsulate(b);
            }
            Object.DestroyImmediate(temp);
            _prefabs[prefab] = result;
            return result;
        }

        public int Add(GameObject prefab, Matrix4x4 matrix, Category category)
        {
            if (prefab == null)
                return -1;
            var pp = Get(prefab);
            if (pp.Parts.Count == 0)
                return -1;
            Vector3 pos = matrix.GetColumn(3);
            int cx = Mathf.Clamp((int)(pos.x / _chunkSize), 0, _chunksX - 1);
            int cz = Mathf.Clamp((int)(pos.z / _chunkSize), 0, _chunksZ - 1);
            int chunk = cz * _chunksX + cx;
            var settings = CategorySettings[(int)category];

            foreach (var (mesh, submesh, material, local) in pp.Parts)
            {
                var key = (mesh, submesh, material, category);
                if (!_partIndex.TryGetValue(key, out int index))
                {
                    index = _parts.Count;
                    _partIndex[key] = index;
                    _parts.Add(new InstancedScatter.Part
                    {
                        mesh = mesh, submesh = submesh, material = material,
                        castShadows = settings.shadows, cullDistance = settings.cull,
                    });
                    _data.Add(new Dictionary<int, List<Matrix4x4>>());
                }
                var world = matrix * local;
                if (!_data[index].TryGetValue(chunk, out var list))
                    _data[index][chunk] = list = new List<Matrix4x4>();
                list.Add(world);

                var wb = TransformBounds(mesh.bounds, world);
                if (_chunkUsed[chunk]) _chunkBounds[chunk].Encapsulate(wb);
                else { _chunkBounds[chunk] = wb; _chunkUsed[chunk] = true; }
            }
            Placed.Add((prefab, matrix, category));
            Count++;
            return Placed.Count - 1;
        }

        // Things that belong to a placed object and must move with it when it is edited by hand: a lamp's light
        // points, a traffic signal's head. They become its children when the city is written as objects.
        public readonly Dictionary<int, List<Transform>> Attached = new Dictionary<int, List<Transform>>();

        public void Attach(int placed, Transform t)
        {
            if (placed < 0 || t == null)
                return;
            if (!Attached.TryGetValue(placed, out var list))
                Attached[placed] = list = new List<Transform>();
            list.Add(t);
        }

        public static (float cull, bool shadows) Settings(Category c) => CategorySettings[(int)c];

        // Uniform scale so the longest horizontal side equals length; models built along X are turned to face +Z.
        public Matrix4x4 FitLength(GameObject prefab, Vector3 pos, float yaw, float length)
        {
            var b = Get(prefab).Bounds;
            bool alongX = b.size.x > b.size.z;
            float scale = length / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
            var rot = Quaternion.Euler(0f, yaw + (alongX ? -90f : 0f), 0f);
            Vector3 p = pos - rot * (new Vector3(b.center.x, 0f, b.center.z) * scale);
            p.y = pos.y - b.min.y * scale;
            return Matrix4x4.TRS(p, rot, Vector3.one * scale);
        }

        // Uniform scale to a height (capped by footprint), centred on pos, sitting on pos.y.
        public Matrix4x4 Fit(GameObject prefab, Vector3 pos, float yaw, float height, float maxFootprint)
        {
            var b = Get(prefab).Bounds;
            float scale = b.size.y > 0.01f ? height / b.size.y : 1f;
            float footprint = Mathf.Max(b.size.x, b.size.z) * scale;
            if (footprint > maxFootprint)
                scale *= maxFootprint / footprint;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 p = pos - rot * (new Vector3(b.center.x, 0f, b.center.z) * scale);
            p.y = pos.y - b.min.y * scale;
            return Matrix4x4.TRS(p, rot, Vector3.one * scale);
        }

        // Non-uniform scale to fill an oriented box w x h x d whose base sits `sink` metres below `ground`.
        public Matrix4x4 FitBox(GameObject prefab, Vector3 centre, float yaw, float w, float d, float h, float ground, float sink = 0.3f)
        {
            var b = Get(prefab).Bounds;
            var s = new Vector3(w / Mathf.Max(b.size.x, 0.01f), h / Mathf.Max(b.size.y, 0.01f), d / Mathf.Max(b.size.z, 0.01f));
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 c = Vector3.Scale(b.center, s);
            Vector3 p = new Vector3(centre.x, 0f, centre.z) - rot * new Vector3(c.x, 0f, c.z);
            p.y = ground - b.min.y * s.y - sink;
            return Matrix4x4.TRS(p, rot, s);
        }

        public InstancedScatter Write(GameObject host, string dataPath)
        {
            using (var w = new BinaryWriter(File.Create(Path.GetFullPath(dataPath))))
            {
                w.Write(_parts.Count);
                foreach (var chunks in _data)
                {
                    w.Write(chunks.Count);
                    foreach (var kv in chunks)
                    {
                        w.Write(kv.Key);
                        w.Write(kv.Value.Count);
                        foreach (var m in kv.Value)
                            for (int k = 0; k < 16; k++)
                                w.Write(m[k]);
                    }
                }
                for (int c = 0; c < _chunkBounds.Length; c++)
                {
                    var b = _chunkUsed[c] ? _chunkBounds[c] : new Bounds(Vector3.zero, Vector3.zero);
                    w.Write(b.min.x); w.Write(b.min.y); w.Write(b.min.z);
                    w.Write(b.max.x); w.Write(b.max.y); w.Write(b.max.z);
                }
            }
            AssetDatabase.ImportAsset(dataPath, ImportAssetOptions.ForceUpdate);
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(dataPath);

            // Instancing must be enabled on the source materials for the runtime clones to inherit it cleanly.
            foreach (var part in _parts)
                if (!part.material.enableInstancing)
                {
                    part.material.enableInstancing = true;
                    EditorUtility.SetDirty(part.material);
                }

            var scatter = host.AddComponent<InstancedScatter>();
            scatter.Configure(_parts.ToArray(), asset, _chunksX, _chunksZ);
            return scatter;
        }

        static Bounds TransformBounds(Bounds b, Matrix4x4 m)
        {
            Vector3 c = m.MultiplyPoint3x4(b.center);
            Vector3 e = b.extents;
            Vector3 ax = m.MultiplyVector(new Vector3(e.x, 0f, 0f));
            Vector3 ay = m.MultiplyVector(new Vector3(0f, e.y, 0f));
            Vector3 az = m.MultiplyVector(new Vector3(0f, 0f, e.z));
            var ext = new Vector3(
                Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
                Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
                Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
            return new Bounds(c, ext * 2f);
        }
    }
}
