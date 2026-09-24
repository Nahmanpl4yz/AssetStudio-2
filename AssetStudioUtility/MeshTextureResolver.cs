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

        // Property names that carry the base color / albedo, in priority order. Built-in and
        // legacy shaders use _MainTex; URP uses _BaseMap; HDRP uses _BaseColorMap; many custom
        // and mobile shaders use _Albedo/_Diffuse variants.
        private static readonly string[] AlbedoPropertyNames =
        {
            "_MainTex", "_BaseMap", "_BaseColorMap", "_BaseColorTexture", "_Albedo", "_AlbedoMap",
            "_AlbedoTex", "_Diffuse", "_DiffuseMap", "_DiffuseTex", "_ColorMap", "_ColorTex",
            "_Tex", "_Texture", "_BaseTex"
        };

        // Slots that hold data which is NOT a color image (normals, masks, lookups...). Showing one
        // of these as "the" texture of a mesh looks like a corrupted/purple/grey texture, so they
        // are never picked by the "first texture found" fallback.
        private static readonly string[] NonAlbedoKeywords =
        {
            "bump", "normal", "mask", "occlusion", "emission", "emissive", "metallic", "specular",
            "gloss", "rough", "detail", "light", "shadow", "height", "parallax", "displace",
            "cube", "reflection", "env", "ramp", "lut", "noise", "flow", "distortion", "cookie"
        };

        private static bool IsNonAlbedoSlot(string name)
        {
            foreach (var kw in NonAlbedoKeywords)
            {
                if (name.IndexOf(kw, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// AssetStudio 2: best-effort base-color texture for a single material. Looks for a known
        /// albedo slot first (_MainTex, _BaseMap, ...), then falls back to the first texture that
        /// is not obviously a normal/mask/lightmap slot. Used per-submesh by the mesh preview and
        /// for map_Kd in the OBJ export so both pick the same texture.
        /// </summary>
        public static Texture2D FindMainTextureForMaterial(Material mat)
        {
            return FindMainTextureForMaterial(mat, out _);
        }

        public static Texture2D FindMainTextureForMaterial(Material mat, out string propertyName)
        {
            propertyName = null;
            if (mat?.m_SavedProperties?.m_TexEnvs == null)
                return null;

            foreach (var wanted in AlbedoPropertyNames)
            {
                foreach (var texEnv in mat.m_SavedProperties.m_TexEnvs)
                {
                    if (texEnv.Key == wanted && texEnv.Value.m_Texture.TryGet<Texture2D>(out var tex))
                    {
                        propertyName = texEnv.Key;
                        return tex;
                    }
                }
            }
            foreach (var texEnv in mat.m_SavedProperties.m_TexEnvs)
            {
                if (IsNonAlbedoSlot(texEnv.Key))
                    continue;
                if (texEnv.Value.m_Texture.TryGet<Texture2D>(out var tex))
                {
                    propertyName = texEnv.Key;
                    return tex;
                }
            }

            return null;
        }

        /// <summary>
        /// AssetStudio 2: number of float components per vertex in a UV channel (2, 3 or 4), or 0 if
        /// the array does not divide evenly into the vertex count. Unity meshes can store UV0 as a
        /// Vector3/Vector4 (e.g. when extra data is packed in z/w); reading such an array with a
        /// hard-coded stride of 2 samples the wrong floats for every vertex after the first and
        /// scrambles the texture across the mesh.
        /// </summary>
        public static int GetUVStride(float[] uv, int vertexCount)
        {
            if (uv == null || vertexCount <= 0 || uv.Length < vertexCount * 2)
                return 0;
            if (uv.Length % vertexCount != 0)
                return 0;
            var stride = uv.Length / vertexCount;
            return stride >= 2 && stride <= 4 ? stride : 0;
        }
    }
}
