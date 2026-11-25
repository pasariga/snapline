# Snapline Windows 1.6.2-windows.1

[简体中文](#简体中文)

First Windows x64 release candidate for pasariga’s independently maintained Snapline edition. It follows the local source version `1.6.2-local.20261016.2` and packages the Windows changes introduced in [b732828](https://github.com/pasariga/snapline/commit/b7328285f9b5a846a09913934678e898581d9137).

- Screenshots fly into the shelf using the Windows compositor where available. Animation previews leave the original image intact.
- Auto-hide waits while you drag a card, edit, or use a menu. The delay defaults to 2.5 seconds and can be changed in Settings.
- Ordinary maximized windows and borderless fullscreen apps are handled separately.
- The installer uses its own Windows edition identity, install path, profile, and sign-in entry. Existing Snapline data is kept in its original directory.

| File | Use |
| --- | --- |
| `Snapline-1.6.2-windows.1-win-x64-Setup.exe` | Per-user installer with Start Menu and uninstall entries |
| `Snapline-1.6.2-windows.1-win-x64-Portable.exe` | Direct launch with no setup |
| `SHA256SUMS.txt` | SHA-256 hashes for both executables |

Both executables are self-contained and target Windows 11 and Windows 10 x64 (1809 or newer). They are unsigned. Windows may show an unknown-publisher or SmartScreen prompt. To compare hashes in PowerShell, run `Get-FileHash -Algorithm SHA256 -Path .\Snapline-*-win-x64-*.exe` in the download folder and compare the result for each file with `SHA256SUMS.txt`.

Save important screenshots and notes outside the managed folder. The shelf resets after a new sign-in session or local date change, and eligible app-managed captures may go to the Recycle Bin. The setup and portable editions share `%LOCALAPPDATA%\Snapline Windows`.

## 简体中文

这是 pasariga 独立维护的 Snapline 首个 Windows x64 发行候选版，承接本地源码版本 `1.6.2-local.20261016.2`，打包了由 [b732828](https://github.com/pasariga/snapline/commit/b7328285f9b5a846a09913934678e898581d9137) 引入的 Windows 修改。

- 截图飞入晾衣杆时会优先使用 Windows 合成器，预览动画保留原图。
- 拖拽、编辑、使用菜单期间暂停自动收起。默认延迟 2.5 秒，可在设置里调整。
- 区分普通最大化窗口和无边框全屏应用。
- 安装版使用独立的安装标识、目录、配置和开机启动项，已有 Snapline 数据留在原目录。

| 文件 | 用途 |
| --- | --- |
| `Snapline-1.6.2-windows.1-win-x64-Setup.exe` | 当前用户安装版，提供开始菜单和卸载入口 |
| `Snapline-1.6.2-windows.1-win-x64-Portable.exe` | 免安装直接运行 |
| `SHA256SUMS.txt` | 两个可执行文件的 SHA-256 校验值 |

两个可执行文件均自带运行时，面向 Windows 11 和 Windows 10 x64（1809 或更新版本）。文件未签名，Windows 可能弹出未知发布者或 SmartScreen 提示。在下载目录用 PowerShell 执行 `Get-FileHash -Algorithm SHA256 -Path .\Snapline-*-win-x64-*.exe`，逐项与 `SHA256SUMS.txt` 比对即可校验。

请把重要截图和便签另存到托管目录外。新登录会话或本地日期变化时晾衣杆会清空，符合条件的托管截图可能移入回收站。安装版和便携版共用 `%LOCALAPPDATA%\Snapline Windows`。

## Credits / 署名

Upstream [Snapline](https://github.com/mohabujubara/snapline) by Mohammed Abu Jubara and contributors, based on [Tendedero](https://github.com/alejandrobujan/tendedero) by Alejandro Buján Pampín. [MIT License](https://github.com/pasariga/snapline/blob/main/LICENSE) · [Manrope SIL Open Font License](https://github.com/pasariga/snapline/blob/main/src/Snapline/Assets/Fonts/OFL.txt).
