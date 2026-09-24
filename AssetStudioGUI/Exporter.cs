using AssetStudio;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AssetStudioGUI
{
    internal static class Exporter
    {
        public static bool ExportTexture2D(AssetItem item, string exportPath)
        {
            var m_Texture2D = (Texture2D)item.Asset;
            if (Properties.Settings.Default.convertTexture)
            {
                var type = Properties.Settings.Default.convertType;
                if (!TryExportFile(exportPath, item, "." + type.ToString().ToLower(), out var exportFullPath))
                    return false;
                var image = m_Texture2D.ConvertToImage(true);
                if (image == null)
                    return false;
                using (image)
                {
                    using (var file = File.OpenWrite(exportFullPath))
                    {
                        image.WriteToStream(file, type);
                    }
                    return true;
                }
            }
            else
            {
                if (!TryExportFile(exportPath, item, ".tex", out var exportFullPath))
                    return false;
                File.WriteAllBytes(exportFullPath, m_Texture2D.image_data.GetData());
                return true;
            }
        }

        public static bool ExportAudioClip(AssetItem item, string exportPath)
        {
            var m_AudioClip = (AudioClip)item.Asset;
            var m_AudioData = m_AudioClip.m_AudioData.GetData();
            if (m_AudioData == null || m_AudioData.Length == 0)
                return false;
            var converter = new AudioClipConverter(m_AudioClip);
            if (Properties.Settings.Default.convertAudio && converter.IsSupport)
            {
                if (!TryExportFile(exportPath, item, ".wav", out var exportFullPath))
                    return false;
                var buffer = converter.ConvertToWav();
                if (buffer == null)
                    return false;
                File.WriteAllBytes(exportFullPath, buffer);
            }
            else
            {
                if (!TryExportFile(exportPath, item, converter.GetExtensionName(), out var exportFullPath))
                    return false;
                File.WriteAllBytes(exportFullPath, m_AudioData);
            }
            return true;
        }

        public static bool ExportShader(AssetItem item, string exportPath)
        {
            if (!TryExportFile(exportPath, item, ".shader", out var exportFullPath))
                return false;
            var m_Shader = (Shader)item.Asset;
            var str = m_Shader.Convert();
            File.WriteAllText(exportFullPath, str);
            return true;
        }

        public static bool ExportTextAsset(AssetItem item, string exportPath)
        {
            var m_TextAsset = (TextAsset)(item.Asset);
            var extension = ".txt";
            if (Properties.Settings.Default.restoreExtensionName)
            {
                if (!string.IsNullOrEmpty(item.Container))
                {
                    extension = Path.GetExtension(item.Container);
                }
            }
            if (!TryExportFile(exportPath, item, extension, out var exportFullPath))
                return false;
            File.WriteAllBytes(exportFullPath, m_TextAsset.m_Script);
            return true;
        }

        public static bool ExportMonoBehaviour(AssetItem item, string exportPath)
        {
            if (!TryExportFile(exportPath, item, ".json", out var exportFullPath))
                return false;
            var m_MonoBehaviour = (MonoBehaviour)item.Asset;
            var type = m_MonoBehaviour.ToType();
            if (type == null)
            {
                var m_Type = Studio.MonoBehaviourToTypeTree(m_MonoBehaviour);
                type = m_MonoBehaviour.ToType(m_Type);
            }
            var str = JsonConvert.SerializeObject(type, Formatting.Indented);
            File.WriteAllText(exportFullPath, str);
            return true;
        }

        public static bool ExportFont(AssetItem item, string exportPath)
        {
            var m_Font = (Font)item.Asset;
            if (m_Font.m_FontData != null)
            {
                var extension = ".ttf";
                if (m_Font.m_FontData[0] == 79 && m_Font.m_FontData[1] == 84 && m_Font.m_FontData[2] == 84 && m_Font.m_FontData[3] == 79)
                {
                    extension = ".otf";
                }
                if (!TryExportFile(exportPath, item, extension, out var exportFullPath))
                    return false;
                File.WriteAllBytes(exportFullPath, m_Font.m_FontData);
                return true;
            }
            return false;
        }

        public static bool ExportMesh(AssetItem item, string exportPath)
        {
            var m_Mesh = (Mesh)item.Asset;
            if (m_Mesh.m_VertexCount <= 0)
                return false;
            if (m_Mesh.m_Vertices == null || m_Mesh.m_Vertices.Length == 0)
                return false;
            if (!TryExportFile(exportPath, item, ".obj", out var exportFullPath))
                return false;

            //AssetStudio 2: optionally locate the texture(s) this mesh actually uses (via its
            //owning GameObject/Renderer/Material - a raw Mesh asset has no direct PPtr to a
            //Texture2D) and export them as a single textured OBJ+MTL+image model instead of a
            //bare, untextured OBJ.
            //The materials are taken in renderer slot order WITHOUT de-duplication (null = empty
            //slot) so that submesh i always maps to material slot i. The old de-duplicated list
            //shifted every later submesh onto the wrong material/texture whenever two slots
            //shared a material.
            List<Material> meshMaterials = null;
            if (Properties.Settings.Default.exportMeshWithTextures)
            {
                meshMaterials = MeshTextureResolver.FindOrderedMaterials(m_Mesh, Studio.assetsManager);
            }

            //Unique, file-system-safe names per distinct material so two materials that share a
            //name can't collapse into one .mtl entry (which made one of them use the other's texture).
            var materialNames = new Dictionary<Material, string>();
            var distinctMaterials = new List<Material>();
            if (meshMaterials != null)
            {
                var usedNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var mat in meshMaterials)
                {
                    if (mat == null || materialNames.ContainsKey(mat))
                        continue;
                    var baseName = FixFileName(string.IsNullOrEmpty(mat.m_Name) ? "material" : mat.m_Name).Replace(' ', '_');
                    var name = baseName;
                    for (var n = 1; !usedNames.Add(name); n++)
                    {
                        name = baseName + "_" + n;
                    }
                    materialNames[mat] = name;
                    distinctMaterials.Add(mat);
                }
            }

            var sb = new StringBuilder();
            var mtlFileName = Path.GetFileNameWithoutExtension(exportFullPath) + ".mtl";
            var hasMaterials = distinctMaterials.Count > 0;
            if (hasMaterials)
            {
                sb.AppendLine("mtllib " + mtlFileName);
            }
            sb.AppendLine("g " + m_Mesh.m_Name);
            #region Vertices
            int c = 3;
            if (m_Mesh.m_Vertices.Length == m_Mesh.m_VertexCount * 4)
            {
                c = 4;
            }
            for (int v = 0; v < m_Mesh.m_VertexCount; v++)
            {
                sb.AppendFormat("v {0} {1} {2}\r\n", -m_Mesh.m_Vertices[v * c], m_Mesh.m_Vertices[v * c + 1], m_Mesh.m_Vertices[v * c + 2]);
            }
            #endregion

            #region UV
            //UV0 can be a Vector2/3/4 per vertex; use its real stride.
            var uvStride = MeshTextureResolver.GetUVStride(m_Mesh.m_UV0, m_Mesh.m_VertexCount);
            var hasUV = uvStride > 0;
            if (hasUV)
            {
                for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                {
                    sb.AppendFormat("vt {0} {1}\r\n", m_Mesh.m_UV0[v * uvStride], m_Mesh.m_UV0[v * uvStride + 1]);
                }
            }
            #endregion

            #region Normals
            var hasNormals = false;
            if (m_Mesh.m_Normals?.Length > 0)
            {
                var nc = 0;
                if (m_Mesh.m_Normals.Length == m_Mesh.m_VertexCount * 3)
                {
                    nc = 3;
                }
                else if (m_Mesh.m_Normals.Length == m_Mesh.m_VertexCount * 4)
                {
                    nc = 4;
                }
                if (nc != 0)
                {
                    hasNormals = true;
                    for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                    {
                        sb.AppendFormat("vn {0} {1} {2}\r\n", -m_Mesh.m_Normals[v * nc], m_Mesh.m_Normals[v * nc + 1], m_Mesh.m_Normals[v * nc + 2]);
                    }
                }
            }
            #endregion

            #region Face
            //Only reference vt/vn indices that were actually written; a face like "1/1/1" pointing at
            //a nonexistent vt/vn makes importers reject the file or scramble the UVs.
            var faceFormat = hasUV && hasNormals ? "{0}/{0}/{0}" : hasUV ? "{0}/{0}" : hasNormals ? "{0}//{0}" : "{0}";
            int sum = 0;
            for (var i = 0; i < m_Mesh.m_SubMeshes.Length; i++)
            {
                sb.AppendLine($"g {m_Mesh.m_Name}_{i}");
                if (hasMaterials)
                {
                    //Submesh index -> material slot of the same index (last slot reused if the
                    //renderer has fewer materials than submeshes, as Unity does).
                    Material mat = null;
                    if (meshMaterials.Count > 0)
                    {
                        mat = meshMaterials[i < meshMaterials.Count ? i : meshMaterials.Count - 1];
                    }
                    if (mat != null && materialNames.TryGetValue(mat, out var matName))
                    {
                        sb.AppendLine($"usemtl {matName}");
                    }
                }
                int indexCount = (int)m_Mesh.m_SubMeshes[i].indexCount;
                var end = sum + indexCount / 3;
                for (int f = sum; f < end; f++)
                {
                    sb.AppendFormat("f {0} {1} {2}\r\n",
                        string.Format(faceFormat, m_Mesh.m_Indices[f * 3 + 2] + 1),
                        string.Format(faceFormat, m_Mesh.m_Indices[f * 3 + 1] + 1),
                        string.Format(faceFormat, m_Mesh.m_Indices[f * 3] + 1));
                }
                sum = end;
            }
            #endregion

            sb.Replace("NaN", "0");
            File.WriteAllText(exportFullPath, sb.ToString());

            if (hasMaterials)
            {
                ExportMeshMaterials(distinctMaterials, materialNames, Path.GetDirectoryName(exportFullPath), mtlFileName);
            }

            return true;
        }

        //AssetStudio 2: writes a .mtl alongside a mesh's .obj, plus the referenced textures
        //(map_Kd for the base color texture, map_Bump for a normal/bump map when present), so the
        //mesh and its texture(s) can be opened together as one 3D model in any OBJ-compatible
        //viewer/DCC tool instead of the user having to separately export and manually re-link
        //the texture in stock AssetStudio.
        private static void ExportMeshMaterials(List<Material> materials, Dictionary<Material, string> materialNames, string exportDir, string mtlFileName)
        {
            var sb = new StringBuilder();
            //One unique file name per texture object. Two different Texture2D assets that share a
            //name used to write to the same .png, so the last one silently overwrote the other.
            var textureFileNames = new Dictionary<Texture2D, string>();
            var usedFileNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            string ExportTexture(Texture2D tex)
            {
                if (textureFileNames.TryGetValue(tex, out var existing))
                    return existing;

                var baseName = FixFileName(string.IsNullOrEmpty(tex.m_Name) ? "texture" : tex.m_Name).Replace(' ', '_');
                var texFileName = baseName + ".png";
                for (var n = 1; !usedFileNames.Add(texFileName); n++)
                {
                    texFileName = baseName + "_" + n + ".png";
                }
                textureFileNames[tex] = texFileName;

                try
                {
                    using (var image = tex.ConvertToImage(true))
                    {
                        if (image != null)
                        {
                            using (var fs = File.Create(Path.Combine(exportDir, texFileName)))
                            {
                                image.WriteToStream(fs, ImageFormat.Png);
                            }
                        }
                    }
                }
                catch
                {
                    //A texture that fails to decode shouldn't stop the rest of the model export.
                }
                return texFileName;
            }

            foreach (var mat in materials)
            {
                sb.AppendLine($"newmtl {materialNames[mat]}");
                sb.AppendLine("Kd 1.000 1.000 1.000");

                if (mat.m_SavedProperties?.m_TexEnvs == null)
                {
                    continue;
                }

                //Base color: same selection logic as the mesh preview (handles _MainTex, _BaseMap, ...).
                var mainTex = MeshTextureResolver.FindMainTextureForMaterial(mat, out var mainTexProperty);
                if (mainTex != null)
                {
                    sb.AppendLine($"map_Kd {ExportTexture(mainTex)}");
                }

                foreach (var texEnv in mat.m_SavedProperties.m_TexEnvs)
                {
                    if (!texEnv.Value.m_Texture.TryGet<Texture2D>(out var tex))
                        continue;

                    if (texEnv.Key == mainTexProperty)
                        continue;

                    //Other slots are still exported as files; only the normal/bump map is wired in.
                    var texFileName = ExportTexture(tex);
                    if (texEnv.Key == "_BumpMap")
                        sb.AppendLine($"map_Bump {texFileName}");
                }
            }

            File.WriteAllText(Path.Combine(exportDir, mtlFileName), sb.ToString());
        }

        public static bool ExportVideoClip(AssetItem item, string exportPath)
        {
            var m_VideoClip = (VideoClip)item.Asset;
            if (m_VideoClip.m_ExternalResources.m_Size > 0)
            {
                if (!TryExportFile(exportPath, item, Path.GetExtension(m_VideoClip.m_OriginalPath), out var exportFullPath))
                    return false;
                m_VideoClip.m_VideoData.WriteData(exportFullPath);
                return true;
            }
            return false;
        }

        public static bool ExportMovieTexture(AssetItem item, string exportPath)
        {
            var m_MovieTexture = (MovieTexture)item.Asset;
            if (!TryExportFile(exportPath, item, ".ogv", out var exportFullPath))
                return false;
            File.WriteAllBytes(exportFullPath, m_MovieTexture.m_MovieData);
            return true;
        }

        public static bool ExportSprite(AssetItem item, string exportPath)
        {
            var type = Properties.Settings.Default.convertType;
            if (!TryExportFile(exportPath, item, "." + type.ToString().ToLower(), out var exportFullPath))
                return false;
            var image = ((Sprite)item.Asset).GetImage();
            if (image != null)
            {
                using (image)
                {
                    using (var file = File.OpenWrite(exportFullPath))
                    {
                        image.WriteToStream(file, type);
                    }
                    return true;
                }
            }
            return false;
        }

        public static bool ExportRawFile(AssetItem item, string exportPath)
        {
            if (!TryExportFile(exportPath, item, ".dat", out var exportFullPath))
                return false;
            File.WriteAllBytes(exportFullPath, item.Asset.GetRawData());
            return true;
        }

        private static bool TryExportFile(string dir, AssetItem item, string extension, out string fullPath)
        {
            var fileName = FixFileName(item.Text);
            fullPath = Path.Combine(dir, fileName + extension);
            if (!File.Exists(fullPath))
            {
                Directory.CreateDirectory(dir);
                return true;
            }
            fullPath = Path.Combine(dir, fileName + item.UniqueID + extension);
            if (!File.Exists(fullPath))
            {
                Directory.CreateDirectory(dir);
                return true;
            }
            return false;
        }

        public static bool ExportAnimator(AssetItem item, string exportPath, List<AssetItem> animationList = null)
        {
            var exportFullPath = Path.Combine(exportPath, item.Text, item.Text + ".fbx");
            if (File.Exists(exportFullPath))
            {
                exportFullPath = Path.Combine(exportPath, item.Text + item.UniqueID, item.Text + ".fbx");
            }
            var m_Animator = (Animator)item.Asset;
            var convert = animationList != null
                ? new ModelConverter(m_Animator, Properties.Settings.Default.convertType, animationList.Select(x => (AnimationClip)x.Asset).ToArray())
                : new ModelConverter(m_Animator, Properties.Settings.Default.convertType);
            ExportFbx(convert, exportFullPath);
            return true;
        }

        public static void ExportGameObject(GameObject gameObject, string exportPath, List<AssetItem> animationList = null, HashSet<long> excludedGameObjectPathIDs = null)
        {
            var convert = animationList != null
                ? new ModelConverter(gameObject, Properties.Settings.Default.convertType, animationList.Select(x => (AnimationClip)x.Asset).ToArray(), excludedGameObjectPathIDs)
                : new ModelConverter(gameObject, Properties.Settings.Default.convertType, null, excludedGameObjectPathIDs);
            exportPath = exportPath + FixFileName(gameObject.m_Name) + ".fbx";
            ExportFbx(convert, exportPath);
        }

        public static void ExportGameObjectMerge(List<GameObject> gameObject, string exportPath, List<AssetItem> animationList = null)
        {
            var rootName = Path.GetFileNameWithoutExtension(exportPath);
            var convert = animationList != null
                ? new ModelConverter(rootName, gameObject, Properties.Settings.Default.convertType, animationList.Select(x => (AnimationClip)x.Asset).ToArray())
                : new ModelConverter(rootName, gameObject, Properties.Settings.Default.convertType);
            ExportFbx(convert, exportPath);
        }

        private static void ExportFbx(IImported convert, string exportPath)
        {
            var eulerFilter = Properties.Settings.Default.eulerFilter;
            var filterPrecision = (float)Properties.Settings.Default.filterPrecision;
            var exportAllNodes = Properties.Settings.Default.exportAllNodes;
            var exportSkins = Properties.Settings.Default.exportSkins;
            var exportAnimations = Properties.Settings.Default.exportAnimations;
            var exportBlendShape = Properties.Settings.Default.exportBlendShape;
            var castToBone = Properties.Settings.Default.castToBone;
            var boneSize = (int)Properties.Settings.Default.boneSize;
            var exportAllUvsAsDiffuseMaps = Properties.Settings.Default.exportAllUvsAsDiffuseMaps;
            var scaleFactor = (float)Properties.Settings.Default.scaleFactor;
            var fbxVersion = Properties.Settings.Default.fbxVersion;
            var fbxFormat = Properties.Settings.Default.fbxFormat;
            ModelExporter.ExportFbx(exportPath, convert, eulerFilter, filterPrecision,
                exportAllNodes, exportSkins, exportAnimations, exportBlendShape, castToBone, boneSize, exportAllUvsAsDiffuseMaps, scaleFactor, fbxVersion, fbxFormat == 1);
        }

        public static bool ExportDumpFile(AssetItem item, string exportPath)
        {
            if (!TryExportFile(exportPath, item, ".txt", out var exportFullPath))
                return false;
            var str = item.Asset.Dump();
            if (str == null && item.Asset is MonoBehaviour m_MonoBehaviour)
            {
                var m_Type = Studio.MonoBehaviourToTypeTree(m_MonoBehaviour);
                str = m_MonoBehaviour.Dump(m_Type);
            }
            if (str != null)
            {
                File.WriteAllText(exportFullPath, str);
                return true;
            }
            return false;
        }

        public static bool ExportConvertFile(AssetItem item, string exportPath)
        {
            switch (item.Type)
            {
                case ClassIDType.Texture2D:
                    return ExportTexture2D(item, exportPath);
                case ClassIDType.AudioClip:
                    return ExportAudioClip(item, exportPath);
                case ClassIDType.Shader:
                    return ExportShader(item, exportPath);
                case ClassIDType.TextAsset:
                    return ExportTextAsset(item, exportPath);
                case ClassIDType.MonoBehaviour:
                    return ExportMonoBehaviour(item, exportPath);
                case ClassIDType.Font:
                    return ExportFont(item, exportPath);
                case ClassIDType.Mesh:
                    return ExportMesh(item, exportPath);
                case ClassIDType.VideoClip:
                    return ExportVideoClip(item, exportPath);
                case ClassIDType.MovieTexture:
                    return ExportMovieTexture(item, exportPath);
                case ClassIDType.Sprite:
                    return ExportSprite(item, exportPath);
                case ClassIDType.Animator:
                    return ExportAnimator(item, exportPath);
                case ClassIDType.AnimationClip:
                    return false;
                default:
                    return ExportRawFile(item, exportPath);
            }
        }

        public static string FixFileName(string str)
        {
            if (str.Length >= 260) return Path.GetRandomFileName();
            return Path.GetInvalidFileNameChars().Aggregate(str, (current, c) => current.Replace(c, '_'));
        }
    }
}
