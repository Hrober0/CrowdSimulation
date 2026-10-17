using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Examples.Rts
{
    /// <summary>
    /// Collects gizmo shapes over a whole <c>OnDrawGizmos</c> and draws them in a handful of calls.
    ///
    /// Every <c>Gizmos.DrawLine</c>, <c>DrawSphere</c> and <c>DrawCube</c> is its own draw call with its own
    /// material switch. The overlays draw several shapes per agent and per cell, so at 800 agents they issued
    /// over a hundred thousand draws a frame and the Editor spent 135 ms rendering debug lines around a
    /// simulation that took one. Batched, it is one line list and one mesh per colour.
    ///
    /// Colours are batch keys, so a caller drawing a continuous range - a speed, an age, a cost - rounds it
    /// into a few steps first (<see cref="Step"/>). That is invisible at the sizes these are drawn at.
    ///
    /// Owned by the overlay that uses it, not shared: the meshes it keeps are that overlay's.
    /// </summary>
    public sealed class GizmoBatch : IDisposable
    {
        private const int CIRCLE_SEGMENTS = 16;
        private const int DISC_SEGMENTS = 8;

        private readonly Dictionary<Color32, LineList> _lines = new();
        private readonly Dictionary<Color32, MeshList> _fills = new();
        private readonly List<Mesh> _meshes = new();

        /// <summary>Rounds a 0-1 value down onto one of <paramref name="steps"/> levels, so it can key a batch.</summary>
        public static float Step(float value, int steps) =>
            Mathf.Round(Mathf.Clamp01(value) * (steps - 1)) / (steps - 1);

        public void Line(Color colour, Vector3 from, Vector3 to)
        {
            LineList list = LinesOf(colour);
            list.Add(from);
            list.Add(to);
        }

        public void WireRect(Color colour, Vector3 centre, Vector2 size)
        {
            Vector3 half = new(size.x * 0.5f, size.y * 0.5f, 0f);
            Vector3 a = centre + new Vector3(-half.x, -half.y, 0f);
            Vector3 b = centre + new Vector3(half.x, -half.y, 0f);
            Vector3 c = centre + new Vector3(half.x, half.y, 0f);
            Vector3 d = centre + new Vector3(-half.x, half.y, 0f);

            Line(colour, a, b);
            Line(colour, b, c);
            Line(colour, c, d);
            Line(colour, d, a);
        }

        public void WireCircle(Color colour, Vector3 centre, float radius)
        {
            Vector3 previous = centre + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= CIRCLE_SEGMENTS; i++)
            {
                float angle = i / (float)CIRCLE_SEGMENTS * Mathf.PI * 2f;
                Vector3 next = centre + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                Line(colour, previous, next);
                previous = next;
            }
        }

        public void FillRect(Color colour, Vector3 centre, Vector2 size)
        {
            MeshList mesh = FillsOf(colour);
            int first = mesh.Vertices.Count;
            Vector3 half = new(size.x * 0.5f, size.y * 0.5f, 0f);

            mesh.Vertices.Add(centre + new Vector3(-half.x, -half.y, 0f));
            mesh.Vertices.Add(centre + new Vector3(half.x, -half.y, 0f));
            mesh.Vertices.Add(centre + new Vector3(half.x, half.y, 0f));
            mesh.Vertices.Add(centre + new Vector3(-half.x, half.y, 0f));

            mesh.Triangles.Add(first);
            mesh.Triangles.Add(first + 2);
            mesh.Triangles.Add(first + 1);
            mesh.Triangles.Add(first);
            mesh.Triangles.Add(first + 3);
            mesh.Triangles.Add(first + 2);
        }

        public void FillDisc(Color colour, Vector3 centre, float radius)
        {
            MeshList mesh = FillsOf(colour);
            int hub = mesh.Vertices.Count;
            mesh.Vertices.Add(centre);

            for (int i = 0; i < DISC_SEGMENTS; i++)
            {
                float angle = i / (float)DISC_SEGMENTS * Mathf.PI * 2f;
                mesh.Vertices.Add(centre + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }

            for (int i = 0; i < DISC_SEGMENTS; i++)
            {
                mesh.Triangles.Add(hub);
                mesh.Triangles.Add(hub + 1 + (i + 1) % DISC_SEGMENTS);
                mesh.Triangles.Add(hub + 1 + i);
            }
        }

        /// <summary>Draws everything collected since the last flush, then empties the lists (keeping their room).</summary>
        public void Flush()
        {
            Color previous = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.identity;

            foreach ((Color32 colour, LineList list) in _lines)
            {
                if (list.Count == 0)
                {
                    continue;
                }

                Gizmos.color = colour;
                Gizmos.DrawLineList(new ReadOnlySpan<Vector3>(list.Points, 0, list.Count));
                list.Count = 0;
            }

            int meshIndex = 0;
            foreach ((Color32 colour, MeshList list) in _fills)
            {
                if (list.Vertices.Count == 0)
                {
                    continue;
                }

                Mesh mesh = MeshAt(meshIndex++);
                mesh.Clear();
                mesh.indexFormat = list.Vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(list.Vertices);
                mesh.SetTriangles(list.Triangles, 0);

                // Facing the camera, which looks down +Z at the XY plane (SimToWorld), so the gizmo shader
                // lights it the way it lit the front face of the cubes and spheres this replaces.
                list.Normals.Clear();
                for (int i = 0; i < list.Vertices.Count; i++)
                {
                    list.Normals.Add(Vector3.back);
                }

                mesh.SetNormals(list.Normals);

                Gizmos.color = colour;
                Gizmos.DrawMesh(mesh);

                list.Vertices.Clear();
                list.Triangles.Clear();
            }

            Gizmos.color = previous;
            Gizmos.matrix = previousMatrix;
        }

        public void Dispose()
        {
            foreach (Mesh mesh in _meshes)
            {
                if (mesh == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(mesh);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }

            _meshes.Clear();
            _lines.Clear();
            _fills.Clear();
        }

        private LineList LinesOf(Color colour)
        {
            Color32 key = colour;
            if (!_lines.TryGetValue(key, out LineList list))
            {
                list = new LineList();
                _lines[key] = list;
            }

            return list;
        }

        private MeshList FillsOf(Color colour)
        {
            Color32 key = colour;
            if (!_fills.TryGetValue(key, out MeshList list))
            {
                list = new MeshList();
                _fills[key] = list;
            }

            return list;
        }

        private Mesh MeshAt(int index)
        {
            while (_meshes.Count <= index)
            {
                _meshes.Add(new Mesh { name = "GizmoBatch", hideFlags = HideFlags.HideAndDontSave });
            }

            return _meshes[index];
        }

        /// <summary>Line endpoints in pairs, in an array so it can be handed to the draw as a span.</summary>
        private sealed class LineList
        {
            public Vector3[] Points = new Vector3[256];
            public int Count;

            public void Add(Vector3 point)
            {
                if (Count == Points.Length)
                {
                    Array.Resize(ref Points, Points.Length * 2);
                }

                Points[Count++] = point;
            }
        }

        private sealed class MeshList
        {
            public readonly List<Vector3> Vertices = new();
            public readonly List<int> Triangles = new();
            public readonly List<Vector3> Normals = new();
        }
    }
}
