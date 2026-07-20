# Security Policy / 安全策略

## Supported versions / 支持版本

Security fixes are provided for the latest release only. / 安全修复仅面向最新版本。

## Reporting a vulnerability / 报告漏洞

Please use GitHub's private vulnerability reporting under **Security → Advisories → Report a vulnerability**. Do not open a public issue for credential exposure, authentication bypass, arbitrary code execution, or unsafe update behavior.

请使用 GitHub 的私密漏洞报告功能：**Security → Advisories → Report a vulnerability**。涉及凭据泄露、身份验证绕过、任意代码执行或不安全更新行为时，请勿创建公开 Issue。

Include the affected version, reproduction steps, impact, and a minimal sanitized proof of concept. Never attach a real token or credential cache. / 请提供受影响版本、复现步骤、影响和经过脱敏的最小示例，绝不要附带真实 token 或凭据缓存。

## Security boundaries / 安全边界

- Quota Lens reads existing OAuth state for the current Windows user. / Quota Lens 会读取当前 Windows 用户已有的 OAuth 登录状态。
- Claude Desktop safe storage is decrypted with Windows DPAPI only in the same user's process memory. / Claude Desktop 安全存储仅在同一用户进程内通过 Windows DPAPI 解密。
- Tokens are not persisted, logged, or sent to third parties. / token 不会被保存、记录或发送给第三方。
- Usage requests go directly to `chatgpt.com` and `api.anthropic.com`. / 额度请求直接发往 `chatgpt.com` 与 `api.anthropic.com`。
- The application is currently unsigned. Verify release hashes or build from source when trust is critical. / 当前应用未签名；对信任要求较高时请校验发布哈希或自行构建。
