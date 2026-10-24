# Windows implementation notes / Windows 实现说明

[English README](../README.md) | [简体中文 README](../README.zh-CN.md)

## English

### Baseline

This comparison uses personal commit `b7328285f9b5a846a09913934678e898581d9137` and upstream parent `6a1f2425aea144f692787033188b0d480dd09bf4`. Screenshots, markup, notes, OCR, drag-and-drop and top-edge reveal already existed upstream. The inherited macOS implementation is outside this Windows comparison.

### Rendering path

- [`FlightWindow.cs`](../src/Snapline/UI/FlightWindow.cs): 0.44 s flight, 0.075 s fade, native path with WPF fallback; parent flight duration is 0.65 s.
- [`CompositorFlight.cs`](../src/Snapline/UI/CompositorFlight.cs): prepares compositor timelines and cached shadow/peg pixels. DWM samples the retained timeline; lifetime bookkeeping is not a per-frame redraw loop.
- [`LineWindow.cs`](../src/Snapline/UI/LineWindow.cs): caches snapshots by scene, width and DPI; generation checks reject stale callbacks; rapid reversal starts from an estimated visible position. Configured native reveal/hide intervals are 0.28/0.22 s.
- [`AppController.cs`](../src/Snapline/AppController.cs): limits temporary preview decoding. Optional compact mode also changes the animation rectangle and is disabled by default. Original files are not rewritten by preview generation.

These mechanisms reduce repeated work in the implementation. They do not establish cross-device FPS, latency percentiles or percentage speedups. A fallback may change the actual rendering path.

### Countdown and interaction

[`AutoHideGate.cs`](../src/Snapline/Core/AutoHideGate.cs) tracks one leave deadline. Pointer re-entry or interaction cancels it. [`AppController.cs`](../src/Snapline/AppController.cs) supplies monotonic time and counts card press/drag, rope drag, menus, markup, notes, Settings and clear confirmation as busy states. Card removal also cancels the deadline. The full delay restarts after a fresh leave.

Explicit show/hide and genuine fullscreen suppression are independent. Interaction protection describes the timeout path, not a guarantee that no command can hide the panel.

### Ownership and clearing

[`CaptureSession.cs`](../src/Snapline/Core/CaptureSession.cs) derives a logon identifier from the Windows access token and compares the local calendar date. A boundary change clears hanging positions and pin state. Recycling requires an ownership record, matching folder and SHA-256, and no reparse points along the path. Unreadable, moved or replaced files remain; legacy profiles do not imply ownership.

[`Line.cs`](../src/Snapline/Core/Line.cs) distinguishes clear-all from managed-file disposal. Clear-all removes cards and retained edit backups, not original image files. Session cleanup is separate and may recycle eligible app-owned captures. Keep important material outside the managed folder.

### Verification limits

[`BehaviorTests.cs`](../src/Snapline/UI/BehaviorTests.cs) contains synthetic countdown, interaction, session-boundary, image-integrity and transition checks. [`AnimationBenchmark.cs`](../src/Snapline/UI/AnimationBenchmark.cs) and [`FlightProbe.cs`](../src/Snapline/UI/FlightProbe.cs) record diagnostic timing and resource information. They cover particular paths, not all hardware or UI combinations. Synthetic session changes are not actual reboot acceptance tests, and callback/submit timing is not monitor scanout FPS. See [local development](../LOCAL-DEVELOPMENT.md) for commands. This documentation revision claims no new test runs.

## 简体中文

### 比较基线

比较个人提交 `b7328285f9b5a846a09913934678e898581d9137` 与上游父提交 `6a1f2425aea144f692787033188b0d480dd09bf4`。截图、标注、便签、OCR、拖放和顶部展开均已存在于上游；继承的 macOS 实现不属于这次 Windows 比较范围。

### 渲染路径

- [`FlightWindow.cs`](../src/Snapline/UI/FlightWindow.cs)：飞行 0.44 秒、淡出 0.075 秒，原生路径带 WPF 回退；父提交飞行时长为 0.65 秒。
- [`CompositorFlight.cs`](../src/Snapline/UI/CompositorFlight.cs)：准备合成器时间线，缓存阴影和夹子像素；保留式时间线由 DWM 采样，生命周期等待不是逐帧重绘循环。
- [`LineWindow.cs`](../src/Snapline/UI/LineWindow.cs)：按场景、宽度和 DPI 缓存快照，代次检查排除过期回调，快速反向时从估计的可见位置衔接；原生展开／收回配置时长为 0.28／0.22 秒。
- [`AppController.cs`](../src/Snapline/AppController.cs)：限制临时预览解码，默认关闭的紧凑模式还会改变动画矩形；生成预览不会改写原文件。

这些机制在实现上减少重复工作，但不能证明跨设备帧率、延迟分位数或百分比性能提升；发生回退时实际路径也会变化。

### 倒计时与交互

[`AutoHideGate.cs`](../src/Snapline/Core/AutoHideGate.cs) 管理单一离开截止时间，鼠标重入或交互会取消它。[`AppController.cs`](../src/Snapline/AppController.cs) 提供单调时钟，将按住／拖拽卡片、拖绳子、菜单、标注、便签、设置和清空确认计入忙碌状态，移除卡片也会取消截止时间。再次离开后重新等待完整延迟。

显式展开／隐藏和真实全屏压制独立存在；交互保护针对超时路径，不代表任何命令都不能隐藏面板。

### 归属与清空

[`CaptureSession.cs`](../src/Snapline/Core/CaptureSession.cs) 从 Windows 访问令牌取得登录标识，并比较本地日期。边界变化清空挂图位置和固定状态；回收要求归属记录、目录和 SHA-256 均匹配，且路径无重解析点。不可读、移动过或被替换的文件保留，旧配置不推定所有权。

[`Line.cs`](../src/Snapline/Core/Line.cs) 区分全部收下与托管文件处置：全部收下移除卡片和保留的编辑原图副本，不删除截图原文件；会话清理是另一条路径，可能回收符合条件的应用自有截图。重要内容应另存到托管目录之外。

### 验证限制

[`BehaviorTests.cs`](../src/Snapline/UI/BehaviorTests.cs) 包含合成倒计时、交互、会话边界、图像完整性和过渡检查。[`AnimationBenchmark.cs`](../src/Snapline/UI/AnimationBenchmark.cs) 与 [`FlightProbe.cs`](../src/Snapline/UI/FlightProbe.cs) 记录诊断时序和资源信息，只覆盖特定路径，不代表所有硬件和界面组合。合成会话变化不是实际重启验收，回调／提交时序也不是显示器扫描输出帧率。命令见[本地开发说明](../LOCAL-DEVELOPMENT.md)，本次文档修订不声称新增测试运行。
