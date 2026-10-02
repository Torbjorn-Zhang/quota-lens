# Quota Lens

[English](README.en.md) | 简体中文

[![CI](https://github.com/Torbjorn-Zhang/quota-lens/actions/workflows/ci.yml/badge.svg)](https://github.com/Torbjorn-Zhang/quota-lens/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Torbjorn-Zhang/quota-lens?display_name=tag)](https://github.com/Torbjorn-Zhang/quota-lens/releases)
[![License](https://img.shields.io/github/license/Torbjorn-Zhang/quota-lens)](LICENSE)

Quota Lens 是一个轻量的额度小工具，用于实时查看 **Claude Code** 与 **Codex** 的订阅额度、重置时间和剩余比例。Windows 上是透明桌面小组件（可贴边成侧栏），macOS 上是菜单栏应用。

![Quota Lens 预览](docs/images/preview.png)

> [!IMPORTANT]
> 本项目是非官方社区工具，与 Anthropic 或 OpenAI 无隶属或背书关系。额度接口并非稳定的公共 API，上游变化可能导致功能暂时失效。

## 功能

- 同时显示 Codex 与 Claude Code 的 5 小时、7 天等额度窗口；Codex 短周期额度恢复后会在下次刷新自动重新显示
- 自动识别并显示 Fable 等模型家族的独立周额度（Fable 5 与 Fable 5.1 共用同一份 Fable 周额度）；接口返回多个模型家族时逐行显示，未返回时自动隐藏
- 识别 Claude Pro、Max 5× 与 Max 20× 方案；点击刷新会立即更新 Claude 方案与额度
- 显示订阅方案、重置倒计时、附加 credits 与模型专项周额度
- Codex 每 60 秒刷新；Claude 最快每 3 分钟刷新
- Claude 遇到 HTTP 429 时自动按 5/10/20/30 分钟退避，并保留上次成功数据
- 可从托盘关闭低额度提醒；开启时会合并同时出现的提醒，同一重置周期只提醒一次
- 透明悬浮窗、置顶、拖动、托盘常驻和透明度调节
- 贴边侧栏：拖到屏幕左/右边缘后收成一条竖条，用同心圆和重置时间展示全部额度，鼠标停上去弹出完整面板，移开自动收回
- 一键关闭所有显示器，同时阻止系统自动睡眠；鼠标或键盘即可唤醒屏幕
- 可选随 Windows 登录自动启动
- macOS 菜单栏版：菜单栏图标用两组迷你同心圆显示 Codex 与 Claude 额度，点一下即弹出同款深色面板，逐项显示剩余百分比和实时重置倒计时
- 不保存 OAuth token，不记录请求或凭据日志

## 系统要求

- Windows 10 或 Windows 11（x64），或 macOS 11 及以上（Apple Silicon 或 Intel）
- 已使用 ChatGPT 账号登录 Codex
- 已使用 Claude 订阅账号登录 Claude Code，或已登录 Claude Desktop（Windows 上为 Microsoft Store 版）

API key、Bedrock、Vertex 等按量计费账号通常没有相同的订阅额度百分比，因此不在支持范围内。

## 安装

### Windows

1. 从 [Releases](https://github.com/Torbjorn-Zhang/quota-lens/releases) 下载最新的 `QuotaLens-*-win-x64.zip`。
2. 解压到一个固定目录。
3. 双击 `QuotaLens.exe`。
4. 如需开机启动，在托盘菜单中启用“开机启动”。

当前版本未进行商业代码签名，Windows SmartScreen 可能首次显示提示。请只从本仓库的 Releases 下载，或自行从源码构建。

### macOS

**一键安装**：打开“终端”，粘贴运行：

```bash
curl -fsSL https://raw.githubusercontent.com/Torbjorn-Zhang/quota-lens/main/install-mac.sh | bash
```

脚本自动识别 Apple Silicon 或 Intel，从最新发布版下载 macOS 安装包并校验 SHA-256，装进“应用程序”后直接启动；已有旧版本时会先退出再替换。若最新发布版还没有 macOS 包（或加 `--source`），会改为在本机从源码构建：只需联网，不需要 Git、Xcode 或管理员权限，.NET 6 SDK 只装在 `~/Library/Caches/QuotaLens`。已经克隆仓库时，也可以在 Finder 中双击 `Install-QuotaLens.command`。`--help` 查看全部选项。

手动安装：

1. 从 Releases 下载 `QuotaLens-*-macos-arm64.zip`（Apple Silicon）或 `QuotaLens-*-macos-x64.zip`（Intel），解压后把 `QuotaLens.app` 拖进“应用程序”。
2. 应用只做了临时签名、没有经过 Apple 公证，首次打开会被拦截。可在“系统设置 → 隐私与安全性”中点“仍要打开”，或在终端执行：

   ```bash
   xattr -dr com.apple.quarantine /Applications/QuotaLens.app
   ```

3. 打开后菜单栏出现 `C◎ A◎` 图标，没有程序坞图标。首次读取 Claude 桌面版登录态时，macOS 会询问是否允许 Quota Lens 使用钥匙串中的“Claude Safe Storage”，输入登录密码并选“始终允许”即可。由于应用未经 Apple 签名，每次更新 Quota Lens 后 macOS 会把它当作新程序，再询问一次。
4. 首次运行默认开启“登录时启动”，可在面板底部关闭。

## 使用

### macOS

- 菜单栏缩略图对应 Windows 的贴边竖条：左边 `C` 是 Codex、右边 `A` 是 Claude，每组同心圆外圈 5 小时、中圈 7 天、内圈模型专项额度，颜色与 Windows 版一致；圆环右侧两行分别是 5 小时和 7 天的剩余百分比与重置倒计时（如 `77% 2h01m`、`73% 2d14h`），每 30 秒更新，余量偏低时变橙或变红。
- 点击图标会在它下方弹出同心圆详情面板：每个额度窗口一行，显示剩余百分比和逐秒更新的重置倒计时（如“2时1分后 · 10/1 03:40”），与 Windows 版面板一致；点击别处或再点一次图标即收起。
- 面板右上角 `↻` 立即刷新；底部可切换“登录时启动”“低额度提醒”，另有“息屏”（关闭显示器并保持电脑运行）和“退出”。

### Windows

- 拖动顶部标题区域移动小组件。
- 拖到屏幕左或右边缘会贴边成侧栏：平时只露一条竖条。Codex 与 Claude 各有一组同心圆：外圈 5 小时、中圈 7 天、内圈模型专项额度，弧长表示剩余比例；圆下方按同样顺序和颜色逐行列出剩余百分比与重置时间（24 小时内显示时刻如 `14:30`，更远显示日期如 `10/3`）。鼠标停在竖条上会滑出完整面板，移开后自动收回。拖离边缘即恢复悬浮窗，也可在托盘菜单中切换“贴边侧栏”。贴边时竖条始终置顶。
- 颜色固定对应额度类型，面板和侧栏一致：蓝色为 5 小时，紫色为 7 天，绿色为模型专项额度（如 Fable）。余量不超过 20% 时圆环和条形变红；百分比在 40% 以下变橙、20% 以下变红。
- 点击 `↻` 立即刷新，点击 `◇` 切换置顶（仅悬浮窗模式）。
- 点击月亮按钮关闭显示器并保持电脑运行。
- 点击 `×` 只会隐藏到托盘；从托盘菜单选择“退出”才会完全结束程序。
- 鼠标移入时会提高不透明度，移出后恢复。

## 隐私与安全

Quota Lens 只读取当前用户已有的登录状态，并把凭据直接发送给对应官方服务：

- Codex：读取 `~/.codex/auth.json`（Windows 为 `%USERPROFILE%\.codex\auth.json`）或 `CODEX_HOME`
- Claude Code：读取 `~/.claude/.credentials.json` 或 `CLAUDE_CONFIG_DIR`；macOS 上还会读取钥匙串项“Claude Code-credentials”
- Claude Desktop：Windows 上读取由 DPAPI 保护的 Electron 安全存储；macOS 上经你授权后从钥匙串取“Claude Safe Storage”密码解密。两者都只在内存中解密

token 不会写入 Quota Lens 设置，也不会发送给第三方。详细信息见[隐私说明](docs/PRIVACY.md)和[安全策略](SECURITY.md)。

## 从源码构建

需要 [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)。

```powershell
dotnet restore .\QuotaLens.csproj
dotnet build .\QuotaLens.csproj -c Release
dotnet run --project .\QuotaLens.csproj -c Release
```

运行解析器测试：

```powershell
dotnet run --project .\tests\QuotaLens.Tests\QuotaLens.Tests.csproj -c Release
```

生成单文件发布包：

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1
```

默认生成包含 .NET 运行时的 `artifacts\win-x64-v<版本>\QuotaLens.exe`。使用 `-FrameworkDependent` 可生成较小的框架依赖版本。

macOS 版在 macOS 上构建（需要 .NET 6 SDK 与 Xcode 命令行工具）：

```bash
./publish-mac.sh osx-arm64
```

生成 `artifacts/mac/osx-arm64/QuotaLens.app` 和 `artifacts/QuotaLens-v<版本>-macos-arm64.zip`。共享逻辑在 `src/QuotaLens.Core`，Windows 界面在仓库根目录（WPF），macOS 界面在 `src/QuotaLens.Mac`（Avalonia）。

## 项目文档

- [架构说明](docs/ARCHITECTURE.md)
- [隐私说明](docs/PRIVACY.md)
- [路线图](ROADMAP.md)
- [贡献指南](CONTRIBUTING.md)
- [变更日志](CHANGELOG.md)
- [安全策略](SECURITY.md)

## 参与贡献

欢迎提交 Issue 和 Pull Request。请勿在截图、日志、测试数据或 Issue 中提交真实 token。完整流程见[贡献指南](CONTRIBUTING.md)。

## 许可证

[MIT](LICENSE) © 2026 Torbjorn-Zhang
