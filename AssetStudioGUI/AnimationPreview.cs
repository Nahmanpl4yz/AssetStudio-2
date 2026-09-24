using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AssetStudio;
#if NET472
using OpenTK;
using Vector2 = OpenTK.Vector2;
using Vector3 = OpenTK.Vector3;
using Vector4 = OpenTK.Vector4;
using Quaternion = OpenTK.Quaternion;
using Matrix4 = OpenTK.Matrix4;
#else
using OpenTK.Mathematics;
using Vector2 = OpenTK.Mathematics.Vector2;
using Vector3 = OpenTK.Mathematics.Vector3;
using Vector4 = OpenTK.Mathematics.Vector4;
using Quaternion = OpenTK.Mathematics.Quaternion;
using Matrix4 = OpenTK.Mathematics.Matrix4;
#endif

namespace AssetStudioGUI
{
    // AssetStudio 2 - Feature 3: Animation preview.
    //
    // When an AnimationClip is selected, this scans every loaded GameObject for an Animator
    // whose controller references the clip (falling back to scanning every SkinnedMeshRenderer's
    // bone set against the clip's curve paths if no Animator claims it - covers clips extracted
    // standalone from a controller). It then builds a lightweight rig (bone hierarchy + skin
    // data for every mesh under that rig) and decodes the clip's curves into per-bone-path
    // keyframe tracks, entirely independent of the FBX exporter/native wrapper so this works
    // even in builds without the native FBX plugin, and keeps quaternions end-to-end instead of
    // round-tripping through Euler angles.
    public static class AnimationPreview
    {
        public class Bone
        {
            public Transform UnityTransform;
            public string Path;
            public Bone Parent;
            public List<Bone> Children = new List<Bone>();

            // Bind (rest) pose local TRS, used when a track doesn't animate this bone.
            public Vector3 BindLocalPos;
            public Quaternion BindLocalRot;
            public Vector3 BindLocalScale;

            // Current evaluated local TRS (updated every frame during playback).
            public Vector3 LocalPos;
            public Quaternion LocalRot;
            public Vector3 LocalScale;

            // Current evaluated world matrix (updated every frame, root-to-here).
            public Matrix4 WorldMatrix;
        }

        public class SkinnedPart
        {
            public Mesh Mesh;
            public Bone[] BoneMap;              // per Mesh.m_BindPose index -> Bone (may contain nulls)
            public Matrix4[] InverseBindPoses;   // per Mesh.m_BindPose index, converted to OpenTK
            public GameObject OwnerGameObject;
            public string Name;
            // Fallback rigid transform for meshes without skin data (e.g. MeshFilter parts
            // mixed into the same rig, like a static muzzle attached to an animated character).
            public Bone RigidParentBone;
        }

        public class Rig
        {
            public Bone Root;
            public Dictionary<string, Bone> BonesByPath = new Dictionary<string, Bone>();
            public List<SkinnedPart> Parts = new List<SkinnedPart>();
            public GameObject RootGameObject;
        }

        public class Track
        {
            public List<(float time, Vector3 value)> Translations = new List<(float, Vector3)>();
            public List<(float time, Quaternion value)> Rotations = new List<(float, Quaternion)>();
            public List<(float time, Vector3 value)> Scalings = new List<(float, Vector3)>();
        }

        public class DecodedClip
        {
            public string Name;
            public float Length;
            public float SampleRate = 60f;
            public Dictionary<string, Track> TracksByPath = new Dictionary<string, Track>();
        }

        // ------------------------------------------------------------------
        // Rig discovery
        // ------------------------------------------------------------------

        // Finds the best Animator to drive this clip: prefers an Animator whose
        // AnimatorController (or override controller) lists the clip, falls back to any
        // Animator whose rig's bone paths intersect the clip's curve paths, and finally to
        // just the first Animator found anywhere (better than nothing for a quick look).
        public static Animator FindOwningAnimator(AnimationClip clip, AssetsManager assetsManager)
        {
            Animator pathMatchFallback = null;
            Animator anyFallback = null;
            var clipPaths = GetClipPathHashes(clip);

            foreach (var assetsFile in assetsManager.assetsFileList)
            {
                foreach (var obj in assetsFile.Objects)
                {
                    if (!(obj is Animator animator)) continue;
                    if (anyFallback == null) anyFallback = animator;

                    if (ControllerReferencesClip(animator, clip))
                    {
                        return animator;
                    }

                    if (pathMatchFallback == null && clipPaths.Count > 0 && AnimatorRigMatchesPaths(animator, clipPaths))
                    {
                        pathMatchFallback = animator;
                    }
                }
            }
            return pathMatchFallback ?? anyFallback;
        }

        private static bool ControllerReferencesClip(Animator animator, AnimationClip clip)
        {
            try
            {
                if (!animator.m_Controller.TryGet(out var rc)) return false;
                switch (rc)
                {
                    case AnimatorController ac:
                        foreach (var pptr in ac.m_AnimationClips)
                        {
                            if (pptr.TryGet(out var c) && c == clip) return true;
                        }
                        break;
                    case AnimatorOverrideController aoc:
                        foreach (var clipOverride in aoc.m_Clips)
                        {
                            if (clipOverride.m_OriginalClip.TryGet(out var oc) && oc == clip) return true;
                            if (clipOverride.m_OverrideClip.TryGet(out var vc) && vc == clip) return true;
                        }
                        // also check the base controller's own clip list
                        if (aoc.m_Controller.TryGet(out var baseRc) && baseRc is AnimatorController baseAc)
                        {
                            foreach (var pptr in baseAc.m_AnimationClips)
                            {
                                if (pptr.TryGet(out var c) && c == clip) return true;
                            }
                        }
                        break;
                }
            }
            catch { /* best-effort */ }
            return false;
        }

        private static HashSet<uint> GetClipPathHashes(AnimationClip clip)
        {
            var set = new HashSet<uint>();
            if (clip.m_ClipBindingConstant != null)
            {
                foreach (var b in clip.m_ClipBindingConstant.genericBindings)
                {
                    if (b.typeID == ClassIDType.Transform) set.Add(b.path);
                }
            }
            if (clip.m_RotationCurves != null)
                foreach (var c in clip.m_RotationCurves) set.Add(PathHash(c.path));
            if (clip.m_PositionCurves != null)
                foreach (var c in clip.m_PositionCurves) set.Add(PathHash(c.path));
            if (clip.m_ScaleCurves != null)
                foreach (var c in clip.m_ScaleCurves) set.Add(PathHash(c.path));
            if (clip.m_CompressedRotationCurves != null)
                foreach (var c in clip.m_CompressedRotationCurves) set.Add(PathHash(c.m_Path));
            return set;
        }

        private static bool AnimatorRigMatchesPaths(Animator animator, HashSet<uint> clipPaths)
        {
            if (!animator.m_GameObject.TryGet(out var go)) return false;
            var t = GameObjectMeshCombiner.GetTransform(go);
            if (t == null) return false;
            var root = GameObjectMeshCombiner.FindRootTransform(go) ?? t;
            var hit = false;
            WalkHashes(root, "", crc =>
            {
                if (clipPaths.Contains(crc)) hit = true;
            });
            return hit;
        }

        private static void WalkHashes(Transform t, string parentPath, Action<uint> onPath)
        {
            if (!t.m_GameObject.TryGet(out var go)) return;
            var path = string.IsNullOrEmpty(parentPath) ? go.m_Name : parentPath + "/" + go.m_Name;
            onPath(PathHash(path));
            if (t.m_Children == null) return;
            foreach (var childPtr in t.m_Children)
            {
                if (childPtr.TryGet(out var child)) WalkHashes(child, path, onPath);
            }
        }

        public static uint PathHash(string path)
        {
            var crc = new SevenZip.CRC();
            var bytes = Encoding.UTF8.GetBytes(path ?? string.Empty);
            crc.Update(bytes, 0, (uint)bytes.Length);
            return crc.GetDigest();
        }

        // ------------------------------------------------------------------
        // Rig building
        // ------------------------------------------------------------------

        // Builds the bone hierarchy + collects every skinned/static mesh under the Animator's
        // GameObject, in root-relative paths matching Unity's own CRC path convention so the
        // decoded clip's tracks line up by simple dictionary lookup.
        public static Rig BuildRig(Animator animator)
        {
            if (!animator.m_GameObject.TryGet(out var animatorGo)) return null;
            var animTransform = GameObjectMeshCombiner.GetTransform(animatorGo);
            if (animTransform == null) return null;
            var rootTransform = GameObjectMeshCombiner.FindRootTransform(animatorGo) ?? animTransform;

            var rig = new Rig();
            rig.RootGameObject = rootTransform.m_GameObject.TryGet(out var rootGo) ? rootGo : animatorGo;
            rig.Root = BuildBoneTree(rootTransform, null, "", rig.BonesByPath, new HashSet<long>());
            CollectSkinnedParts(rootTransform, rig);
            return rig;
        }

        private static Bone BuildBoneTree(Transform t, Bone parent, string parentPath, Dictionary<string, Bone> byPath, HashSet<long> visited)
        {
            if (t == null || !visited.Add(t.m_PathID)) return null;
            if (!t.m_GameObject.TryGet(out var go)) return null;

            var path = string.IsNullOrEmpty(parentPath) ? go.m_Name : parentPath + "/" + go.m_Name;
            var bone = new Bone
            {
                UnityTransform = t,
                Path = path,
                Parent = parent,
                BindLocalPos = ToV3(t.m_LocalPosition),
                BindLocalRot = ToQuat(t.m_LocalRotation),
                BindLocalScale = ToV3(t.m_LocalScale)
            };
            bone.LocalPos = bone.BindLocalPos;
            bone.LocalRot = bone.BindLocalRot;
            bone.LocalScale = bone.BindLocalScale;

            byPath[path] = bone;

            if (t.m_Children != null)
            {
                foreach (var childPtr in t.m_Children)
                {
                    if (childPtr.TryGet(out var child))
                    {
                        var childBone = BuildBoneTree(child, bone, path, byPath, visited);
                        if (childBone != null) bone.Children.Add(childBone);
                    }
                }
            }
            return bone;
        }

        private static void CollectSkinnedParts(Transform root, Rig rig)
        {
            var visited = new HashSet<long>();
            WalkParts(root, rig, visited);
        }

        private static void WalkParts(Transform t, Rig rig, HashSet<long> visited)
        {
            if (t == null || !visited.Add(t.m_PathID)) return;
            if (t.m_GameObject.TryGet(out var go))
            {
                foreach (var componentPtr in go.m_Components)
                {
                    if (!componentPtr.TryGet(out var comp)) continue;

                    if (comp is SkinnedMeshRenderer smr && smr.m_Mesh.TryGet(out var skinnedMesh) && skinnedMesh.m_VertexCount > 0)
                    {
                        var part = new SkinnedPart
                        {
                            Mesh = skinnedMesh,
                            OwnerGameObject = go,
                            Name = go.m_Name
                        };

                        if (skinnedMesh.m_BindPose != null && skinnedMesh.m_BindPose.Length > 0 && smr.m_Bones != null)
                        {
                            var boneCount = Math.Min(skinnedMesh.m_BindPose.Length, smr.m_Bones.Length);
                            part.BoneMap = new Bone[boneCount];
                            part.InverseBindPoses = new Matrix4[boneCount];
                            for (int i = 0; i < boneCount; i++)
                            {
                                part.InverseBindPoses[i] = ToMatrix4(skinnedMesh.m_BindPose[i]);
                                if (smr.m_Bones[i].TryGet(out var boneTransform) &&
                                    rig.BonesByPath.TryGetValue(FindPathFor(boneTransform, rig), out var bone))
                                {
                                    part.BoneMap[i] = bone;
                                }
                            }
                        }
                        else
                        {
                            rig.BonesByPath.TryGetValue(FindPathFor(t, rig), out var fallbackBone);
                            part.RigidParentBone = fallbackBone ?? rig.Root;
                        }
                        rig.Parts.Add(part);
                    }
                    else if (comp is MeshFilter mf && mf.m_Mesh.TryGet(out var staticMesh) && staticMesh.m_VertexCount > 0)
                    {
                        rig.BonesByPath.TryGetValue(FindPathFor(t, rig), out var bone);
                        rig.Parts.Add(new SkinnedPart
                        {
                            Mesh = staticMesh,
                            OwnerGameObject = go,
                            Name = go.m_Name,
                            RigidParentBone = bone ?? rig.Root
                        });
                    }
                }
            }

            if (t.m_Children != null)
            {
                foreach (var childPtr in t.m_Children)
                {
                    if (childPtr.TryGet(out var child)) WalkParts(child, rig, visited);
                }
            }
        }

        // Cache-free path lookup by PathID against every bone already built - rig hierarchies
        // are small enough (dozens to low hundreds of bones) that a linear scan per bone/mesh
        // during one-time rig construction is negligible.
        private static string FindPathFor(Transform t, Rig rig)
        {
            foreach (var kvp in rig.BonesByPath)
            {
                if (kvp.Value.UnityTransform.m_PathID == t.m_PathID) return kvp.Key;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Clip decoding (native-FBX-free, quaternion-based)
        // ------------------------------------------------------------------

        public static DecodedClip DecodeClip(AnimationClip clip, Rig rig)
        {
            var decoded = new DecodedClip { Name = clip.m_Name, SampleRate = clip.m_SampleRate > 0 ? clip.m_SampleRate : 60f };
            float maxTime = 0f;

            Track TrackFor(string path)
            {
                if (path == null) return null;
                if (!decoded.TracksByPath.TryGetValue(path, out var track))
                {
                    track = new Track();
                    decoded.TracksByPath[path] = track;
                }
                return track;
            }

            // Resolves a curve's CRC path hash (relative, possibly rooted anywhere in the
            // hierarchy) against the rig's known bone paths by matching the path's tail -
            // mirrors how Unity resolves an AnimationClip against whatever GameObject it's
            // played on, so the same clip works for any rig sharing bone names.
            string ResolvePath(uint hash, Dictionary<uint, string> knownFragments)
            {
                if (knownFragments.TryGetValue(hash, out var frag) && rig.BonesByPath.ContainsKey(frag))
                    return frag;
                // fall back to suffix search against every known bone path
                foreach (var bonePath in rig.BonesByPath.Keys)
                {
                    var idx = 0;
                    while (idx >= 0)
                    {
                        idx = bonePath.IndexOf('/', idx);
                        var candidate = idx < 0 ? bonePath : bonePath.Substring(idx + 1);
                        if (PathHash(candidate) == hash)
                        {
                            knownFragments[hash] = candidate;
                            return candidate == bonePath ? bonePath : (rig.BonesByPath.ContainsKey(candidate) ? candidate : bonePath);
                        }
                        if (idx >= 0) idx++;
                    }
                }
                return null;
            }

            var fragmentCache = new Dictionary<uint, string>();
            // Build a hash->path map for every bone in the rig up front (root-relative and every
            // suffix), which both resolves the common case in O(1) and seeds ResolvePath's cache.
            foreach (var bonePath in rig.BonesByPath.Keys)
            {
                fragmentCache[PathHash(bonePath)] = bonePath;
                var idx = 0;
                while (idx >= 0)
                {
                    idx = bonePath.IndexOf('/', idx);
                    if (idx < 0) break;
                    var suffix = bonePath.Substring(idx + 1);
                    var h = PathHash(suffix);
                    if (!fragmentCache.ContainsKey(h)) fragmentCache[h] = suffix;
                    idx++;
                }
            }

            if (clip.m_Legacy)
            {
                if (clip.m_RotationCurves != null)
                {
                    foreach (var c in clip.m_RotationCurves)
                    {
                        var path = ResolvePath(PathHash(c.path), fragmentCache) ?? (rig.BonesByPath.ContainsKey(c.path) ? c.path : null) ?? c.path;
                        var track = TrackFor(path);
                        if (track == null) continue;
                        foreach (var k in c.curve.m_Curve)
                        {
                            track.Rotations.Add((k.time, ToQuat(k.value)));
                            maxTime = Math.Max(maxTime, k.time);
                        }
                    }
                }
                if (clip.m_CompressedRotationCurves != null)
                {
                    foreach (var c in clip.m_CompressedRotationCurves)
                    {
                        var path = ResolvePath(PathHash(c.m_Path), fragmentCache) ?? c.m_Path;
                        var track = TrackFor(path);
                        if (track == null) continue;
                        var numKeys = c.m_Times.m_NumItems;
                        var timesData = c.m_Times.UnpackInts();
                        var times = new float[numKeys];
                        int t = 0;
                        for (int i = 0; i < numKeys; i++) { t += timesData[i]; times[i] = t * 0.01f; }
                        var quats = c.m_Values.UnpackQuats();
                        for (int i = 0; i < numKeys && i < quats.Length; i++)
                        {
                            track.Rotations.Add((times[i], ToQuat(quats[i])));
                            maxTime = Math.Max(maxTime, times[i]);
                        }
                    }
                }
                if (clip.m_PositionCurves != null)
                {
                    foreach (var c in clip.m_PositionCurves)
                    {
                        var path = ResolvePath(PathHash(c.path), fragmentCache) ?? c.path;
                        var track = TrackFor(path);
                        if (track == null) continue;
                        foreach (var k in c.curve.m_Curve)
                        {
                            track.Translations.Add((k.time, ToV3(k.value)));
                            maxTime = Math.Max(maxTime, k.time);
                        }
                    }
                }
                if (clip.m_ScaleCurves != null)
                {
                    foreach (var c in clip.m_ScaleCurves)
                    {
                        var path = ResolvePath(PathHash(c.path), fragmentCache) ?? c.path;
                        var track = TrackFor(path);
                        if (track == null) continue;
                        foreach (var k in c.curve.m_Curve)
                        {
                            track.Scalings.Add((k.time, ToV3(k.value)));
                            maxTime = Math.Max(maxTime, k.time);
                        }
                    }
                }
                if (clip.m_EulerCurves != null)
                {
                    foreach (var c in clip.m_EulerCurves)
                    {
                        var path = ResolvePath(PathHash(c.path), fragmentCache) ?? c.path;
                        var track = TrackFor(path);
                        if (track == null) continue;
                        foreach (var k in c.curve.m_Curve)
                        {
                            var euler = ToV3(k.value);
                            var q = Quaternion.FromEulerAngles(MathHelper.DegreesToRadians(euler.X), MathHelper.DegreesToRadians(euler.Y), MathHelper.DegreesToRadians(euler.Z));
                            track.Rotations.Add((k.time, q));
                            maxTime = Math.Max(maxTime, k.time);
                        }
                    }
                }
            }
            else if (clip.m_MuscleClip?.m_Clip != null)
            {
                DecodeGenericClip(clip, decoded, fragmentCache, rig, ref maxTime);
            }

            decoded.Length = Math.Max(maxTime, 0.01f);
            return decoded;
        }

        private static void DecodeGenericClip(AnimationClip clip, DecodedClip decoded, Dictionary<uint, string> fragmentCache, Rig rig, ref float maxTime)
        {
            var m_Clip = clip.m_MuscleClip.m_Clip;
            var m_ClipBindingConstant = clip.m_ClipBindingConstant ?? m_Clip.ConvertValueArrayToGenericBinding();

            // A ref parameter (maxTime) can't be captured by the local function below, so we
            // track the running max in an ordinary local and write it back to the ref parameter
            // once, after every call site below has finished updating it.
            float maxTimeLocal = maxTime;

            Track TrackFor(string path)
            {
                if (path == null) return null;
                if (!decoded.TracksByPath.TryGetValue(path, out var track))
                {
                    track = new Track();
                    decoded.TracksByPath[path] = track;
                }
                return track;
            }

            string ResolvePath(uint hash)
            {
                if (fragmentCache.TryGetValue(hash, out var frag)) return frag;
                return null;
            }

            void ReadCurve(int index, float time, float[] data, int offset, ref int curveIndex)
            {
                var binding = m_ClipBindingConstant.FindBinding(index);
                if (binding == null) { curveIndex++; return; }
                if (binding.typeID != ClassIDType.Transform) { curveIndex++; return; }

                var path = ResolvePath(binding.path);
                var track = TrackFor(path);
                switch (binding.attribute)
                {
                    case 1: //position
                        {
                            var v = new Vector3(data[curveIndex++ + offset], data[curveIndex++ + offset], data[curveIndex++ + offset]);
                            track?.Translations.Add((time, v));
                            break;
                        }
                    case 2: //rotation (quaternion)
                        {
                            var q = new Quaternion(data[curveIndex++ + offset], data[curveIndex++ + offset], data[curveIndex++ + offset], data[curveIndex++ + offset]);
                            track?.Rotations.Add((time, q));
                            break;
                        }
                    case 3: //scale
                        {
                            var v = new Vector3(data[curveIndex++ + offset], data[curveIndex++ + offset], data[curveIndex++ + offset]);
                            track?.Scalings.Add((time, v));
                            break;
                        }
                    case 4: //euler
                        {
                            var euler = new Vector3(data[curveIndex++ + offset], data[curveIndex++ + offset], data[curveIndex++ + offset]);
                            var q = Quaternion.FromEulerAngles(MathHelper.DegreesToRadians(euler.X), MathHelper.DegreesToRadians(euler.Y), MathHelper.DegreesToRadians(euler.Z));
                            track?.Rotations.Add((time, q));
                            break;
                        }
                    default:
                        curveIndex++;
                        break;
                }
                maxTimeLocal = Math.Max(maxTimeLocal, time);
            }

            var streamedFrames = m_Clip.m_StreamedClip.ReadData();
            for (int frameIndex = 1; frameIndex < streamedFrames.Count - 1; frameIndex++)
            {
                var frame = streamedFrames[frameIndex];
                var streamedValues = frame.keyList.Select(x => x.value).ToArray();
                for (int curveIndex = 0; curveIndex < frame.keyList.Length;)
                {
                    ReadCurve(frame.keyList[curveIndex].index, frame.time, streamedValues, 0, ref curveIndex);
                }
            }

            var m_DenseClip = m_Clip.m_DenseClip;
            var streamCount = m_Clip.m_StreamedClip.curveCount;
            for (int frameIndex = 0; frameIndex < m_DenseClip.m_FrameCount; frameIndex++)
            {
                var time = m_DenseClip.m_BeginTime + frameIndex / m_DenseClip.m_SampleRate;
                var frameOffset = frameIndex * m_DenseClip.m_CurveCount;
                for (int curveIndex = 0; curveIndex < m_DenseClip.m_CurveCount;)
                {
                    var index = streamCount + curveIndex;
                    ReadCurve((int)index, time, m_DenseClip.m_SampleArray, (int)frameOffset, ref curveIndex);
                }
            }

            if (m_Clip.m_ConstantClip != null)
            {
                var m_ConstantClip = m_Clip.m_ConstantClip;
                var denseCount = m_Clip.m_DenseClip.m_CurveCount;
                var time2 = 0f;
                for (int i = 0; i < 2; i++)
                {
                    for (int curveIndex = 0; curveIndex < m_ConstantClip.data.Length;)
                    {
                        var index = streamCount + denseCount + curveIndex;
                        ReadCurve((int)index, time2, m_ConstantClip.data, 0, ref curveIndex);
                    }
                    time2 = clip.m_MuscleClip.m_StopTime;
                }
            }

            // Write the accumulated max time back to the caller's ref parameter.
            maxTime = maxTimeLocal;
        }

        // ------------------------------------------------------------------
        // Pose evaluation / playback
        // ------------------------------------------------------------------

        // Samples the clip at `time` and writes each bone's LocalPos/LocalRot/LocalScale and
        // WorldMatrix. Bones with no track keep their bind pose (so unanimated parts of the rig
        // stay put, matching Unity's behaviour for a clip that doesn't touch every bone).
        public static void EvaluatePose(Rig rig, DecodedClip clip, float time)
        {
            EvaluateBone(rig.Root, clip, time, Matrix4.Identity);
        }

        private static void EvaluateBone(Bone bone, DecodedClip clip, float time, Matrix4 parentWorld)
        {
            if (bone == null) return;

            if (clip != null && clip.TracksByPath.TryGetValue(bone.Path, out var track))
            {
                bone.LocalPos = track.Translations.Count > 0 ? SampleVec(track.Translations, time) : bone.BindLocalPos;
                bone.LocalRot = track.Rotations.Count > 0 ? SampleQuat(track.Rotations, time) : bone.BindLocalRot;
                bone.LocalScale = track.Scalings.Count > 0 ? SampleVec(track.Scalings, time) : bone.BindLocalScale;
            }
            else
            {
                bone.LocalPos = bone.BindLocalPos;
                bone.LocalRot = bone.BindLocalRot;
                bone.LocalScale = bone.BindLocalScale;
            }

            var local = Matrix4.CreateScale(bone.LocalScale) * Matrix4.CreateFromQuaternion(bone.LocalRot) * Matrix4.CreateTranslation(bone.LocalPos);
            bone.WorldMatrix = local * parentWorld;

            foreach (var child in bone.Children)
            {
                EvaluateBone(child, clip, time, bone.WorldMatrix);
            }
        }

        private static Vector3 SampleVec(List<(float time, Vector3 value)> keys, float t)
        {
            if (keys.Count == 1) return keys[0].value;
            if (t <= keys[0].time) return keys[0].value;
            if (t >= keys[keys.Count - 1].time) return keys[keys.Count - 1].value;
            for (int i = 0; i < keys.Count - 1; i++)
            {
                var a = keys[i];
                var b = keys[i + 1];
                if (t >= a.time && t <= b.time)
                {
                    var span = b.time - a.time;
                    var f = span > 1e-6f ? (t - a.time) / span : 0f;
                    return Vector3.Lerp(a.value, b.value, f);
                }
            }
            return keys[keys.Count - 1].value;
        }

        private static Quaternion SampleQuat(List<(float time, Quaternion value)> keys, float t)
        {
            if (keys.Count == 1) return keys[0].value;
            if (t <= keys[0].time) return keys[0].value;
            if (t >= keys[keys.Count - 1].time) return keys[keys.Count - 1].value;
            for (int i = 0; i < keys.Count - 1; i++)
            {
                var a = keys[i];
                var b = keys[i + 1];
                if (t >= a.time && t <= b.time)
                {
                    var span = b.time - a.time;
                    var f = span > 1e-6f ? (t - a.time) / span : 0f;
                    return Quaternion.Slerp(a.value, b.value, f);
                }
            }
            return keys[keys.Count - 1].value;
        }

        // ------------------------------------------------------------------
        // Skinning (CPU, per-frame)
        // ------------------------------------------------------------------

        public class SkinnedBuffers
        {
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Vector4[] Colors;
            public Vector2[] UV0;
            public int[] Indices;
            public bool HasUV;
        }

        // Skins every part in the rig using the bones' current WorldMatrix (call EvaluatePose
        // first). Falls back to rigid-attach (single parent bone, no blending) for meshes with
        // no skin data, so static attachments on an animated rig move with their parent bone.
        //
        // AssetStudio 2 - Feature 7 perf note: this runs every animation frame (up to ~60x/sec).
        // The previous version called Matrix4.Invert once PER VERTEX PER BONE INFLUENCE (up to
        // 4x per vertex) to build the normal matrix, which is by far the most expensive operation
        // here and was being redone for the same bone over and over within a single part. It's
        // now computed once per bone per part per frame and reused across all vertices that
        // reference that bone, which is the same math with orders of magnitude fewer inversions
        // on any reasonably dense skinned mesh. Output buffers are also pre-sized instead of
        // growing List<T> by doubling every frame.
        public static SkinnedBuffers SkinRig(Rig rig, HashSet<long> excludedGameObjectPathIDs = null)
        {
            var totalVerts = 0;
            var totalIndices = 0;
            foreach (var part in rig.Parts)
            {
                if (excludedGameObjectPathIDs != null && part.OwnerGameObject != null &&
                    excludedGameObjectPathIDs.Contains(part.OwnerGameObject.m_PathID))
                    continue;
                totalVerts += part.Mesh.m_VertexCount;
                if (part.Mesh.m_Indices != null) totalIndices += part.Mesh.m_Indices.Count;
            }

            var vertices = new List<Vector3>(totalVerts);
            var normals = new List<Vector3>(totalVerts);
            var colors = new List<Vector4>(totalVerts);
            var uvs = new List<Vector2>(totalVerts);
            var indices = new List<int>(totalIndices);
            bool anyUV = false;

            foreach (var part in rig.Parts)
            {
                if (excludedGameObjectPathIDs != null && part.OwnerGameObject != null &&
                    excludedGameObjectPathIDs.Contains(part.OwnerGameObject.m_PathID))
                    continue;

                var m = part.Mesh;
                int vcount = m.m_VertexCount;
                if (vcount == 0 || m.m_Vertices == null || m.m_Vertices.Length == 0) continue;

                int stride = m.m_Vertices.Length == vcount * 4 ? 4 : 3;
                bool hasNormals = m.m_Normals != null && m.m_Normals.Length > 0;
                int normalStride = hasNormals ? (m.m_Normals.Length == vcount * 4 ? 4 : 3) : 3;
                int uvStride = MeshTextureResolver.GetUVStride(m.m_UV0, vcount);
                bool hasUV = uvStride > 0;
                anyUV |= hasUV;
                bool skinned = part.BoneMap != null && m.m_Skin != null && m.m_Skin.Length >= vcount;

                var rigidMatrix = part.RigidParentBone != null ? part.RigidParentBone.WorldMatrix : Matrix4.Identity;
                var rigidNormalMatrix = Matrix4.Transpose(Matrix4.Invert(rigidMatrix));

                // Precompute each referenced bone's skin matrix and normal matrix exactly once
                // for this part/frame, instead of re-deriving (and re-inverting) it per vertex.
                Matrix4[] boneSkinMatrices = null;
                Matrix4[] boneNormalMatrices = null;
                bool[] boneMatricesValid = null;
                if (skinned)
                {
                    var boneCount = part.BoneMap.Length;
                    boneSkinMatrices = new Matrix4[boneCount];
                    boneNormalMatrices = new Matrix4[boneCount];
                    boneMatricesValid = new bool[boneCount];
                }

                int baseIndex = vertices.Count;
                for (int v = 0; v < vcount; v++)
                {
                    var localPos = new Vector4(m.m_Vertices[v * stride], m.m_Vertices[v * stride + 1], m.m_Vertices[v * stride + 2], 1f);
                    var localNormal = hasNormals
                        ? new Vector4(m.m_Normals[v * normalStride], m.m_Normals[v * normalStride + 1], m.m_Normals[v * normalStride + 2], 0f)
                        : new Vector4(0, 1, 0, 0);

                    Vector3 worldPos;
                    Vector3 worldNormal;

                    if (skinned)
                    {
                        var skin = m.m_Skin[v];
                        var accumPos = Vector4.Zero;
                        var accumNormal = Vector4.Zero;
                        float totalWeight = 0f;
                        for (int j = 0; j < 4; j++)
                        {
                            var w = skin.weight[j];
                            if (w <= 0f) continue;
                            var bi = skin.boneIndex[j];
                            if (bi < 0 || bi >= part.BoneMap.Length) continue;
                            var bone = part.BoneMap[bi];
                            if (bone == null) continue;

                            if (!boneMatricesValid[bi])
                            {
                                var skinMat = part.InverseBindPoses[bi] * bone.WorldMatrix;
                                boneSkinMatrices[bi] = skinMat;
                                boneNormalMatrices[bi] = Matrix4.Transpose(Matrix4.Invert(skinMat));
                                boneMatricesValid[bi] = true;
                            }

                            accumPos += (localPos * boneSkinMatrices[bi]) * w;
                            accumNormal += (localNormal * boneNormalMatrices[bi]) * w;
                            totalWeight += w;
                        }
                        if (totalWeight > 1e-6f)
                        {
                            worldPos = new Vector3(accumPos.X, accumPos.Y, accumPos.Z) / totalWeight;
                            var n3 = new Vector3(accumNormal.X, accumNormal.Y, accumNormal.Z);
                            worldNormal = n3.LengthSquared > 1e-12f ? Vector3.Normalize(n3) : Vector3.UnitY;
                        }
                        else
                        {
                            // Unweighted vertex - fall back to the mesh's own rigid attach point.
                            var p = localPos * rigidMatrix;
                            worldPos = new Vector3(p.X, p.Y, p.Z);
                            var n = localNormal * rigidNormalMatrix;
                            var n3 = new Vector3(n.X, n.Y, n.Z);
                            worldNormal = n3.LengthSquared > 1e-12f ? Vector3.Normalize(n3) : Vector3.UnitY;
                        }
                    }
                    else
                    {
                        var p = localPos * rigidMatrix;
                        worldPos = new Vector3(p.X, p.Y, p.Z);
                        var n = localNormal * rigidNormalMatrix;
                        var n3 = new Vector3(n.X, n.Y, n.Z);
                        worldNormal = n3.LengthSquared > 1e-12f ? Vector3.Normalize(n3) : Vector3.UnitY;
                    }

                    vertices.Add(worldPos);
                    normals.Add(worldNormal);
                    uvs.Add(hasUV ? new Vector2(m.m_UV0[v * uvStride], m.m_UV0[v * uvStride + 1]) : Vector2.Zero);
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

            return new SkinnedBuffers
            {
                Vertices = vertices.ToArray(),
                Normals = normals.ToArray(),
                Colors = colors.ToArray(),
                UV0 = uvs.ToArray(),
                Indices = indices.ToArray(),
                HasUV = anyUV
            };
        }

        // ------------------------------------------------------------------
        // Conversions
        // ------------------------------------------------------------------

        private static Vector3 ToV3(AssetStudio.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
        private static Quaternion ToQuat(AssetStudio.Quaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);

        private static Matrix4 ToMatrix4(AssetStudio.Matrix4x4 m)
        {
            // AssetStudio.Matrix4x4 is column-major (M_row_col); OpenTK's Matrix4 constructor
            // below takes rows, so transpose element-by-element here explicitly rather than
            // guessing at a bulk-copy layout.
            return new Matrix4(
                m.M00, m.M10, m.M20, m.M30,
                m.M01, m.M11, m.M21, m.M31,
                m.M02, m.M12, m.M22, m.M32,
                m.M03, m.M13, m.M23, m.M33);
        }
    }
}
