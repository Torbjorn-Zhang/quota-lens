# Quota Lens

English | [简体中文](README.md)

[![CI](https://github.com/Torbjorn-Zhang/quota-lens/actions/workflows/ci.yml/badge.svg)](https://github.com/Torbjorn-Zhang/quota-lens/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Torbjorn-Zhang/quota-lens?display_name=tag)](https://github.com/Torbjorn-Zhang/quota-lens/releases)
[![License](https://img.shields.io/github/license/Torbjorn-Zhang/quota-lens)](LICENSE)

Quota Lens is a lightweight, translucent Windows desktop widget that shows subscription usage, reset times, and remaining quota for **Claude Code** and **Codex**.

![Quota Lens preview](docs/images/preview.png)

> [!IMPORTANT]
> This is an unofficial community project. It is not affiliated with or endorsed by Anthropic or OpenAI. The usage endpoints are not stable public APIs and upstream changes may temporarily break the app.

## Features

- Displays the 5-hour, 7-day, and other available quota windows for Codex and Claude Code
- Automatically detects model-scoped weekly allowances such as Fable 5 and hides the extra row when none is returned
- Shows plan details, reset countdowns, extra credits, and model-specific weekly allowances
- Refreshes Codex every 60 seconds and Claude no more than once every 3 minutes
- Applies 5/10/20/30-minute backoff after Claude HTTP 429 responses while keeping the last successful result
- Sends a Windows notification when remaining quota falls below 20%
- Translucent movable widget with always-on-top, tray mode, and opacity controls
- Turns off all displays with one click while preventing automatic system sleep; mouse or keyboard input wakes the displays
- Optional launch at Windows sign-in
- Never persists OAuth tokens or writes credential/request logs

## Requirements

- Windows 10 or Windows 11 (x64)
- Codex signed in with a ChatGPT account
- Claude Code signed in with a Claude subscription, or the Microsoft Store build of Claude Desktop signed in

API-key, Bedrock, Vertex, and other metered accounts generally do not expose the same subscription quota percentages and are not supported.

## Install

1. Download the latest `QuotaLens-*-win-x64.zip` from [Releases](https://github.com/Torbjorn-Zhang/quota-lens/releases).
2. Extract it to a permanent folder.
3. Run `QuotaLens.exe`.
4. Enable “Start with Windows” from the tray menu if desired.

The current release is not commercially code-signed, so Windows SmartScreen may show a warning on first launch. Download only from this repository's Releases page, or build from source.

## Use

- Drag the top header to move the widget.
- Select `↻` to refresh and `◇` to toggle always-on-top.
- Select the moon button to turn off the displays while keeping the computer awake.
- `×` hides the window to the tray; choose “Exit” from the tray menu to stop the app.
- Hovering over the widget temporarily increases its opacity.

## Privacy and security

Quota Lens reads the current Windows user's existing sign-in state and sends credentials only to the matching official service:

- Codex: `%USERPROFILE%\.codex\auth.json` or `CODEX_HOME`
- Claude Code: `%USERPROFILE%\.claude\.credentials.json` or `CLAUDE_CONFIG_DIR`
- Claude Desktop: Electron safe storage protected by Windows DPAPI, decrypted only in memory

Tokens are never written to Quota Lens settings or sent to third parties. See the [privacy notes](docs/PRIVACY.en.md) and [security policy](SECURITY.md) for details.

## Build from source

Install the [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0), then run:

```powershell
dotnet restore .\QuotaLens.csproj
dotnet build .\QuotaLens.csproj -c Release
dotnet run --project .\QuotaLens.csproj -c Release
```

Run the parser checks:

```powershell
dotnet run --project .\tests\QuotaLens.Tests\QuotaLens.Tests.csproj -c Release
```

Create a single-file release:

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1
```

By default, the output at `artifacts\win-x64-v<version>\QuotaLens.exe` includes the .NET runtime. Pass `-FrameworkDependent` for a smaller framework-dependent build.

## Project documentation

- [Architecture](docs/ARCHITECTURE.en.md)
- [Privacy](docs/PRIVACY.en.md)
- [Roadmap](ROADMAP.md)
- [Contributing](CONTRIBUTING.md)
- [Changelog](CHANGELOG.md)
- [Security](SECURITY.md)

## Contributing

Issues and pull requests are welcome. Never include real tokens in screenshots, logs, fixtures, or issues. See [CONTRIBUTING.md](CONTRIBUTING.md) for the full workflow.

## License

[MIT](LICENSE) © 2026 Torbjorn-Zhang
