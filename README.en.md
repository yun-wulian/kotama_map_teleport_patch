# KotamaAcademyCitadel Map Teleport Patch (IL2CPP / BepInEx6)

[中文说明](README.md)

This repository provides a **BepInEx IL2CPP plugin** for *KotamaAcademyCitadel* that allows:

- On the **map UI**, when your cursor is pointing at a savepoint-related mark, pressing **SPACE** will **teleport** you to that mark.

## Features

- Teleport from the map cursor to “relay/checkpoint” type marks.
- Closes the InGameMenu after teleport (if any UI remains, press `ESC` once to exit normally).

## Notes (important)

- The trigger key depends on the game’s map input mapping. This mod hooks the “confirm / place mark” type action that is commonly bound to `Space`.
- Teleport only triggers on savepoint-related marks; other map marks keep the original behavior.

## Requirements

- KotamaAcademyCitadel (Unity 2022.3, IL2CPP)
- **BepInEx 6 (IL2CPP build)**
  - Official Releases (stable): https://github.com/BepInEx/BepInEx/releases
  - BepInEx build site (recommended for IL2CPP bleeding-edge builds): https://builds.bepinex.dev/
  - Project page (bepinex_be): https://builds.bepinex.dev/projects/bepinex_be
  - Known-good build (Windows x64 / IL2CPP metadata v31):
    - `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.752+dd0655f.zip`
    - https://builds.bepinex.dev/projects/bepinex_be/752/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.752%2Bdd0655f.zip

## Install

1. Install BepInEx 6 (IL2CPP).
2. Get the plugin DLL (build it yourself or download from Releases): `Kotama.MapTeleport.dll`
3. Copy it to: `KotamaAcademyCitadel\\BepInEx\\plugins\\Kotama.MapTeleport.dll`
4. Launch the game.

## Usage

1. Open the map UI.
2. Move the cursor to a savepoint-related mark (e.g. a relay/checkpoint mark).
3. Press `Space` to teleport.

## Build (recommended)

This project references `BepInEx/core` and `BepInEx/interop` assemblies via relative paths, so the easiest setup is keeping this repo under the game folder.

1. Place/clone into:
   - `...\\KotamaAcademyCitadel\\Modding\\kotama_map_teleport_patch\\`
2. Build:
   - `dotnet build .\\kotama_map_teleport_patch\\KotamaMapTeleport.csproj -c Release`
3. Output:
   - `.\\kotama_map_teleport_patch\\bin\\Release\\net6.0\\Kotama.MapTeleport.dll`

## Notes

- This repo does not commit any game files, BepInEx files, or build outputs; download prebuilt DLLs from GitHub Releases.

