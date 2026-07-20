# Contributing / 贡献指南

Thank you for improving Quota Lens. 感谢你参与改进 Quota Lens。

## Before opening an issue / 提交 Issue 前

- Search existing issues first. / 请先搜索现有 Issue。
- Remove account IDs, tokens, and private paths from screenshots or logs. / 从截图和日志中删除账号 ID、token 与私人路径。
- State the Windows version, Quota Lens version, login method, and exact error text. / 提供 Windows 版本、Quota Lens 版本、登录方式和完整错误文字。

## Development / 开发

Requirements / 环境：

- Windows 10/11
- .NET 6 SDK
- Test accounts are optional; parser checks use synthetic fixtures. / 测试账号不是必需的，解析器检查使用合成数据。

```powershell
dotnet restore .\QuotaLens.csproj
dotnet build .\QuotaLens.csproj -c Release
dotnet run --project .\tests\QuotaLens.Tests\QuotaLens.Tests.csproj -c Release
```

## Pull requests / Pull Request

1. Create a focused branch from `main`. / 从 `main` 创建单一目标的分支。
2. Keep credential handling read-only and in memory. / 凭据处理必须保持只读且仅驻留内存。
3. Add or update synthetic parser checks when response formats change. / 响应格式变化时补充合成解析测试。
4. Run build and tests before opening a PR. / 提交 PR 前运行构建与测试。
5. Explain user impact, security impact, and validation in the PR body. / 在 PR 描述中说明用户影响、安全影响与验证方式。

Never commit real OAuth responses or encrypted credential caches, even if they appear unreadable. / 即使内容看似不可读，也绝不要提交真实 OAuth 响应或加密凭据缓存。

## Style / 风格

- Use nullable reference types and keep warnings at zero. / 使用可空引用类型，并保持零警告。
- Prefer clear user-facing errors over raw server responses. / 面向用户显示清晰错误，不显示原始服务器响应。
- Keep the widget usable at 100%–200% Windows scaling. / 保证小组件在 Windows 100%–200% 缩放下可用。
- Update both Chinese and English documentation for user-visible changes. / 用户可见变更需同步更新中英文文档。
