using System.Collections.Generic;

namespace AssetStudio
{
    /// <summary>
    /// AssetStudio 2: resolves the Material(s)/Texture2D(s) that belong to a standalone
    /// Mesh asset by scanning every loaded GameObject for a MeshFilter+MeshRenderer or a
    /// SkinnedMeshRenderer that points at the mesh, then reading that renderer's materials.
    /// This lets the Mesh preview and the "export mesh with textures" feature show/save the
    /// correct texture(s) even though a raw Mesh object has no direct PPtr to a Texture2D.
    /// </summary>
    public static class MeshTextureResolver
    {
        public static List<Material> FindMaterials(Mesh mesh, AssetsManager assetsManager)
        {
            var result = new List<Material>();
            if (mesh == null || assetsManager == null)
                return result;

            foreach (var assetsFile in assetsManager.assetsFileList)
            {
                foreach (var obj in assetsFile.Objects)
                {
                    if (!(obj is GameObject go))
                        continue;

                    Mesh goMesh = null;
                    Renderer renderer = null;

                    if (go.m_MeshFilter != null && go.m_MeshFilter.m_Mesh.TryGet(out var mf))
                    {
                        goMesh = mf;
                        renderer = go.m_MeshRenderer;
                    }
                    else if (go.m_SkinnedMeshRenderer != null && go.m_SkinnedMeshRenderer.m_Mesh.TryGet(out var smr))
                    {
                        goMesh = smr;
                        renderer = go.m_SkinnedMeshRenderer;
                    }

                    if (goMesh != mesh || renderer == null || renderer.m_Materials == null)
                        continue;

                    foreach (var matPPtr in renderer.m_Materials)
                    {
                        if (matPPtr.TryGet(out var mat) && !result.Contains(mat))
                        {
                            result.Add(mat);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// AssetStudio 2: returns the resolved Material for each submesh index, in submesh
        /// order, WITHOUT deduplication - unlike FindMaterials, a null entry means "no material
        /// for this submesh" and duplicate materials (e.g. two submeshes sharing one material)
        /// are preserved so that index i always corresponds to submesh i. FindMaterials'
        /// deduplication silently breaks this index alignment whenever any two submeshes share
        /// a material, which is exactly what caused the mesh preview to show the wrong texture
        /// region on later submeshes for multi-material meshes, even though export (which reads
        /// Renderer.m_Materials directly, in order) was unaffected.
        /// </summary>
        public static List<Material> FindOrderedMaterials(Mesh mesh, AssetsManager assetsManager)
        {
            if (mesh == null || assetsManager == null)
                return new List<Material>();

            foreach (var assetsFile in assetsManager.assetsFileList)
            {
                foreach (var obj in assetsFile.Objects)
                {
                    if (!(obj is GameObject go))
                        continue;

                    Mesh goMesh = null;
                    Renderer renderer = null;

                    if (go.m_MeshFilter != null && go.m_MeshFilter.m_Mesh.TryGet(out var mf))
                    {
                        goMesh = mf;
                        renderer = go.m_MeshRenderer;
                    }
                    else if (go.m_SkinnedMeshRenderer != null && go.m_SkinnedMeshRenderer.m_Mesh.TryGet(out var smr))
                    {
                        goMesh = smr;
                        renderer = go.m_SkinnedMeshRenderer;
                    }

                    if (goMesh != mesh || renderer == null || renderer.m_Materials == null)
                        continue;

                    var ordered = new List<Material>();
                    foreach (var matPPtr in renderer.m_Materials)
                    {
                        ordered.Add(matPPtr.TryGet(out var mat) ? mat : null);
                    }
                    return ordered;
                }
            }
            return new List<Material>();
        }

        /// <summary>
        /// Best-effort "main" texture for a mesh: prefers a material's _MainTex slot,
        /// falls back to the first texture found on any material used by the mesh.
        /// </summary>
        public static Texture2D FindMainTexture(Mesh mesh, AssetsManager assetsManager)
        {
            var materials = FindMaterials(mesh, assetsManager);

            foreach (var mat in materials)
            {
                var tex = FindMainTextureForMaterial(mat);
                if (tex != null)
                {
                    return tex;
                }
            }

            return null;
        }

        /// <summary>
        /// AssetStudio 2: best-effort main texture (_MainTex, else first texture) for a single
        /// material. Used per-submesh by the mesh preview so each submesh binds the texture
        /// from its own material rather than one texture being applied to the whole mesh.
        /// </summary>
        public static Texture2D FindMainTextureForMaterial(Material mat)
        {
            if (mat?.m_SavedProperties?.m_TexEnvs == null)
                return null;

            foreach (var texEnv in mat.m_SavedProperties.m_TexEnvs)
            {
                if (texEnv.Key == "_MainTex" && texEnv.Value.m_Texture.TryGet<Texture2D>(out var tex))
                {
                    return tex;
                }
            }
            foreach (var texEnv in mat.m_SavedProperties.m_TexEnvs)
            {
                if (texEnv.Value.m_Texture.TryGet<Texture2D>(out var tex))
                {
                    return tex;
                }
            }

            return null;
        }
    }
}
