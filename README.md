FModel - An Unreal Engine Archives Explorer in C#
------------------------------------------

[![CI Status](https://img.shields.io/github/actions/workflow/status/4sval/FModel/qa.yml?label=CI)](https://github.com/4sval/FModel/actions)
[![Latest](https://img.shields.io/github/v/release/4sval/FModel?color=yellow)](https://fmodel.app/download)
[![Donate](https://img.shields.io/badge/sponsor-DB61A2?logo=GitHub-Sponsors&logoColor=white)](https://fmodel.app/donate)
[![Discord](https://discord.com/api/guilds/637265123144237061/widget.png?style=shield)](https://fmodel.app/discord)
***

### Description:
FModel is an archive explorer for [Unreal Engine](https://www.unrealengine.com/en-US/) games that uses [CUE4Parse](https://github.com/FabianFG/CUE4Parse) as its core parsing library, providing robust support for the latest UE4 and UE5 archive formats. It aims to deliver a modern and intuitive user interface, powerful features, and a comprehensive set of tools for previewing and converting game packages, empowering YOU to understand games' inner workings with ease.

FModel is actively maintained and developed by a dedicated community of contributors, and welcomes all new contributions and feedback.

### Installation:
For installation, follow the instructions from [here](https://github.com/4sval/FModel/wiki/Installing-FModel)

### Linux (native):
This fork runs natively on Linux (X11 and Wayland via XWayland) — the UI was ported from WPF to [Avalonia](https://avaloniaui.net/), everything else (CUE4Parse, 3D viewer, audio player, exports) runs as on Windows.

**Requirements:** a 64-bit Linux distribution, OpenGL 4.6 capable GPU drivers (3D viewer), `libopenal1` / `openal-soft` (audio player), and optionally `java` (Lua decompilation). Oodle, zlib-ng and vgmstream are downloaded automatically on first start, like on Windows.

**AppImage (recommended):** download `FModel-x86_64.AppImage` from the [Linux release](../../releases/tag/linux-latest) (built automatically by GitHub Actions on every push), then:
```bash
chmod +x FModel-x86_64.AppImage
./FModel-x86_64.AppImage
```

**Build:**
```bash
git clone --recursive <this repository>
cd FModel
./linux/build-appimage.sh # AppImage, needs the .NET 10 SDK, cmake and g++
./linux/build.sh          # or a plain self-contained binary
./FModel/bin/Publish/linux-x64/install.sh   # optional: installs to ~/.local with a desktop entry
```
Or run it directly from source: `./linux/apply-patches.sh && dotnet run --project FModel -c Release` (the build scripts apply the Linux fixes in `linux/patches` to the CUE4Parse submodule automatically).

Settings are stored in `~/.config/FModel`. The output folder is `~/.local/share/FModel/Output` for the AppImage, and `Output/` next to the executable for other builds (or `~/.local/share/FModel/Output` if that location is read-only). Games are auto-detected from Steam libraries and from Heroic / Legendary (Epic Games) installations.

### Sponsorship:
<p>
  <a href="https://1password.com/">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="https://cdn.fmodel.app/i/svg/1password-light.svg">
      <source media="(prefers-color-scheme: light)" srcset="https://cdn.fmodel.app/i/svg/1password-dark.svg">
      <img src="https://cdn.fmodel.app/i/svg/1password-light.svg" width="256px">
    </picture>
  </a>
</p>

### License:
FModel is licensed under [GPL-3](https://github.com/4sval/FModel/blob/dev/LICENSE), and licenses of third-party libraries used are listed [here](https://github.com/4sval/FModel/blob/dev/NOTICE).