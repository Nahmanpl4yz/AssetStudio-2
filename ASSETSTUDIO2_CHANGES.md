# AssetStudio 2 — changes from upstream Perfare/AssetStudio

This is a source patch on top of the official `Perfare/AssetStudio` repo (cloned at commit
`d158e86`). It has **not been compiled** — this environment has no Windows/MSVC/.NET Framework
toolchain, and this solution mixes .NET Framework 4.7.2 C# with native C++ (vcxproj) projects
that can only be built on Windows with Visual Studio. Open `AssetStudio.sln` in Visual Studio
2022 (with the ".NET desktop development" and "Desktop development with C++" workloads) and
build the `AssetStudioGUI` project (x64, Release) as normal.

## 1. Better / Original decoder mode (Options → Decoder mode)

Stock AssetStudio always uses the bundled native `Texture2DDecoderNative.dll` to decode
block-compressed textures. That library:
- **never implements BC2/DXT3 at all** — `Texture2DConverter.DecodeTexture2D`'s `DXT3` case was
  a bare `break;`, so any DXT3 texture silently rendered as solid black. This is a real bug,
  not a design choice, and is now fixed **in both modes** since there's no native fallback to
  preserve for a format that was never implemented.
- interpolates BC1/BC3/BC4/BC5 endpoint colors with truncating integer math, which visibly
  darkens/bands smooth gradients versus the color ramp the GPU/Unity itself produces.

`AssetStudioUtility/BetterBCnDecoder.cs` is a new managed decoder for BC1 (DXT1), BC2 (DXT3),
BC3 (DXT5), BC4 and BC5 that uses the exact rounded 1/3–2/3 interpolation from the D3D/OpenGL
spec, and correctly decodes BC2's explicit 4-bit alpha block. `Texture2DConverter.Mode`
(`DecoderMode.Better` / `DecoderMode.Original`) switches between it and the native decoder for
those five formats; everything else (ETC/PVRTC/ASTC/ATC/BC6H/BC7/Crunch) still uses the native
decoder in both modes — those weren't reimplemented in this pass. The setting is persisted
(`Properties.Settings.Default.decoderMode`) and exposed as a real menu under
**Options → Decoder mode → Better (recommended) / Original**.

## 2. Textured mesh preview (Options → "Show texture on mesh preview")

The OpenGL mesh preview previously only had position/normal/vertex-color shading — no UVs, no
texture support at all. Since a raw `Mesh` asset has no direct pointer to a `Texture2D` (only
the `GameObject` → `MeshRenderer`/`SkinnedMeshRenderer` → `Material` → texture chain has one),
a new `AssetStudioUtility/MeshTextureResolver.cs` scans the loaded assets for the GameObject
that owns a given mesh and returns its material(s)/texture(s). `PreviewMesh` now builds a UV
vertex buffer from `m_Mesh.m_UV0`, resolves and decodes the mesh's main texture through the
normal `Texture2DConverter` pipeline, uploads it to a new GL texture, and a new shader pair
(`vsTex`/`fsTex`) renders the mesh with that texture applied (still respecting wireframe/shaded
toggles). Toggleable and persisted.

## 3. Export Mesh + its texture(s) as one model (Options → "Export mesh with textures")

`Exporter.ExportMesh` (used when exporting a raw `Mesh` asset directly, not via
"Export selected objects" which already went through the FBX pipline) previously wrote a bare,
untextured `.obj`. It now uses the same `MeshTextureResolver` to find the mesh's material(s),
writes an accompanying `.mtl` (`mtllib`/`usemtl` in the `.obj`) and exports each referenced
texture as a `.png` next to it (`map_Kd` for `_MainTex`, `map_Bump` for `_BumpMap`), so the
mesh and its texture come out as one openable, textured 3D model in Blender/any OBJ-aware tool.
Falls back to the old bare-OBJ behavior automatically if no owning GameObject/material can be
found, or if the option is turned off.

## 4. Other fixes

- **DXT3/BC2 decoding** — see #1; this was a genuine, silent bug (all-black textures), not a
  stylistic difference.
- **GPU buffer leak in the mesh preview** — `CreateVAO()` deleted the old vertex array object
  on every mesh selection but never deleted the old VBOs/EBO it had allocated with
  `GL.GenBuffers`, so browsing many meshes in one session steadily leaked native GPU memory.
  It now tracks and deletes the previous mesh's buffers before allocating new ones.
- **Mesh-preview texture leak** — the new textured-preview GL texture is released before a new
  one is uploaded (and when texture preview is unavailable/disabled), instead of accumulating
  one GPU texture per mesh click.

## Not done in this pass (flagged, not silently skipped)

- "Better" decoders only cover BC1/BC2/BC3/BC4/BC5. ETC1/2, PVRTC, ASTC, ATC, BC6H/BC7 and
  Crunch still use the native decoder either way — correctly reimplementing those is
  substantially more work than fit in this pass.
- Multi-texture / multi-material combined export handles `_MainTex` and `_BumpMap`; other
  texture slots (specular, emission, etc.) are exported as files but not wired into the `.mtl`.
- None of this has been compiled or tested in a Windows environment — please build and sanity
  check before relying on it.
