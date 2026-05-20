# CampfireBugFix

A mod for **Slay the Spire 2** that fixes focus and navigation bugs on campfire card selection screens (Upgrade, Transform, Card Select, and Enchant), and patches card removal logic for merchants and rewards.

## What It Fixes

- **Campfire screen focus bugs** — After selecting a card on Upgrade/Transform/Card Select/Enchant screens, focus would be lost or misrouted. This mod ensures the grid refocuses correctly and keyboard/gamepad navigation continues working.
- **Merchant card removal** — Patches `OneOffSynchronizer.DoMerchantCardRemoval` to allow multi-card removal.
- **Reward card removal** — Patches `RewardSynchronizer.DoCardRemoval` to allow multi-card removal from rewards.

## Prerequisites

- **Godot Editor** 4.5+ with .NET support ([download](https://godotengine.org/download/windows/))
- **.NET 9.0 SDK** ([download](https://dotnet.microsoft.com/en-us/download/dotnet/9.0))
- A copy of `sts2.dll` from your Slay the Spire 2 installation

## Build Instructions

### 1. Set up `sts2.dll`

Copy `sts2.dll` from your Slay the Spire 2 game installation into the root of this project:

**Windows:**
```
copy "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\sts2.dll" .
```

**Linux (native):**
```bash
cp "$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2/sts2.dll" .
```

**Linux (Steam Play / Proton):**
If running through Proton, the path uses the Steam library's `compatdata`:
```bash
# Find the game's compatdata directory (the App ID for Slay the Spire 2)
cp "<steam-library>/steamapps/compatdata/<appid>/pfx/drive_c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/sts2.dll" .
```

This file is referenced by the project and is required for compilation. It is **not** included in the repository.

### 2. Build the C# DLL

Open the project in Godot Editor. The editor will generate the `.godot` directory and restore NuGet packages (including HarmonyLib). Then build the C# project:

```bash
dotnet build
```

This produces `CampfireBugFix.dll` in `.godot/mono/temp/bin/Debug/`.

### 3. Pack the PCK

The mod includes a GDScript-based packer. Run it headlessly from Godot:

**Windows:**
```
"C:\Path\To\Godot_v4.5-stable_win64_console.exe" --headless --script tools\build_pck.gd
```

**Linux:**
```bash
# Standard Godot installation or self-contained binary
godot --headless --script tools/build_pck.gd

# or with explicit path:
"/path/to/Godot_v4.5-stable_linux.x86_64" --headless --script tools/build_pck.gd
```

This reads the manifest (`CampfireBugFix.json`), the mod image, and all files under `CampfireBugFix/`, then writes `build/CampfireBugFix.pck`.

### 4. Assemble the release folder

Create a release folder with the following structure:

```
CampfireBugFix/
├── CampfireBugFix.json
├── CampfireBugFix.pck
└── CampfireBugFix.dll
```

Copy the files:

**Windows (PowerShell):**
```powershell
mkdir build\CampfireBugFix
copy CampfireBugFix.json build\CampfireBugFix\
copy build\CampfireBugFix.pck build\CampfireBugFix\
copy .godot\mono\temp\bin\Debug\CampfireBugFix.dll build\CampfireBugFix\
```

**Linux:**
```bash
mkdir -p build/CampfireBugFix
cp CampfireBugFix.json build/CampfireBugFix/
cp build/CampfireBugFix.pck build/CampfireBugFix/
cp .godot/mono/temp/bin/Debug/CampfireBugFix.dll build/CampfireBugFix/
```

The entire `CampfireBugFix/` folder under `build/` is the installable mod.

## Installation

1. Locate your Slay the Spire 2 mods directory:

   **Windows:**
   ```
   %USERPROFILE%\AppData\LocalLow\Mega Crit\Slay the Spire 2\Mods
   ```

   **Linux:**
   ```
   $HOME/.local/share/Steam/steamapps/compatdata/<appid>/pfx/drive_c/users/steamuser/AppData/LocalLow/Mega Crit/Slay the Spire 2/Mods
   ```
   Replace `<appid>` with the Slay the Spire 2 Steam App ID.

   If the `Mods` folder does not exist, create it.

2. Copy the `CampfireBugFix` folder into the `Mods` directory. The folder should contain:

   ```
   CampfireBugFix/
   ├── CampfireBugFix.json
   ├── CampfireBugFix.pck
   └── CampfireBugFix.dll
   ```

3. Launch Slay the Spire 2. The mod will be loaded automatically if the game's mod system detects the manifest.

## Uninstallation

Delete the `CampfireBugFix` folder from the `Mods` directory.