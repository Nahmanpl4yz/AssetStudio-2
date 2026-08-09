# AssetStudio 2

**AssetStudio 2 (AS2)** is an independent remake of [AssetStudio](https://github.com/Perfare/AssetStudio), focused on improving Unity asset inspection, previewing, decoding, and exporting.

AssetStudio 2 aims to provide a more complete asset-extraction workflow while retaining the familiar functionality and behavior of the original AssetStudio.

> **Note:** AssetStudio 2 is an independent project and is **not affiliated with, sponsored by, endorsed by, or authorized by Perfare, Unity Technologies, or any of their affiliates.**

**All credits belong to their respective original authors and contributors.**

---

## ✨ What's New in AssetStudio 2?

AssetStudio 2 builds upon the original AssetStudio experience with several improvements to asset handling and visualization.

### 🖼️ Improved Texture Decoding

* Better support for problematic and uncommon texture formats.
* Improved texture decoding reliability.
* Ability to switch between the **original decoder** and the **improved decoder**.
* Original decoder behavior remains available for compatibility.

### 🧊 Improved Mesh Preview

* Preview Unity meshes directly inside AssetStudio 2.
* Automatically associate meshes with their corresponding textures where possible.
* Preview meshes with their textures applied.
* Improved material and texture handling.

### 📦 Improved Mesh Exporting

* Export meshes together with their associated textures.
* Automatically identify textures used by a mesh where possible.
* Simplifies the process of importing extracted models into external 3D software.

### 🛠️ General Improvements

* Additional bug fixes and stability improvements.
* Improved handling of assets that may not work correctly in the original AssetStudio.
* Retains much of the original AssetStudio workflow and functionality.

---

# AssetStudio 2 vs. AssetStudio

| Feature                              |          AssetStudio          |                      AssetStudio 2                     |
| ------------------------------------ | :---------------------------: | :----------------------------------------------------: |
| Browse Unity assets                  |               ✅               |                            ✅                           |
| Extract AssetBundles                 |               ✅               |                            ✅                           |
| Texture export                       |               ✅               |                            ✅                           |
| Sprite export                        |               ✅               |                            ✅                           |
| Mesh export                          |               ✅               |                            ✅                           |
| Audio export                         |               ✅               |                            ✅                           |
| Animation export                     |               ✅               |                            ✅                           |
| Improved texture decoders            |               ❌               |                            ✅                           |
| Original / improved decoder switch   |               ❌               |                            ✅                           |
| Automatic mesh ↔ texture association |        Limited / Manual       |                            ✅                           |
| Textured mesh previews               |            Limited            |                       ✅ Improved                       |
| Mesh + texture export                |            Limited            |                            ✅                           |
| Problematic texture formats          |          More limited         |                       ✅ Improved                       |
| Additional bug fixes                 |    Original implementation    |                            ✅                           |
| Original decoder behavior            |               —               |                            ✅                           |
| **Primary focus**                    | Asset inspection & extraction | **Improved asset inspection, previewing & extraction** |

---

# 📋 Supported Unity Versions

AssetStudio 2 is based on the original AssetStudio architecture and aims to support the same general range of Unity versions:

**Unity 3.4 – 2022.1**

> Support for individual Unity versions and asset formats may vary depending on the asset and Unity features used by a particular game.

---

# 📦 Supported Asset Types

AssetStudio 2 supports the asset types available in the original AssetStudio, including:

* **Texture2D**

  * PNG
  * TGA
  * JPEG
  * BMP
* **Sprite**

  * PNG
  * TGA
  * JPEG
  * BMP
* **AudioClip**

  * MP3
  * OGG
  * WAV
  * M4A
  * FSB
* **Font**

  * TTF
  * OTF
* **Mesh**

  * OBJ
  * Additional model export functionality
* **TextAsset**
* **Shader**
* **MovieTexture**
* **VideoClip**
* **MonoBehaviour**

  * JSON
* **Animator**

  * FBX
  * AnimationClip support

Additional support may vary depending on the Unity version and asset configuration.

---

# 💻 Requirements

AssetStudio 2 currently targets:

### AssetStudio.net472

* **.NET Framework 4.7.2**

Download:

[.NET Framework 4.7.2](https://dotnet.microsoft.com/download/dotnet-framework/net472)

---

# 🚀 Usage

## Loading Assets

Use:

**File → Load file**

or

**File → Load folder**

AssetStudio 2 can load Unity asset files and AssetBundles for inspection and extraction.

### AssetBundles

When AssetBundles are loaded directly, AssetStudio loads and decompresses their contents into memory. Large AssetBundles can therefore consume a significant amount of RAM.

If memory usage becomes an issue, use:

**File → Extract file**

or

**File → Extract folder**

to extract the AssetBundles first, then load the extracted files.

---

# 📤 Exporting Assets

Select the assets you want to export and use the:

**Export**

menu.

Depending on the asset type, AssetStudio 2 can export textures, sprites, audio, meshes, animations, fonts, and other supported assets.

---

# 🧊 Exporting Models

Models can be exported from the:

**Scene Hierarchy**

using the **Model** menu.

Animator assets can be exported from the:

**Asset List**

using the **Export** menu.

### AnimationClip Export

To export a model with an AnimationClip:

1. Select the model in **Scene Hierarchy**.
2. Select the desired AnimationClip in **Asset List**.
3. Use:
   **Model → Export selected objects with AnimationClip**

Alternatively, Animator assets can be exported with selected AnimationClips through:

**Export → Export Animator with selected AnimationClip**

Hold **Ctrl** to select multiple compatible assets when necessary.

---

# 🔍 MonoBehaviour Export

When selecting a `MonoBehaviour` asset for the first time, AssetStudio 2 may ask for the location of the game's assemblies.

Select the directory containing the game's assemblies, commonly:

```text
<Game Folder>/
└── Managed/
    ├── Assembly-CSharp.dll
    ├── UnityEngine.dll
    └── ...
```

This allows AssetStudio 2 to resolve serialized MonoBehaviour information and export it as JSON where supported.

---

# 🔧 IL2CPP Games

For IL2CPP-based Unity games, the required managed assemblies may not be present in their original form.

A common workflow is to first generate dummy assemblies using **Il2CppDumper**, then provide the generated DLL directory to AssetStudio 2 when prompted.

> AssetStudio 2 does not itself convert IL2CPP binaries into managed assemblies.

---

# 🏗️ Building From Source

## Requirements

* Visual Studio 2022 or newer
* .NET Framework 4.7.2
* FBX SDK 2020.2.1 for the native FBX exporter

### FBX SDK

The `AssetStudioFBXNative` project uses:

**Autodesk FBX SDK 2020.2.1**

Before building, install the FBX SDK and configure the project to point to the appropriate:

* Include directory
* Library directory

The exact paths depend on your local FBX SDK installation.

---

# 📚 Open-Source Libraries

AssetStudio 2 uses or is based upon work from several open-source projects.

### Texture2DDecoder

* [Ishotihadus/mikunyan](https://github.com/Ishotihadus/mikunyan)
* [BinomialLLC/crunch](https://github.com/BinomialLLC/crunch)
* [Unity-Technologies/crunch](https://github.com/Unity-Technologies/crunch/tree/unity)

Please refer to the respective projects for their licenses, authors, and attribution requirements.

---

# 📜 Credits & Attribution

AssetStudio 2 is a remake based on the work and ideas of the original **AssetStudio** project by **Perfare**.

This project is **not affiliated with, sponsored by, endorsed by, or authorized by Perfare or Unity Technologies**.

All third-party libraries, code, and technologies remain the property of their respective authors and are subject to their respective licenses.

If you use or redistribute components from this project, please ensure that you comply with all applicable third-party licenses.

---

# ⚠️ Disclaimer

AssetStudio 2 is provided as an independent, community-developed project.

The developers of AssetStudio 2 are not responsible for how the software is used.

**Do not use AssetStudio 2 to infringe copyrights, violate software licenses, or extract assets from software without appropriate permission.**

---

## AssetStudio 2

This is a continuation of the AssetStudio concept, built for easier Unity asset exploration.
