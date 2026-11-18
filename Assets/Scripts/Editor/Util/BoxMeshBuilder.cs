using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KR.Editor.Util
{
    // Accumulates axis-aligned boxes into one mesh with a submesh per material.
    // UVs are planar in metres so tiling textures can use a material scale of 1 / tile size.
    internal sealed class BoxMeshBuilder
    {
        static readonly Vector3[] FaceNormals =
            { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };

        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<Vector2> _uvs = new List<Vector2>();
        readonly List<int>[] _triangles;

        public BoxMeshBuilder(int submeshes)
        {
            _triangles = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++)
                _triangles[i] = new List<int>();
        }

        public void Box(int submesh, Vector3 centre, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            foreach (var n in FaceNormals)
            {
                // u, v span the face; with v = n x u the vertex order below is counter-clockwise from
                // outside, so the indices are reversed to give Unity's clockwise front faces.
                Vector3 u = n.y != 0f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                Vector3 c = centre + Vector3.Scale(n, h);
                Vector3 du = Vector3.Scale(u, h), dv = Vector3.Scale(v, h);
                int s = _vertices.Count;
                AddVertex(c - du - dv, n);
                AddVertex(c - du + dv, n);
                AddVertex(c + du + dv, n);
                AddVertex(c + du - dv, n);
                var t = _triangles[submesh];
                t.Add(s); t.Add(s + 2); t.Add(s + 1);
                t.Add(s); t.Add(s + 3); t.Add(s + 2);
            }
        }

        void AddVertex(Vector3 p, Vector3 n)
        {
            _vertices.Add(p);
            _normals.Add(n);
            _uvs.Add(n.y != 0f ? new Vector2(p.x, p.z) : n.x != 0f ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y));
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.subMeshCount = _triangles.Length;
            for (int i = 0; i < _triangles.Length; i++)
                mesh.SetTriangles(_triangles[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
