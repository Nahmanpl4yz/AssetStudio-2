# AssetStudio 2

**AssetStudio 2 (AS2)** is an independent remake of [AssetStudio](https://github.com/Perfare/AssetStudio), focused on improving Unity asset inspection, previewing, decoding, and exporting.

> **Note:** AssetStudio 2 is an independent project and is **not affiliated with, sponsored by, endorsed by, or authorized by Perfare, Unity Technologies, or any of their affiliates.**

**All credits belong to their respective original authors and contributors.**

---

## 🚀 Latest Version — 0.17

The latest release of AssetStudio 2 is **0.17**.

Version 0.17 introduces new mesh reconstruction and audio playback features designed to make extracted Unity assets more useful and easier to inspect.

### 🆕 New Feature: Game Object Mesh Reconstruction

AssetStudio 0.17 can now attempt to **reconstruct the mesh layout used by a GameObject in the original game**.

Instead of exporting individual meshes separately, AssetStudio 2 analyzes related meshes and attempts to combine them into the structure of the original GameObject.

For example, a car in a Unity game might consist of:

```text
Car
├── Body
├── Interior
├── Dashboard
├── Front Wheels
├── Rear Wheels
├── Windows
├── Seats
└── Other Parts
```

AssetStudio 2 can scan the related meshes from the same container or through other available asset relationships and determine how they belong to the main GameObject.

It then attempts to:

* Find related meshes.
* Identify meshes belonging to the same GameObject.
* Preserve their original transforms.
* Align interior components, wheels, body parts, and other meshes.
* Combine them into a reconstructed GameObject mesh.
* Produce a model that more closely resembles the object as it appeared in the original game.

This is particularly useful for complex objects such as **cars, trains, characters, buildings, weapons, and other multi-part GameObjects**.

> **Example:** Instead of receiving a car body, four wheels, interior, and dashboard as completely separate exports, AssetStudio 0.17 can attempt to reconstruct them into the complete car as it was assembled in the Unity game.

Because Unity games can organize assets in many different ways, reconstruction depends on the information available in the game's serialized data. Some GameObjects may therefore require manual handling.

---

### 🆕 New Feature: Audio Playback Speed

AssetStudio 0.17 adds **Audio Playback Speed** controls.

When previewing supported `AudioClip` assets, you can change the playback speed to make inspecting audio easier.

This can be useful for:

* Quickly reviewing long audio files.
* Inspecting sound effects at different speeds.
* Studying game audio.
* Comparing variations of an audio clip.
* Inspecting very short or fast sound effects.

The playback speed affects **preview playback** and does not modify the original audio asset.

---

# ✨ Features

### 🖼️ Improved Texture Decoding

* Better support for problematic and uncommon texture formats.
* Improved texture decoding reliability.
* Switch between the **original decoder** and the **improved decoder**.
* Original decoder behavior remains available for compatibility.

### 🧊 Improved Mesh Preview

* Preview Unity meshes directly inside AssetStudio 2.
* Automatically associate meshes with corresponding textures where possible.
* Preview meshes with textures applied.
* Improved material and texture handling.

### 🧩 GameObject Mesh Reconstruction

* Detect related meshes belonging to a GameObject.
* Reconstruct multi-part GameObjects.
* Preserve object transforms when combining meshes.
* Automatically align components such as wheels, interiors, and other parts.
* Useful for vehicles, characters, buildings, and complex models.

### 📦 Improved Mesh Exporting

* Export meshes together with their associated textures.
* Automatically identify textures used by a mesh where possible.
* Export reconstructed GameObjects.
* Simplifies importing extracted models into external 3D software.

### 🔊 Audio Preview

* Preview supported Unity `AudioClip` assets.
* Adjustable audio playback speed.
* Original audio files remain unchanged.

### 🛠️ General Improvements

* Additional bug fixes and stability improvements.
* Improved handling of assets that may not work correctly in the original AssetStudio.
* Retains much of the original AssetStudio workflow and functionality.

---

# 📊 AssetStudio 2 vs. AssetStudio

| Feature                              |          AssetStudio          |                     AssetStudio 2                    |
| ------------------------------------ | :---------------------------: | :--------------------------------------------------: |
| Browse Unity assets                  |               ✅               |                           ✅                          |
| Extract AssetBundles                 |               ✅               |                           ✅                          |
| Texture export                       |               ✅               |                           ✅                          |
| Sprite export                        |               ✅               |                           ✅                          |
| Mesh export                          |               ✅               |                           ✅                          |
| Audio export                         |               ✅               |                           ✅                          |
| Animation export                     |               ✅               |                           ✅                          |
| Improved texture decoders            |               ❌               |                           ✅                          |
| Original / improved decoder switch   |               ❌               |                           ✅                          |
| Automatic mesh ↔ texture association |        Limited / Manual       |                           ✅                          |
| Textured mesh previews               |            Limited            |                      ✅ Improved                      |
| Mesh + texture export                |            Limited            |                           ✅                          |
| **GameObject mesh reconstruction**   |               ❌               |                   ✅ **New in 0.17**                  |
| **Multi-part mesh alignment**        |               ❌               |                   ✅ **New in 0.17**                  |
| **Audio playback speed**             |               ❌               |                   ✅ **New in 0.17**                  |
| Problematic texture formats          |          More limited         |                      ✅ Improved                      |
| Additional bug fixes                 |    Original implementation    |                           ✅                          |
| Original decoder behavior            |               —               |                           ✅                          |
| **Primary focus**                    | Asset inspection & extraction | **Improved inspection, reconstruction & extraction** |

---

# 📋 Supported Unity Versions

AssetStudio 2 is based on the original AssetStudio architecture and aims to support the same general range of Unity versions:

**Unity 3.4 – 2022.1**

> Support for individual Unity versions and asset formats may vary depending on the assets and Unity features used by a particular game.

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
  * Reconstructed GameObject meshes
* **TextAsset**
* **Shader**
* **MovieTexture**
* **VideoClip**
* **MonoBehaviour**

  * JSON
* **Animator**

  * FBX
  * AnimationClip support

---

# 💻 Requirements

### AssetStudio.net472

* **.NET Framework 4.7.2**

Download:

[.NET Framework 4.7.2](https://dotnet.microsoft.com/download/dotnet-framework/net472)

---

# 🚀 Usage

## Loading Assets

Use:

**File → Load file**

or:

**File → Load folder**

AssetStudio 2 can load Unity asset files and AssetBundles for inspection and extraction.

### AssetBundles

When AssetBundles are loaded directly, AssetStudio loads and decompresses their contents into memory. Large AssetBundles can therefore consume a significant amount of RAM.

If memory usage becomes an issue, use:

**File → Extract file**

or:

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

# 🧩 GameObject Mesh Reconstruction

AssetStudio 0.17 introduces automatic reconstruction of complex GameObjects.

When possible, AssetStudio 2 analyzes the relationships between meshes, GameObjects, transforms, containers, and other serialized data.

For example:

```text
Vehicle
├── Main Body
├── Interior
├── Dashboard
├── Steering Wheel
├── Wheel FL
├── Wheel FR
├── Wheel RL
├── Wheel RR
└── Other Components
```

AssetStudio 2 attempts to determine how these meshes were positioned in the original GameObject and reconstruct them accordingly.

The result is intended to more closely represent the **complete object as it existed inside the Unity game**, rather than a collection of unrelated individual meshes.

### Reconstruction Process

Conceptually, the process is:

```text
Unity Assets
     ↓
Find related GameObjects
     ↓
Find associated meshes
     ↓
Read transforms
     ↓
Identify relationships
     ↓
Align components
     ↓
Reconstruct GameObject
     ↓
Export complete model
```

The exact results depend on how the game stores its GameObjects and assets.

---

# 🔊 Audio Playback

AssetStudio 0.17 adds playback-speed controls for supported `AudioClip` previews.

This allows you to inspect audio at different speeds without changing the original asset.

**Playback speed is a preview feature only.**

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

# 🎯 Project Goals

AssetStudio 2 aims to become a more capable and convenient Unity asset inspection and extraction tool while preserving the simplicity that made the original AssetStudio useful.

The project's main goals are:

* 🔍 **Better asset inspection**
* 🖼️ **Better texture decoding**
* 🧊 **Better mesh visualization**
* 🧩 **GameObject reconstruction**
* 🎨 **Automatic texture/material association**
* 📦 **Simpler model + texture extraction**
* 🔊 **Improved audio inspection**
* 🛠️ **Improved compatibility and stability**
* 🔄 **Preservation of original AssetStudio behavior where possible**

---

# 📌 Version History

## 0.17 — Latest

### New Features

* **GameObject Mesh Reconstruction**

  * Combines related meshes into the GameObject structure used by the original game.
  * Scans related containers and asset relationships for additional meshes.
  * Aligns components such as interiors, wheels, and other parts with the main mesh.
  * Designed for complex multi-part objects such as cars and other vehicles.

* **Audio Playback Speed**

  * Allows supported audio previews to be played at different speeds.
  * Does not modify the original audio asset.

### Improvements

* Improved asset handling.
* Additional mesh and texture association improvements.
* Additional bug fixes and stability improvements.

---

## AssetStudio 2

This is a continuation of the AssetStudio concept, built for easier Unity asset exploration.
