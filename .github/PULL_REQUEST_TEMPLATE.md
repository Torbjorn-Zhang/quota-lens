## What changed / 变更内容

<!-- Describe the change and why it is needed. / 描述改动及原因。 -->

## User impact / 用户影响

<!-- Describe visible behavior and compatibility. / 描述可见行为与兼容性。 -->

## Security and privacy / 安全与隐私

- [ ] No real tokens, account IDs, auth files, or credential caches are included.
- [ ] 未包含真实 token、账号 ID、登录文件或凭据缓存。
- [ ] Credential handling remains read-only and in memory, or the change is explained below.
- [ ] 凭据处理仍保持只读且仅驻留内存，或已在下方说明变化。

## Validation / 验证

- [ ] `dotnet build .\QuotaLens.csproj -c Release --warnaserror`
- [ ] `dotnet run --project .\tests\QuotaLens.Tests\QuotaLens.Tests.csproj -c Release`
- [ ] Manual Windows UI check when applicable / 如适用，已进行 Windows 界面检查
