# Changelog / 变更日志

All notable changes are documented here. / 所有重要变更均记录在此。

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added / 新增

- Automatic Fable 5 and model-scoped weekly quota monitoring / 自动监控 Fable 5 与其他模型的独立周额度

### Changed / 变更

- The widget now grows vertically only when a model-scoped quota row is available / 仅在存在模型独立额度时自动扩展小组件高度
- Low-quota alerts are combined and remembered across restarts, limiting each quota window to one notification per reset period / 合并低额度提醒并跨重启记忆，每个额度窗口在一次重置周期内只通知一次
- Model reset timestamps are normalized to prevent sub-second API jitter from retriggering alerts; notifications can now be disabled from the tray / 归一化模型重置时间，防止接口毫秒抖动重复提醒，并新增托盘提醒开关
- Codex 5-hour quota restoration is covered as a first-class refresh transition / 将 Codex 5 小时额度恢复纳入正式刷新兼容与回归测试

### Planned / 计划

- In-app language switching / 应用内语言切换
- Signed installer and update channel / 签名安装包与更新通道

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

[Unreleased]: https://github.com/Torbjorn-Zhang/quota-lens/compare/v0.3.3...HEAD
[0.3.3]: https://github.com/Torbjorn-Zhang/quota-lens/releases/tag/v0.3.3
