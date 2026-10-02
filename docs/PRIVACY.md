# 隐私说明

[English](PRIVACY.en.md) | 简体中文

## 读取的数据

- Codex OAuth token 与 ChatGPT account ID
- Claude Code OAuth token，或 Claude Desktop 加密登录缓存
- macOS：钥匙串项“Claude Safe Storage”（Claude 桌面版的安全存储密码）与“Claude Code-credentials”（Claude Code 命令行的登录信息）。只有在 macOS 弹出的授权对话框中允许后才能读取；读取通过 Security.framework 直接进行，“始终允许”只信任 Quota Lens 本身
- 官方额度服务返回的方案、使用百分比、重置时间和 credits 信息

## 数据去向

- Codex 凭据仅发送至 `https://chatgpt.com/backend-api/wham/usage`
- Claude 凭据仅发送至 `https://api.anthropic.com/api/oauth/usage`
- 应用不包含分析、遥测、广告或第三方中转服务

## 本地存储

`%LOCALAPPDATA%\QuotaLens\settings.json` 只保存刷新间隔、开机启动、置顶、透明度、窗口位置和贴边方向。启用开机启动时，程序在 Windows 任务计划程序中为当前用户注册一个名为 `QuotaLens` 的登录任务，其中保存可执行文件路径；任务计划程序不可用时才回退到当前用户的 `Run` 注册表项。

`%LOCALAPPDATA%\QuotaLens\startup.log` 记录程序的启动、退出、开机启动注册结果和未处理错误，每行只含时间、版本、启动参数和简短信息，保留最近 200 行。

macOS 版的设置和日志位于 `~/Library/Application Support/QuotaLens/`（`settings.json` 只保存刷新间隔、登录时启动与低额度提醒状态）。日志另外在额度可用状态变化时记一行，只含“成功/失败”和界面上显示的友好错误文字。启用“登录时启动”时，程序写入 `~/Library/LaunchAgents/io.github.torbjorn-zhang.quotalens.plist`，其中只保存应用路径。

OAuth token、额度响应正文和账户 ID 不会写入 Quota Lens 的文件或日志。解密后的 Claude Desktop token 与主密钥只存在于进程内存中，使用结束后相关字节缓冲区会被清零。macOS 上由钥匙串密码派生的解密密钥会在进程内存中保留到退出，以免每次刷新都重新请求钥匙串授权；钥匙串密码本身用完即清零。

## 删除数据

退出 Quota Lens 后删除 `%LOCALAPPDATA%\QuotaLens` 即可清除其设置和日志。也可从托盘菜单关闭开机启动，删除对应的登录任务（以及可能存在的旧注册表项）。此操作不会删除 Claude 或 Codex 的登录信息。

macOS 上先在菜单中关闭“登录时启动”（会删除上面的 LaunchAgent）并退出，再删除 `~/Library/Application Support/QuotaLens` 和 `/Applications/QuotaLens.app`。如需撤销钥匙串授权，可在“钥匙串访问”中打开“Claude Safe Storage”，在“访问控制”里移除 Quota Lens。
