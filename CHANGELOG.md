# Changelog / 变更日志

All notable changes are documented here. / 所有重要变更均记录在此。

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed / 修复

- The whole header band now drags the widget, including the frame border, the top padding, and the gaps between header controls; previously the outermost strip was dead / 整个标题带都可拖动小组件，包括边框、顶部内边距和标题控件之间的空隙；此前最顶上的一小段无法拖动

### Planned / 计划

- In-app language switching / 应用内语言切换
- Signed installer and update channel / 签名安装包与更新通道

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

[Unreleased]: https://github.com/Torbjorn-Zhang/quota-lens/compare/v0.4.6...HEAD
[0.4.6]: https://github.com/Torbjorn-Zhang/quota-lens/releases/tag/v0.4.6
[0.3.3]: https://github.com/Torbjorn-Zhang/quota-lens/releases/tag/v0.3.3
