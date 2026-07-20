# Privacy

English | [简体中文](PRIVACY.md)

## Data read

- Codex OAuth token and ChatGPT account ID
- Claude Code OAuth token, or the encrypted Claude Desktop sign-in cache
- Plan, usage percentage, reset time, and credit information returned by the official usage services

## Data destinations

- Codex credentials are sent only to `https://chatgpt.com/backend-api/wham/usage`
- Claude credentials are sent only to `https://api.anthropic.com/api/oauth/usage`
- The app contains no analytics, telemetry, advertising, or third-party relay service

## Local storage

`%LOCALAPPDATA%\QuotaLens\settings.json` stores only the refresh interval, launch-at-sign-in preference, always-on-top state, opacity, and window position. When launch at sign-in is enabled, the executable path is stored in the current user's Windows `Run` registry key.

OAuth tokens, raw usage responses, and account IDs are never written to Quota Lens files or logs. Decrypted Claude Desktop tokens and master keys exist only in process memory, and their byte buffers are zeroed after use.

## Removing data

Exit Quota Lens and delete `%LOCALAPPDATA%\QuotaLens` to remove its settings. Disable launch at sign-in from the tray menu to remove the registry entry. Neither action deletes Claude or Codex sign-in data.
