using System;
using System.Collections.Generic;
using System.Linq;
using AssetStudio;
#if NET472
using Vector2 = OpenTK.Vector2;
using Vector3 = OpenTK.Vector3;
using Vector4 = OpenTK.Vector4;
using Quaternion = OpenTK.Quaternion;
using Matrix4 = OpenTK.Matrix4;
#else
using Vector2 = OpenTK.Mathematics.Vector2;
using Vector3 = OpenTK.Mathematics.Vector3;
using Vector4 = OpenTK.Mathematics.Vector4;
using Quaternion = OpenTK.Mathematics.Quaternion;
using Matrix4 = OpenTK.Mathematics.Matrix4;
#endif

namespace AssetStudioGUI
{
    // AssetStudio 2: combines every renderable mesh under a GameObject's transform hierarchy
    // (interior, wheels, doors, etc. that ship as separate meshes/GameObjects in the container)
    // into a single vertex/index buffer in the parent's local space, the same way Unity assembles
    // them at runtime. Used to feed the existing GL mesh preview pipeline with a "whole object"
    // view instead of a single disconnected part.
    public static class GameObjectMeshCombiner
    {
        public class CombinedPart
        {
            public Mesh Mesh;
            public Matrix4 LocalToRoot;
            // AssetStudio 2: identity of the GameObject that owns this part, so the GUI can list
            // parts individually (toggle visibility in preview) and so export can be filtered to
            // match - this is the same PathID ModelConverter sees while walking the hierarchy.
            public long GameObjectPathID;
            public string Name;
        }

        public class CombinedMesh
        {
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Vector4[] Colors;
            public Vector2[] UV0;
            public int[] Indices;
            public bool HasUV;
        }

        // Finds the GameObject that owns this Mesh (via MeshFilter or SkinnedMeshRenderer) by
        // scanning every loaded GameObject for a component pointing at it.
        public static GameObject FindOwningGameObject(Mesh m_Mesh, AssetsManager assetsManager)
        {
            foreach (var assetsFile in assetsManager.assetsFileList)
            {
                foreach (var obj in assetsFile.Objects)
                {
                    if (obj is GameObject go)
                    {
                        foreach (var componentPtr in EnumerateComponents(go))
                        {
                            if (!componentPtr.TryGet(out var comp)) continue;
                            switch (comp)
                            {
                                case MeshFilter mf when mf.m_Mesh.TryGet(out var m1) && m1 == m_Mesh:
                                    return go;
                                case SkinnedMeshRenderer smr when smr.m_Mesh.TryGet(out var m2) && m2 == m_Mesh:
                                    return go;
                            }
                        }
                    }
                }
            }
            return null;
        }

        // Walks up m_Father until there's no parent left, so we combine the *whole* object
        // (e.g. the car), not just the branch the selected mesh happens to live on.
        public static Transform FindRootTransform(GameObject go)
        {
            Transform t = GetTransform(go);
            if (t == null) return null;
            var current = t;
            var guard = 0;
            while (current.m_Father != null && current.m_Father.TryGet(out var father) && guard++ < 1000)
            {
                current = father;
            }
            return current;
        }

        public static Transform GetTransform(GameObject go)
        {
            foreach (var componentPtr in EnumerateComponents(go))
            {
                if (componentPtr.TryGet(out var comp) && comp is Transform t)
                    return t;
            }
            return null;
        }

        private static IEnumerable<PPtr<Component>> EnumerateComponents(GameObject go)
        {
            foreach (var c in go.m_Components)
            {
                yield return c;
            }
        }

        // Recursively collects every Mesh reachable from the root transform, together with the
        // matrix that takes that mesh's local-space vertices into root-local space.
        public static List<CombinedPart> CollectParts(Transform root)
        {
            var parts = new List<CombinedPart>();
            Walk(root, Matrix4.Identity, parts, new HashSet<long>());
            return parts;
        }

        private static void Walk(Transform t, Matrix4 parentToRoot, List<CombinedPart> parts, HashSet<long> visited)
        {
            if (t == null || !visited.Add(t.m_PathID)) return; // guard against cyclic/duplicate refs

            var local = LocalTRS(t);
            var localToRoot = local * parentToRoot;

            if (t.m_GameObject.TryGet(out var go))
            {
                foreach (var componentPtr in EnumerateComponents(go))
                {
                    if (!componentPtr.TryGet(out var comp)) continue;

                    Mesh mesh = null;
                    if (comp is MeshFilter mf && mf.m_Mesh.TryGet(out var m1)) mesh = m1;
                    else if (comp is SkinnedMeshRenderer smr && smr.m_Mesh.TryGet(out var m2)) mesh = m2;

                    if (mesh != null && mesh.m_VertexCount > 0)
                    {
                        parts.Add(new CombinedPart
                        {
                            Mesh = mesh,
                            LocalToRoot = localToRoot,
                            GameObjectPathID = go.m_PathID,
                            Name = go.m_Name
                        });
                    }
                }
            }

            if (t.m_Children != null)
            {
                foreach (var childPtr in t.m_Children)
                {
                    if (childPtr.TryGet(out var child))
                    {
                        Walk(child, localToRoot, parts, visited);
                    }
                }
            }
        }

        private static Matrix4 LocalTRS(Transform t)
        {
            var pos = new Vector3(t.m_LocalPosition.X, t.m_LocalPosition.Y, t.m_LocalPosition.Z);
            var rot = new Quaternion(t.m_LocalRotation.X, t.m_LocalRotation.Y, t.m_LocalRotation.Z, t.m_LocalRotation.W);
            var scale = new Vector3(t.m_LocalScale.X, t.m_LocalScale.Y, t.m_LocalScale.Z);

            return Matrix4.CreateScale(scale)
                   * Matrix4.CreateFromQuaternion(rot)
                   * Matrix4.CreateTranslation(pos);
        }

        // Builds one combined vertex/index buffer (root-local space) out of every part found.
        // Parts whose owning GameObject PathID is in excludedGameObjectPathIDs are skipped, so
        // the preview can hide individual pieces (wheels, interior, etc.) on demand.
        public static CombinedMesh Combine(List<CombinedPart> parts, HashSet<long> excludedGameObjectPathIDs = null)
        {
            if (excludedGameObjectPathIDs != null && excludedGameObjectPathIDs.Count > 0)
            {
                parts = parts.Where(p => !excludedGameObjectPathIDs.Contains(p.GameObjectPathID)).ToList();
            }

            var result = new CombinedMesh();
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Vector4>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();
            bool anyUV = false;

            foreach (var part in parts)
            {
                var m = part.Mesh;
                int vcount = m.m_VertexCount;
                if (vcount == 0 || m.m_Vertices == null || m.m_Vertices.Length == 0) continue;

                int stride = m.m_Vertices.Length == vcount * 4 ? 4 : 3;
                int normalStride = 3;
                bool hasNormals = m.m_Normals != null && m.m_Normals.Length > 0;
                if (hasNormals) normalStride = m.m_Normals.Length == vcount * 4 ? 4 : 3;

                int uvStride = MeshTextureResolver.GetUVStride(m.m_UV0, vcount);
                bool hasUV = uvStride > 0;
                anyUV |= hasUV;

                int baseIndex = vertices.Count;
                var normalMatrix = Matrix4.Transpose(Matrix4.Invert(part.LocalToRoot));

                for (int v = 0; v < vcount; v++)
                {
                    var local = new Vector4(
                        m.m_Vertices[v * stride],
                        m.m_Vertices[v * stride + 1],
                        m.m_Vertices[v * stride + 2],
                        1f);
                    var world = local * part.LocalToRoot;
                    vertices.Add(new Vector3(world.X, world.Y, world.Z));

                    if (hasNormals)
                    {
                        var n = new Vector4(
                            m.m_Normals[v * normalStride],
                            m.m_Normals[v * normalStride + 1],
                            m.m_Normals[v * normalStride + 2],
                            0f);
                        var wn = n * normalMatrix;
                        var wn3 = new Vector3(wn.X, wn.Y, wn.Z);
                        if (wn3.LengthSquared > 1e-12f) wn3.Normalize();
                        normals.Add(wn3);
                    }
                    else
                    {
                        normals.Add(new Vector3(0, 1, 0));
                    }

                    if (hasUV)
                        uvs.Add(new Vector2(m.m_UV0[v * uvStride], m.m_UV0[v * uvStride + 1]));
                    else
                        uvs.Add(new Vector2(0, 0));

                    colors.Add(new Vector4(0.6f, 0.6f, 0.6f, 1f));
                }

                if (m.m_Indices != null)
                {
                    for (int i = 0; i + 2 < m.m_Indices.Count; i += 3)
                    {
                        indices.Add(baseIndex + (int)m.m_Indices[i]);
                        indices.Add(baseIndex + (int)m.m_Indices[i + 1]);
                        indices.Add(baseIndex + (int)m.m_Indices[i + 2]);
                    }
                }
            }

            result.Vertices = vertices.ToArray();
            result.Normals = normals.ToArray();
            result.Colors = colors.ToArray();
            result.UV0 = uvs.ToArray();
            result.Indices = indices.ToArray();
            result.HasUV = anyUV;
            return result;
        }
    }
}