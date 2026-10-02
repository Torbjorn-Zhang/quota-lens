# Changelog / 变更日志

All notable changes are documented here. / 所有重要变更均记录在此。

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed / 变更

- macOS: the menu bar thumbnail is restyled after Apple's Activity rings and menu bar, replacing the outlines of 0.6.2. Each ring is drawn in its identity colour over a track of the same colour at low opacity, and each line starts with a dot in that colour, so it stays clear which line is the 5-hour and which the 7-day window. Text is in the menu bar's own label colour (white on dark menu bars, black on light ones) in the system font with equal-width digits, drawn with AppKit, so it reads like the system's own items on any wallpaper. Only a low percentage turns orange (40% or less) or red (20% or less), and a ring at 20% or less turns red, in Apple's increased-contrast system colours; the menu bar's actual appearance is read from the status item, and the thumbnail is only as wide as its text (about 185 instead of 218 points), leaving room for other items beside the notch / macOS：菜单栏缩略图改为参照苹果活动圆环和系统菜单栏的风格，取代 0.6.2 的描边：每个圆环用各自的身份色，底轨为同色淡色，每行文字前有同色小圆点，一眼就能分清哪行是 5 小时、哪行是 7 天；文字使用菜单栏自身的标签颜色（深色菜单栏为白色、浅色为黑色），以系统字体（等宽数字）通过 AppKit 绘制，在任何壁纸上都和系统图标一样清晰；只有额度偏低时数字才变为系统橙（40% 及以下）或系统红（20% 及以下），圆环在 20% 及以下变红；深浅色按状态栏项的实际外观判断；缩略图宽度按实际文字计算（约 185pt，原来 218pt），给刘海旁的其他菜单栏图标留出位置

### Planned / 计划

- In-app language switching / 应用内语言切换
- Signed installer and update channel / 签名安装包与更新通道

## [0.6.2] - 2026-10-02

### Changed / 变更

- macOS: the menu bar thumbnail drops the solid plate added in 0.6.1 and keeps a transparent background; its text and ring arcs get a thin outline instead (dark glass on dark menu bars, white on light ones), which keeps the same 4.5:1 contrast over bright or busy wallpapers / macOS：菜单栏缩略图去掉 0.6.1 加的实心底板，恢复透明背景，改为给文字和圆环加一圈细描边（深色菜单栏为深色描边、浅色菜单栏为白色描边），在亮色或花哨的壁纸上仍保持 4.5:1 的对比度

## [0.6.1] - 2026-10-02

### Changed / 变更

- Easier-to-read quota colours: sky blue 5-hour, orchid 7-day, and mint model allowance replace the cyan, lavender, and green that were hard to tell apart (the outer and inner rings were too close for full-colour vision) and that faded over bright backgrounds. Every pair now stays distinct for red-green colour-blind readers too, and a parser check keeps every quota colour at 4.5:1 or more on the surfaces it is drawn on / 额度颜色更易辨认：5 小时改为天蓝、7 天改为兰紫、模型专项改为薄荷绿，取代原来难以区分（最外圈与最内圈颜色过近）且在亮背景上发虚的青、淡紫、绿；三种颜色对红绿色盲读者也能区分，并新增测试保证每种额度颜色在实际背景上的对比度不低于 4.5:1
- macOS: the menu bar thumbnail sits on its own rounded plate (dark glass on dark menu bars, near-white on light ones), because the macOS 26 menu bar is transparent and bright or busy wallpapers washed the colours out; on light menu bars the rings now use the deeper light-surface shades like the text / macOS：菜单栏缩略图加了圆角底板（深色菜单栏用深色玻璃，浅色菜单栏用近白色），因为 macOS 26 的菜单栏是透明的，亮色或花哨的壁纸会让颜色看不清；浅色菜单栏上的圆环也和文字一样改用加深的颜色
- Windows: the sidebar strip, and the panel while the cursor is on it or it is slid out of the sidebar, always use the full glass density, so a bright desktop no longer shows through; the transparency setting now applies only while the floating widget sits idle / Windows：贴边竖条，以及鼠标停在面板上或从侧栏滑出时的面板，始终使用最高玻璃浓度，亮色桌面不再透出来；透明度设置只作用于闲置时的悬浮窗

### Fixed / 修复

- The macOS installer stopped at once with `arch?: unbound variable` when Terminal used a UTF-8 locale (the default), because the system bash 3.2 read the full-width bracket after `$arch` as part of the variable name; variables next to Chinese text are now braced, and CI rejects new ones / macOS 安装脚本在终端使用 UTF-8 区域设置（默认情况）时会立刻报 `arch?: unbound variable` 退出：系统自带的 bash 3.2 会把 `$arch` 后面的全角括号读进变量名。紧挨中文的变量已改为 `${…}` 写法，CI 也会拦下新的此类写法

## [0.6.0] - 2026-10-02

### Added / 新增

- macOS menu bar app (Apple Silicon and Intel, macOS 11+): the menu bar thumbnail shows Codex and Claude as two sets of concentric rings, each with the 5-hour and 7-day remaining percentage and reset countdown beside it (for example `77% 2h01m`); a click drops the ring panel below the icon with each quota window's remaining percentage and a live reset countdown, plus refresh, display-off-while-awake, launch at login (a per-user LaunchAgent), and low-quota notifications. Released as ad-hoc signed `QuotaLens-*-macos-arm64.zip` / `-x64.zip` / 新增 macOS 菜单栏应用（Apple Silicon 与 Intel，macOS 11 及以上）：菜单栏缩略图用两组同心圆显示 Codex 与 Claude，旁边各有 5 小时与 7 天的剩余百分比和重置倒计时（如 `77% 2h01m`）；点一下在图标下方弹出同心圆详情面板，逐项显示剩余百分比与实时重置倒计时，并提供刷新、息屏保持运行、登录时启动（当前用户 LaunchAgent）和低额度通知。以临时签名的 `QuotaLens-*-macos-arm64.zip` / `-x64.zip` 发布
- One-step macOS installer `install-mac.sh` (`curl -fsSL …/install-mac.sh | bash`): installs the latest macOS release after a SHA-256 check, or builds from source when no release has one (no Git, Xcode, or admin rights; the ~1.3 GB of SDK and packages stays in a temporary folder that is deleted afterwards), replaces a running copy, and starts the app; `Install-QuotaLens.command` runs it by double-click from a checkout / macOS 一键安装脚本 `install-mac.sh`（`curl -fsSL …/install-mac.sh | bash`）：校验 SHA-256 后安装最新 macOS 发布版，没有发布版时改为本机从源码构建（不需要 Git、Xcode 或管理员权限，约 1.3 GB 的 SDK 与构建包放在临时目录、装完即删），自动替换正在运行的旧版并启动；克隆仓库后可双击 `Install-QuotaLens.command`
- macOS credential support: Claude Desktop's safe-storage cache is decrypted with the “Claude Safe Storage” keychain password (Chromium's macOS scheme), and the Claude Code CLI's “Claude Code-credentials” keychain item is read; keychain prompts never stall the other provider's refresh / 支持 macOS 凭据：用钥匙串“Claude Safe Storage”密码按 Chromium 的 macOS 方案解密 Claude 桌面版缓存，并读取 Claude Code 命令行的“Claude Code-credentials”钥匙串项；等待钥匙串授权时不影响另一个服务的刷新

### Changed / 变更

- Quota logic moved into a cross-platform `src/QuotaLens.Core` library shared by the Windows (WPF) and macOS (Avalonia) apps; the parser checks now run on both Windows and macOS CI, and releases include the macOS builds / 额度逻辑移入跨平台的 `src/QuotaLens.Core`，由 Windows（WPF）与 macOS（Avalonia）两端共用；解析器测试在 Windows 与 macOS CI 上都会运行，发布包含 macOS 版本

## [0.5.0] - 2026-10-01

### Added / 新增

- QQ-style edge sidebar: dragging the widget against the left or right screen edge docks it as a slim always-on-top strip with concentric ring gauges per service (outer 5-hour, middle 7-day, inner model allowance) and a row per window with the remaining percentage and reset time (clock time within 24 hours, otherwise the date); resting the cursor on the strip slides the full panel out and moving away tucks it back; dragging it off the edge or the tray item "贴边侧栏" returns to the floating widget. Existing installs start docked to the right / QQ 式贴边侧栏：把小组件拖到屏幕左/右边缘即贴边为始终置顶的细竖条，每个服务一组同心圆（外圈 5 小时、中圈 7 天、内圈模型专项），下方逐行列出剩余百分比和重置时间（24 小时内显示时刻，否则显示日期）；鼠标停在竖条上滑出完整面板，移开自动收回；拖离边缘或用托盘“贴边侧栏”可恢复悬浮窗。已安装用户升级后默认贴在右侧
- Local `startup.log` next to `settings.json` records start, exit, autostart registration and unhandled errors (timestamps, version, arguments and short messages only; last 200 lines) / 在 `settings.json` 旁新增本地 `startup.log`，记录启动、退出、自启注册与未处理错误（仅时间、版本、参数和简短信息，保留最近 200 行）

### Changed / 变更

- Each quota type now has a fixed colour shared by the panel and the sidebar strip (blue 5-hour, violet 7-day, green model allowances such as Fable), with the panel's window names tinted as the legend; rings and bars turn red at 20% or less and percentages warn in orange at 40% and red at 20%. Previously every bar was coloured by remaining level only / 每种额度类型使用固定颜色，面板与侧栏一致（蓝色 5 小时、紫色 7 天、绿色模型专项如 Fable），面板中的窗口名称按同色标注作为图例；余量不超过 20% 时圆环和条形变红，百分比在 40% 以下变橙、20% 以下变红。此前所有条形只按余量高低着色
- "Start with Windows" now registers a per-user Task Scheduler logon task (5-second delay, interactive token, no elevation) instead of a Run registry value, which Windows 11 Explorer was observed to skip silently at sign-in; the Run value is removed on upgrade and only used as a fallback when Task Scheduler is unavailable / “开机启动”改为注册当前用户的 Task Scheduler 登录任务（延迟 5 秒、交互令牌、不提权），不再写 Run 注册表值；实测 Windows 11 的 Explorer 会在登录时静默跳过该值。升级后自动清除旧的 Run 值，仅在 Task Scheduler 不可用时回退

### Fixed / 修复

- The saved window position is restored on launch again; WPF re-centred the window after it had been placed, and the always-on-top preference is no longer overwritten when the position is saved / 启动时重新正确恢复上次的窗口位置（此前 WPF 会在放置后再次居中）；保存位置时不再覆盖“置顶”偏好

## [0.4.7] - 2026-09-04

### Fixed / 修复

- The whole header band now drags the widget, including the frame border, the top padding, and the gaps between header controls; previously the outermost strip was dead / 整个标题带都可拖动小组件，包括边框、顶部内边距和标题控件之间的空隙；此前最顶上的一小段无法拖动

## [0.4.6] - 2026-09-02

### Added / 新增

- Automatic Fable 5 and model-scoped weekly quota monitoring / 自动监控 Fable 5 与其他模型的独立周额度
- `--raw-usage` diagnostic in the parser test harness prints the structural shape of the live Claude usage response; string values are masked except a short allowlist of structural keys (kind, resets_at, …), `scope.model.display_name`, and model ids starting with `claude-` / 解析器测试新增 `--raw-usage` 诊断模式，打印 Claude usage 响应的结构；除少数结构键（kind、resets_at 等）、`scope.model.display_name` 和 `claude-` 开头的模型 id 外，字符串值均脱敏

### Changed / 变更

- Every Claude model-family weekly allowance is now shown as its own row (Fable first, deduplicated by display name) instead of only the preferred one; the usage API currently reports a single "Fable" bucket shared by Fable 5 and Fable 5.1, and an upstream split would appear as an extra row automatically / 所有 Claude 模型家族的周额度逐行显示（Fable 优先、按显示名去重），不再只显示一条；usage 接口目前只返回一个由 Fable 5 与 Fable 5.1 共用的 "Fable" 桶，若上游拆分会自动新增一行

- The widget now grows vertically only when a model-scoped quota row is available / 仅在存在模型独立额度时自动扩展小组件高度
- Low-quota alerts are combined and remembered across restarts, limiting each quota window to one notification per reset period / 合并低额度提醒并跨重启记忆，每个额度窗口在一次重置周期内只通知一次
- Model reset timestamps are normalized to prevent sub-second API jitter from retriggering alerts; notifications can now be disabled from the tray / 归一化模型重置时间，防止接口毫秒抖动重复提醒，并新增托盘提醒开关
- Codex 5-hour quota restoration is covered as a first-class refresh transition / 将 Codex 5 小时额度恢复纳入正式刷新兼容与回归测试
- Claude account metadata now identifies Pro, Max 5×, and Max 20×; manual refresh bypasses the normal cache while preserving 429 backoff / 读取 Claude 账户元数据识别 Pro、Max 5× 与 Max 20×，手动刷新绕过普通缓存但仍遵守 429 退避

### Fixed / 修复

- Tooltips (button hints and the shared Fable row) now use the widget's dark glass theme; the default WPF tooltip drew near-white text on a white box / 提示气泡（按钮提示与共用 Fable 行）改用小组件的深色玻璃样式，此前 WPF 默认样式是白底白字的一片白框

## [0.3.3] - 2026-07-17

### Added / 新增

- Custom high-contrast tray gauge icon / 自定义高对比度托盘额度图标

### Changed / 变更

- Start-with-Windows now opens the widget instead of hiding it in the tray / 开机启动后直接显示小组件，不再静默隐藏

## [0.3.1] - 2026-07-17

### Added / 新增

- One-click display power-off / 一键关闭显示器
- Continuous system-awake mode after display power-off / 息屏后持续阻止系统自动睡眠
- Single-instance protection / 单实例保护

## [0.3.0] - 2026-07-17

### Added / 新增

- Microsoft Store Claude Desktop credential-cache support / 支持 Microsoft Store 版 Claude Desktop 登录缓存
- DPAPI and Electron safe-storage decryption in memory / 在内存中解锁 DPAPI 与 Electron 安全存储
- Claude 429 backoff with last-known-good data / Claude 限流退避与上次成功数据保留

## [0.2.0] - 2026-07-17

### Changed / 变更

- Reworked the UI as a translucent Windows gadget / 将界面改为透明 Windows 小组件风格

## [0.1.0] - 2026-07-17

### Added / 新增

- Initial Codex and Claude Code quota monitoring / 初始 Codex 与 Claude Code 额度监控

[Unreleased]: https://github.com/Torbjorn-Zhang/quota-lens/compare/v0.6.2...HEAD
[0.6.2]: https://github.com/Torbjorn-Zhang/quota-lens/releases/tag/v0.6.2
[0.6.1]: https://github.com/Torbjorn-Zhang/quota-lens/releases/tag/v0.6.1
[0.6.0]: https://github.com/Torbjorn-Zhang/quota-lens/releases/tag/v0.6.0
[0.5.0]: https://github.com/Torbjorn-Zhang/quota-lens/releases/tag/v0.5.0
[0.4.7]: https://github.com/Torbjorn-Zhang/quota-lens/releases/tag/v0.4.7
[0.4.6]: https://github.com/Torbjorn-Zhang/quota-lens/releases/tag/v0.4.6
[0.3.3]: https://github.com/Torbjorn-Zhang/quota-lens/releases/tag/v0.3.3
