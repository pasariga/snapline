<p align="center"><a href="README.md">English</a> · <a href="README.zh-CN.md">简体中文</a></p>
<p align="center"><img src="docs/logo-512.png" width="76" alt="Snapline logo"></p>
<h1 align="center">Snapline</h1>
<p align="center"><strong>A screenshot shelf for Windows.</strong><br>Take a screenshot and keep it within reach at the top of your screen.</p>
<p align="center">
  <a href="https://github.com/pasariga/snapline/releases"><img src="https://img.shields.io/badge/Windows_x64-Downloads-2d5be3?style=for-the-badge" alt="Windows x64 downloads"></a>
</p>
<p align="center">
  <a href="https://github.com/pasariga/snapline/actions/workflows/release.yml"><img src="https://github.com/pasariga/snapline/actions/workflows/release.yml/badge.svg?branch=main" alt="Windows package build"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-426b58" alt="MIT license"></a>
  · <a href="#get-started">Quick start</a> · <a href="https://github.com/pasariga/snapline/issues/new/choose">Feedback</a>
</p>

![Screenshot cards hanging from Snapline's desktop clothesline](docs/hero-light.png)

*Snapline interface artwork from the upstream project. [Watch the upstream demo](docs/demo.mp4).*

Say you’re writing a bug report, collecting slides from a lecture, or grabbing a reference for a chat. Snapline hangs each new capture on a clothesline at the top of your screen. Move your pointer to the top edge to bring it back, then copy or drag the screenshot straight into the app you’re using.

## What you can do

These features come from [upstream Snapline](https://github.com/mohabujubara/snapline).

- **Capture and collect.** Use Windows Snipping Tool or another capture tool. Snapline watches the Windows Screenshots folder and can catch images copied to the clipboard. Add folders for other tools in Settings.
- **Copy, drag, or organize.** Click a card to copy the image, drag it into a compatible app or folder, or move it along the line. Add sticky notes from the menu.
- **Mark up and read text.** Hold a card to annotate it, or right-click and choose **Copy text** to extract text with Windows OCR. The available OCR languages depend on Windows.

## What changed in this Windows edition

- **Capture and shelf animations** use a Windows compositor path with a fallback. The animation preview is separate from the original image.
- **Auto-hide waits for you.** Dragging, editing, and using menus pause the auto-hide countdown. A fresh countdown starts after you finish and leave the shelf. Set the delay in Settings from **0.5 to 30 seconds**. The default is **2.5 seconds**.
- **Maximized windows and fullscreen apps** are handled separately so the shelf can remain available over an ordinary maximized window.

See the [Windows implementation notes](docs/WINDOWS-CHANGES.md) for the original change set, animation timings, and lifecycle details.

## Get started

1. Open [Windows downloads](https://github.com/pasariga/snapline/releases). Pick **Setup.exe** for Start Menu and uninstall entries, or **Portable.exe** to run without setup. Both Windows x64 builds include the .NET runtime. If no release is listed yet, [build from source](LOCAL-DEVELOPMENT.md).
2. Launch Snapline from Start or the portable file. It runs in the notification area.
3. Take a snip with **Win+Shift+S**, then hover at the top edge to reveal the shelf. You can also click the tray icon or press **Ctrl+Alt+T**.
4. Click to copy, hold to annotate, double-click to open, or drag a screenshot where you want it.

The Windows edition targets Windows 11 and Windows 10 x64 (version 1809 or later). The packages are unsigned, so Windows may show an unknown-publisher or SmartScreen prompt. Release notes list the files and how to check their SHA-256 hashes.

## Local storage and screenshot retention

Capture handling and OCR run locally. The app requires no account or API key. Settings, logs, and app-managed captures default to `%LOCALAPPDATA%\Snapline Windows` in both the setup and portable editions. Windows or third-party tools can sync a folder you choose to watch.

**Save screenshots and notes you need to keep outside the managed folder.** Starting a new Windows sign-in session or crossing into a new local date clears the shelf. Snapline may move eligible, app-managed captures to the Recycle Bin after verifying recorded ownership, location, and file hash. External and replaced files remain outside that automatic cleanup.

## FAQ

| Question | Answer |
| --- | --- |
| Why didn’t my screenshot appear? | Check **Catch clipboard captures** in Settings, or add your capture tool’s save folder to the watch list. Use the tray icon or **Ctrl+Alt+T** if top-edge reveal is off. |
| Does the animation reduce screenshot quality? | Animation uses a separate preview. Copy and open use the source image, and annotation can restore the original. |
| Can I keep my existing Snapline installation? | Yes. This edition has its own install entry and data folder. Existing settings are left in their original folder. Run one edition at a time to avoid competing for the same hotkey and clipboard events. |
| Is the interface available in Chinese? | The repository docs are bilingual. The app currently offers English and Arabic. |
| What about macOS? | The repository retains upstream macOS source. This edition prepares Windows x64 packages. |

## Feedback and contributions

[Report a bug or request a feature](https://github.com/pasariga/snapline/issues/new/choose) or see [how to contribute](CONTRIBUTING.md). If Snapline makes your screenshot workflow a little easier, consider giving the repository a star. It helps more people discover the project.

## Credits and license

This is pasariga’s independently maintained Windows edition of [Snapline](https://github.com/mohabujubara/snapline) by **Mohammed Abu Jubara and contributors**, derived from [Tendedero](https://github.com/alejandrobujan/tendedero) by **Alejandro Buján Pampín**. The clothesline concept, inherited features, logo, screenshots, and demo are credited to their respective upstream authors.

Snapline is free and open source under the [MIT License](LICENSE). Manrope is distributed under the [SIL Open Font License](src/Snapline/Assets/Fonts/OFL.txt), also available with the [design assets](design/fonts/OFL.txt).
