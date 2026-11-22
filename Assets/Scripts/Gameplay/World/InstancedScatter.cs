using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace KR.Gameplay.World
{
    // Draws large numbers of static meshes (buildings, trees, street furniture) with GPU instancing.
    // Instances are grouped into a chunk grid; each camera culls chunks by frustum and per-part distance.
    // Matrices live in a compact binary TextAsset; materials are cloned at load so day/night changes never
    // touch project assets.
    [ExecuteAlways]
    public sealed class InstancedScatter : MonoBehaviour
    {
        [Serializable]
        public sealed class Part
        {
            public Mesh mesh;
            public int submesh;
            public Material material;
            public bool castShadows;
            public float cullDistance;
        }

        const int MaxBatch = 1023;

        [SerializeField] Part[] parts;
        [SerializeField] TextAsset data;
        [SerializeField] int chunksX;
        [SerializeField] int chunksZ;
        [SerializeField] float shadowDistance = 300f;

        Matrix4x4[][][] _matrices;   // [part][chunk] -> matrices, null when empty
        Bounds[] _chunkBounds;
        bool[] _visible;
        float[] _distance;
        Material[] _runtimeMaterials;
        readonly Plane[] _planes = new Plane[6];

        public Material[] RuntimeMaterials => _runtimeMaterials;

        public void Configure(Part[] partList, TextAsset instanceData, int countX, int countZ)
        {
            parts = partList;
            data = instanceData;
            chunksX = countX;
            chunksZ = countZ;
        }

        void OnEnable()
        {
            Load();
            RenderPipelineManager.beginCameraRendering += Draw;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Draw;
            Release();
        }

        void Load()
        {
            if (parts == null || data == null)
                return;
            int chunkCount = chunksX * chunksZ;
            _matrices = new Matrix4x4[parts.Length][][];
            _chunkBounds = new Bounds[chunkCount];
            _visible = new bool[chunkCount];
            _distance = new float[chunkCount];

            using (var reader = new BinaryReader(new MemoryStream(data.bytes)))
            {
                int partCount = reader.ReadInt32();
                for (int p = 0; p < partCount; p++)
                {
                    var perChunk = new Matrix4x4[chunkCount][];
                    int used = reader.ReadInt32();
                    for (int u = 0; u < used; u++)
                    {
                        int chunk = reader.ReadInt32();
                        var list = new Matrix4x4[reader.ReadInt32()];
                        for (int i = 0; i < list.Length; i++)
                        {
                            var m = new Matrix4x4();
                            for (int k = 0; k < 16; k++)
                                m[k] = reader.ReadSingle();
                            list[i] = m;
                        }
                        perChunk[chunk] = list;
                    }
                    if (p < parts.Length)
                        _matrices[p] = perChunk;
                }
                for (int c = 0; c < chunkCount; c++)
                {
                    var min = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    var max = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    _chunkBounds[c].SetMinMax(min, max);
                }
            }

            var clones = new Dictionary<Material, Material>();
            _runtimeMaterials = new Material[parts.Length];
            for (int p = 0; p < parts.Length; p++)
            {
                var source = parts[p].material;
                if (source == null)
                    continue;
                if (!clones.TryGetValue(source, out var clone))
                {
                    clone = new Material(source) { name = source.name, hideFlags = HideFlags.DontSave, enableInstancing = true };
                    clones[source] = clone;
                }
                _runtimeMaterials[p] = clone;
            }
        }

        void Release()
        {
            if (_runtimeMaterials == null)
                return;
            var done = new HashSet<Material>();
            foreach (var m in _runtimeMaterials)
                if (m != null && done.Add(m))
                {
                    if (Application.isPlaying) Destroy(m);
                    else DestroyImmediate(m);
                }
            _runtimeMaterials = null;
            _matrices = null;
        }

        static readonly Unity.Profiling.ProfilerMarker ProfileMarker = new Unity.Profiling.ProfilerMarker("KR.InstancedCity.Draw");

        void Draw(ScriptableRenderContext context, Camera cam)
        {
            using (ProfileMarker.Auto())
                DrawCity(cam);
        }

        void DrawCity(Camera cam)
        {
            if (_matrices == null || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection)
                return;
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            Vector3 eye = cam.transform.position;
            for (int c = 0; c < _chunkBounds.Length; c++)
            {
                _visible[c] = GeometryUtility.TestPlanesAABB(_planes, _chunkBounds[c]);
                _distance[c] = Mathf.Sqrt(_chunkBounds[c].SqrDistance(eye));
            }

            for (int p = 0; p < parts.Length; p++)
            {
                var part = parts[p];
                var material = _runtimeMaterials[p];
                var chunks = _matrices[p];
                if (part.mesh == null || material == null || chunks == null)
                    continue;
                for (int c = 0; c < chunks.Length; c++)
                {
                    var list = chunks[c];
                    if (list == null || !_visible[c] || _distance[c] > part.cullDistance)
                        continue;
                    var rp = new RenderParams(material)
                    {
                        camera = cam,
                        layer = gameObject.layer,
                        receiveShadows = true,
                        worldBounds = _chunkBounds[c],
                        shadowCastingMode = part.castShadows && _distance[c] < shadowDistance
                            ? ShadowCastingMode.On
                            : ShadowCastingMode.Off,
                    };
                    for (int start = 0; start < list.Length; start += MaxBatch)
                        Graphics.RenderMeshInstanced(rp, part.mesh, part.submesh, list, Mathf.Min(MaxBatch, list.Length - start), start);
                }
            }
        }
    }
}
