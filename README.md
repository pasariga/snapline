[English](README.md) | [简体中文](README.zh-CN.md)

# Snapline — personal Windows edition

A screenshot shelf presented as a clothesline at the top of the desktop. Capture an image, keep it within reach, then copy, annotate, arrange or drag it into another application.

This repository maintains **pasariga's Windows modifications** to [Snapline by Mohammed Abu Jubara](https://github.com/mohabujubara/snapline), itself derived from [Tendedero by Alejandro Buján Pampín](https://github.com/alejandrobujan/tendedero). The clothesline concept, core workflow, existing artwork and many tools come from upstream. The personal changes focus on rendering, interaction and capture lifecycle behavior.

![Snapline interface](docs/hero-light.png)

*Existing artwork inherited from upstream, illustrating the interface. It is not a benchmark or a recording of this edition.*

## Main features

The inherited workflow includes screenshot-folder and image-only clipboard monitoring, click-to-copy, drag-and-drop, markup with undo and original-image recovery, sticky notes, Windows OCR, card arrangement, themes, configurable shortcuts and multiple-monitor/DPI handling. Additional capture folders can be selected in Settings. OCR languages depend on Windows language support.

Captures are processed locally. Running this app does not require a service account or an API key. The application interface retains the upstream English/Arabic language options; a Chinese README does not add a Chinese UI.

## What this edition changes

The changes are grounded in [commit b732828](https://github.com/pasariga/snapline/commit/b7328285f9b5a846a09913934678e898581d9137), compared with its upstream parent `6a1f242`.

| Area | Implemented change |
| --- | --- |
| Capture animation | Flight duration changes from **0.65 s to 0.44 s**, with a **0.075 s** fade. DirectComposition timelines, cached shadows and peg imagery provide a native rendering path with WPF fallback. These are configured durations, not measured FPS or a percentage speedup. |
| Auto-hide delay | The inherited 0.5 s delay becomes configurable, with a **2.5 s** default and **0.5–30 s** range. A monotonic clock tracks the leave deadline. |
| Interaction | Re-entering the line cancels the countdown. Card press/drag, rope drag, open menus, editing/settings windows and clear confirmation prevent timeout-based hiding. After interaction ends and the pointer leaves, a fresh delay begins. |
| Card removal | A removed card's empty slot no longer triggers immediate hiding. Clear-all requires confirmation and performs one collection update instead of overlapping fall animations. |
| Panel motion | Cached native panel snapshots support rapid transition reversal from the visible position, with WPF fallback. |
| Fullscreen detection | Captioned or maximized windows are distinguished from borderless fullscreen windows, avoiding suppression merely because an ordinary app fills the monitor. |
| Capture lifecycle | Hanging state follows the Windows logon session and local date. Eligible managed captures are recycled only after recorded ownership, directory and SHA-256 checks; external or replaced files are retained. |
| Image quality | Animation previews are separate from original files. Optional compact previews are off by default; copy/open/edit paths still read the original capture. |

Explicit hide commands and fullscreen suppression remain separate from the auto-hide countdown. Source locations and diagnostic limits are explained in the bilingual [Windows implementation notes](docs/WINDOWS-CHANGES.md).

## Everyday use

1. Launch `Snapline.exe`; it lives in the notification area.
2. Take a screenshot using Windows Snipping Tool or a configured capture tool.
3. Rest the pointer at the top edge, click the tray icon, or press **Ctrl+Alt+T** to reveal the line. The inherited top-edge reveal delay is 0.5 s by default, separate from the auto-hide delay.
4. Click to copy, hold to annotate, double-click to open, or drag a card to an application or folder. The tray/line menu provides new captures, notes and Settings.
5. Adjust the auto-hide delay in Settings. Interaction cancels the timer; leaving starts a new full countdown.

Settings, logs and managed captures default to `%LOCALAPPDATA%\Snapline`. Back up this directory before replacing an installation. **Save important captures and notes outside the managed folder:** a new logon session or calendar day clears hanging state and may recycle eligible app-owned files. Keeping a card on the line is not a permanent archive.

## Install the modified version

As of **2026-10-24**, this repository has no published GitHub Release. Upstream packages do **not** include these modifications. Check [this repository's releases](https://github.com/pasariga/snapline/releases) for future packages; for now, build this repository's source.

Requirements: Windows 10 version 1809 or later / Windows 11, x64, **.NET 8 SDK**, and network access for NuGet restore. The target framework is `net8.0-windows10.0.19041.0`, with declared minimum platform version `10.0.17763.0`. Visual Studio is optional.

```powershell
git clone https://github.com/pasariga/snapline.git
cd snapline
dotnet restore src/Snapline/Snapline.csproj
dotnet publish src/Snapline/Snapline.csproj -c Release -r win-x64 --self-contained true -o dist/windows
.\dist\windows\Snapline.exe
```

Keep the **entire** `dist/windows` directory together; this build includes its runtime. Alternatively, `scripts\build.ps1 -Portable` creates a self-contained single file in `dist/portable`. Running `scripts\build.ps1` without that switch creates a framework-dependent build requiring the .NET 8 Desktop Runtime; `-Installer` also requires Inno Setup 6. This documentation change does not publish binaries.

The source currently records version `1.6.2-local.20261016.2`. The inherited `macos/` directory contains Swift/SwiftUI source; the Windows modifications do not establish equivalent macOS behavior or validation.

## Development and verification

C#, WPF and Win32 implement the desktop UI; Vortice bindings expose Direct3D, Direct2D and DirectComposition. [LOCAL-DEVELOPMENT.md](LOCAL-DEVELOPMENT.md) contains clean-build and isolated diagnostic commands.

Existing behavior and animation diagnostics exercise synthetic interaction sequences, image integrity and rendering instrumentation. Their presence does not imply a particular machine has passed them. Callback/submit timing is not monitor-displayed FPS. This documentation revision claims no new benchmark results or test runs; actual reboot behavior, hardware-specific rendering and macOS compatibility require separate validation.

## Origin and license

This is a personal derivative, not the upstream distribution. Git history preserves upstream commits and the personal modification commit.

- Upstream Snapline: [mohabujubara/snapline](https://github.com/mohabujubara/snapline), by Mohammed Abu Jubara and contributors.
- Original concept and derived portions: [Tendedero](https://github.com/alejandrobujan/tendedero), by Alejandro Buján Pampín.
- [MIT license](LICENSE): copyright 2026 Snapline contributors; derived portions retain copyright 2025 Alejandro Buján Pampín. The license text is unchanged.
- Manrope retains the [SIL Open Font License](src/Snapline/Assets/Fonts/OFL.txt), also included with the [design font](design/fonts/OFL.txt).

Existing screenshots and demo assets remain part of the inherited materials. Attribution applies to the interface concept and inherited features as well as the code.
