# 3cxKuando

Windows tray connector for **3CX v20 Presence** and **Call Control**, using the **kuandoHUB HTTP POST** integration.

## Status

**Early development / pre-release.** The integration has not yet been validated against a live kuandoHUB / 3CX production environment. The source is intended for testing; verify all light patterns and source-priority behavior before relying on it.

## Features

- Two independent kuandoHUB sources: `3CX Presence` and `3CX Call`
- Presence profiles via 3CX XAPI; call state via Call Control REST + WebSocket updates
- Automatic reconnect and OAuth token renewal
- Per-Windows-user configuration in `%APPDATA%\3cxKuando`
- Client secret encrypted using Windows DPAPI `CurrentUser` (not protection against processes running as the same user)
- kuandoHUB auto-discovery via `HKCU\Software\Busylight\http_port` or `localhost:8989` on non-RDS systems
- Windows tray settings, per-user logon start, independent call/presence state

## Requirements

- Windows 10/11 or a supported Windows Server RDS session (x64)
- 3CX v20 service principal with Configuration API permission to read the target extension's `CurrentProfileName` and `IsRegistered`
- Call Control permission with the extension assigned (required for call states)
- kuandoHUB HTTP POST connector enabled
- An active kuandoHUB instance in the current user session

## Build

```powershell
dotnet restore 3cxKuando.slnx
dotnet build 3cxKuando.slnx -c Release
dotnet test 3cxKuando.slnx -c Release
dotnet publish src/3cxKuando.App/3cxKuando.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/publish
```

Windows CI compiles an installer using Inno Setup 6. Official manual releases can be dispatched via GitHub Actions / Publish Release, with `0.1.0-beta.1` for prereleases or `0.1.0` for stable releases.

## Per-user setup

1. Install the Windows installer and start the tray application.
2. Enter HTTPS PBX URL, service principal client ID, secret, and extension.
3. Configure kuandoHUB HTTP POST in kuandoHUB settings.
4. The connector registers `3CX Presence` and `3CX Call` sources and sends status changes; kuandoHUB applies the configured priorities.
5. On RDS, provide a session-specific kuandoHUB endpoint if its `http_port` registry value is unavailable; the connector deliberately does not guess port 8989.

The connector does **not** switch the light off on 3CX connection loss. When a call legitimately ends, it sends the sender-specific kuandoHUB `Off` action; the exact priority semantics still require a device integration test.

## Security

All settings are per user. API secret storage uses DPAPI CurrentUser. A user with the same Windows account can extract secrets using custom code. Do not distribute an elevated shared 3CX client secret to untrusted endpoints without accepting this risk. No client secrets or tokens should be committed to GitHub.

## Licensing

[PolyForm Noncommercial 1.0.0](LICENSE.md). Commercial use requires separate permission from the rights holder; the project is source-available, not OSI open source. The project does not claim endorsement by 3CX or Plenom.

## Testing notes

The automated tests cover foundational state, validation and protocol behaviors. Complete coverage of WinForms, DPAPI, WebSocket, installation and hardware interaction remains future work; the current project **does not claim 100% line coverage**.

## Diagnostics

From the tray menu select **Open diagnostic log**. Per-user/per-session logs are
stored at `%LOCALAPPDATA%\3cxKuando\events-session-<session-ID>.log`.
The previous log is rotated as `.log.old` after about 512 KB.

The log includes 3CX state changes, local kuandoHUB POST operations and their HTTP
status (or exception class/socket code), registration failures and connection-health changes.
Sensitive credentials, raw API payloads, token values, and caller details are not logged.

The executable is `3cxKuando.exe`.

## Upgrading from development builds

Existing settings and the DPAPI-protected secret are copied on first launch from the
previous per-user configuration location if the new destination files do not exist.
Existing diagnostic logs are preserved. The earlier Windows autostart entry is removed
when the application starts or settings are saved. Before upgrading, exit the running
tray application.
