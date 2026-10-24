[English](README.md) | [简体中文](README.zh-CN.md)

# Snapline — Windows 个人修改版

把截图挂在桌面顶部的“晾衣杆”上，随手复制、标注、调整位置，或拖入其他应用。

本仓库保存 **pasariga 对 Windows 版的个人修改**，继承自 [Mohammed Abu Jubara 的 Snapline](https://github.com/mohabujubara/snapline)，后者又源于 [Alejandro Buján Pampín 的 Tendedero](https://github.com/alejandrobujan/tendedero)。晾衣杆构想、基本工作流、已有美术素材和许多工具来自上游；个人改动集中在渲染、交互和截图生命周期。

![Snapline 界面](docs/hero-light.png)

*继承自上游的已有展示素材，用于说明界面，不是性能测试或本修改版的运行实录。*

## 主要功能

继承的工作流包括截图目录和纯图像剪贴板监听、单击复制、拖放、支持撤销与恢复原图的标注、便签、Windows OCR、卡片排列、主题、快捷键设置，以及多显示器／DPI 处理。可以在设置中添加其他截图目录，OCR 语言取决于 Windows 已安装的语言支持。

截图在本机处理，运行软件不需要服务账号或 API Key。应用界面仍保留上游的英语／阿拉伯语选项，中文 README 不代表新增了中文界面。

## 本修改版具体改了什么

以下改动依据 [提交 b732828](https://github.com/pasariga/snapline/commit/b7328285f9b5a846a09913934678e898581d9137)，比较基线是其上游父提交 `6a1f242`。

| 范围 | 已实现的改动 |
| --- | --- |
| 截图动画 | 飞行配置时长由 **0.65 秒改为 0.44 秒**，淡出为 **0.075 秒**。DirectComposition 时间线、阴影与夹子图像缓存构成原生渲染路径，并保留 WPF 回退。这些是配置时长，不是实测帧率或百分比性能提升。 |
| 自动隐藏延迟 | 原有的 0.5 秒改为可配置延迟，默认 **2.5 秒**，范围 **0.5–30 秒**，使用单调时钟跟踪离开截止时间。 |
| 使用期间保护 | 鼠标重新进入会取消倒计时；按住／拖拽卡片、拖动绳子、打开菜单、编辑／设置窗口和清空确认期间，不因超时而收回。交互结束且鼠标离开后，重新计算完整延迟。 |
| 移除卡片 | 卡片移除后留下的空位不再触发立即隐藏。全部收下前需要确认，并通过一次集合更新完成，而不是叠加多组下落动画。 |
| 面板过渡 | 原生面板快照缓存支持从当前可见位置快速反向衔接，同时保留 WPF 回退。 |
| 全屏判断 | 区分带标题栏／最大化窗口与无边框全屏，避免普通应用仅因铺满显示器而压制晾衣杆。 |
| 截图生命周期 | 挂图状态按 Windows 登录会话和本地日期管理。只有记录归属、目录和 SHA-256 均符合条件的托管截图才会回收；外部或已被替换的文件保留。 |
| 图像质量 | 动画预览与原文件分离，默认关闭的紧凑预览仅影响临时动画；复制、打开和编辑仍读取原始截图。 |

显式隐藏命令和全屏压制仍独立于自动隐藏倒计时。源码位置和诊断限制见双语 [Windows 实现说明](docs/WINDOWS-CHANGES.md)。

## 日常使用

1. 启动 `Snapline.exe`，软件驻留通知区域。
2. 使用 Windows 截图工具或其他已配置的工具截图。
3. 鼠标停在顶部边缘、点击托盘图标，或按 **Ctrl+Alt+T** 展开晾衣杆。继承的顶部展开等待默认为 0.5 秒，与自动隐藏延迟不同。
4. 单击复制、按住标注、双击打开，或将卡片拖入应用和文件夹。托盘／晾衣杆菜单提供新截图、便签和设置入口。
5. 在设置中调整自动隐藏延迟。交互会取消计时，离开后重新等待完整延迟。

设置、日志与托管截图默认位于 `%LOCALAPPDATA%\Snapline`，替换安装前应备份该目录。**重要截图和便签应另存到托管目录之外：**新登录会话或日期变化会清空挂图状态，并可能回收符合条件的应用自有文件。“保留在晾衣杆上”不等于永久归档。

## 安装修改版

截至 **2026-10-24**，本仓库尚无已发布的 GitHub Release。上游发行包**不包含这里的个人修改**。未来安装包以[本仓库的 Releases](https://github.com/pasariga/snapline/releases) 为准，目前请从本仓库源码构建。

要求：Windows 10 1809 或更新版本／Windows 11、x64、**.NET 8 SDK**，以及用于 NuGet 依赖恢复的网络连接。目标框架是 `net8.0-windows10.0.19041.0`，声明的最低平台版本为 `10.0.17763.0`，Visual Studio 并非必需。

```powershell
git clone https://github.com/pasariga/snapline.git
cd snapline
dotnet restore src/Snapline/Snapline.csproj
dotnet publish src/Snapline/Snapline.csproj -c Release -r win-x64 --self-contained true -o dist/windows
.\dist\windows\Snapline.exe
```

请完整保留 `dist/windows` 目录，该版本已经包含运行时。也可以运行 `scripts\build.ps1 -Portable`，在 `dist/portable` 生成自包含单文件；不带此开关的 `scripts\build.ps1` 生成依赖 .NET 8 Desktop Runtime 的版本，`-Installer` 还需要 Inno Setup 6。本次文档修改不发布二进制版本。

源码当前记录的版本为 `1.6.2-local.20261016.2`。继承的 `macos/` 目录包含 Swift／SwiftUI 源码，Windows 修改不代表 macOS 具备同等行为或已经验证。

## 实现与验证

C#、WPF 和 Win32 负责桌面界面；Vortice 绑定提供 Direct3D、Direct2D 和 DirectComposition 支持。[LOCAL-DEVELOPMENT.md](LOCAL-DEVELOPMENT.md) 提供干净构建与隔离诊断命令。

已有行为测试和动画诊断覆盖合成交互、图像完整性及渲染插桩。存在测试不等于某台机器已通过；回调／提交时序也不等于屏幕实际显示帧率。本次文档修订不声称新增基准结果或运行测试；真实重启、特定硬件渲染和 macOS 兼容性需要单独验证。

## 项目来源与许可证

这是个人衍生版本，不是上游发行版。Git 历史保留了上游提交与个人修改提交。

- 上游 Snapline：[mohabujubara/snapline](https://github.com/mohabujubara/snapline)，作者 Mohammed Abu Jubara 及贡献者。
- 原始构想及部分衍生实现：[Tendedero](https://github.com/alejandrobujan/tendedero)，作者 Alejandro Buján Pampín。
- [MIT 许可证](LICENSE)：2026 Snapline contributors；衍生部分保留 2025 Alejandro Buján Pampín 的版权声明，许可证原文未改动。
- Manrope 字体保留 [SIL Open Font License](src/Snapline/Assets/Fonts/OFL.txt)，[设计字体目录](design/fonts/OFL.txt)也有副本。

已有截图和演示素材作为继承资料保留。对原作的署名同时适用于界面构想、继承功能和代码。
