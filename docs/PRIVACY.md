# 隐私说明

[English](PRIVACY.en.md) | 简体中文

## 读取的数据

- Codex OAuth token 与 ChatGPT account ID
- Claude Code OAuth token，或 Claude Desktop 加密登录缓存
- 官方额度服务返回的方案、使用百分比、重置时间和 credits 信息

## 数据去向

- Codex 凭据仅发送至 `https://chatgpt.com/backend-api/wham/usage`
- Claude 凭据仅发送至 `https://api.anthropic.com/api/oauth/usage`
- 应用不包含分析、遥测、广告或第三方中转服务

## 本地存储

`%LOCALAPPDATA%\QuotaLens\settings.json` 只保存刷新间隔、开机启动、置顶、透明度和窗口位置。启用开机启动时，程序在当前用户的 Windows `Run` 注册表项中保存可执行文件路径。

OAuth token、额度响应正文和账户 ID 不会写入 Quota Lens 的文件或日志。解密后的 Claude Desktop token 与主密钥只存在于进程内存中，使用结束后相关字节缓冲区会被清零。

## 删除数据

退出 Quota Lens 后删除 `%LOCALAPPDATA%\QuotaLens` 即可清除其设置。也可从托盘菜单关闭开机启动，删除对应注册表项。此操作不会删除 Claude 或 Codex 的登录信息。
