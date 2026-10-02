# Privacy

English | [简体中文](PRIVACY.md)

## Data read

- Codex OAuth token and ChatGPT account ID
- Claude Code OAuth token, or the encrypted Claude Desktop sign-in cache
- macOS: the “Claude Safe Storage” keychain item (Claude Desktop's safe-storage password) and “Claude Code-credentials” (the Claude Code CLI sign-in). They are read only after you approve macOS's access prompt; the read goes through Security.framework directly, so “Always Allow” trusts Quota Lens alone
- Plan, usage percentage, reset time, and credit information returned by the official usage services

## Data destinations

- Codex credentials are sent only to `https://chatgpt.com/backend-api/wham/usage`
- Claude credentials are sent only to `https://api.anthropic.com/api/oauth/usage`
- The app contains no analytics, telemetry, advertising, or third-party relay service

## Local storage

`%LOCALAPPDATA%\QuotaLens\settings.json` stores only the refresh interval, launch-at-sign-in preference, always-on-top state, opacity, window position, and docked edge. When launch at sign-in is enabled, a per-user Task Scheduler logon task named `QuotaLens` stores the executable path; the current user's `Run` registry key is used only as a fallback when Task Scheduler is unavailable.

`%LOCALAPPDATA%\QuotaLens\startup.log` records start, exit, autostart registration results, and unhandled errors. Each line holds only a timestamp, the version, launch arguments, and a short message; the last 200 lines are kept.

On macOS, settings and the log live in `~/Library/Application Support/QuotaLens/` (`settings.json` holds only the refresh interval, launch at login, and low-quota alert state). The log additionally records a line whenever quota availability changes, containing only success or failure and the friendly error text shown in the UI. When launch at login is on, the app writes `~/Library/LaunchAgents/io.github.torbjorn-zhang.quotalens.plist`, which holds only the app path.

OAuth tokens, raw usage responses, and account IDs are never written to Quota Lens files or logs. Decrypted Claude Desktop tokens and master keys exist only in process memory, and their byte buffers are zeroed after use. On macOS the decryption key derived from the keychain password stays in process memory until exit so that refreshes do not ask for keychain access again; the keychain password itself is zeroed right after use.

## Removing data

Exit Quota Lens and delete `%LOCALAPPDATA%\QuotaLens` to remove its settings and log. Disable launch at sign-in from the tray menu to remove the logon task (and any legacy registry entry). Neither action deletes Claude or Codex sign-in data.

On macOS, first turn off “Launch at login” in the menu (which removes the LaunchAgent above) and quit, then delete `~/Library/Application Support/QuotaLens` and `/Applications/QuotaLens.app`. To revoke keychain access, open “Claude Safe Storage” in Keychain Access and remove Quota Lens under Access Control.
