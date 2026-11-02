# 3cxKuando

Windows tray connector for **3CX v20 Presence** and **Call Control**, using the **kuandoHUB HTTP POST** integration.

## Status

**Early development / pre-release.** The kuandoHUB HTTP POST and priority-release protocol were validated through PowerShell against a live kuandoHUB, with one source and separate Presence/Alert priorities. The updated C# application and physical light effects still need field validation.

## Features

- One kuandoHUB source, `3CX Connector`, with separately configurable `Presence` and `Alert` event-type priorities
- Independent state/deduplication channels keyed by `(sender, event type)`; `Alert` changes never suppress a `Presence` update despite the shared sender
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
4. The connector registers one `3CX Connector` source with two event types (`Presence` and `Alert`) and adds two priority entries: `Presence / All events` and `Alert / All events`. In kuandoHUB, place `Alert` above `Presence` when calls must override presence. Other applications can be interleaved according to user preference.
5. On RDS, provide a session-specific kuandoHUB endpoint if its `http_port` registry value is unavailable; the connector deliberately does not guess port 8989.

The connector does **not** send an artificial Off on 3CX connection loss. When a call legitimately ends, it sends `action: Off` with `sender: 3CX Connector` and `eventtype: Alert` to release the call priority. Presence stays active until its value changes; when the call priority is released, kuandoHUB can resume the existing Presence event without a repeated POST. `Off` is a control action, **not** a selectable event. No automatic green fallback is sent on an unknown status.

**Migration from previous development versions:** Older builds registered `3CX Presence` and `3CX Call` with `Light / All` priorities. These old entries can remain in kuandoHUB; after confirming the new `3CX Connector` source and its two explicitly typed priorities, remove the obsolete entries **manually** from kuandoHUB. This connector intentionally does not delete, rearrange, or overwrite customized priority entries. The actual order established by `CreateInitialPriority` can depend on kuandoHUB's insertion behavior, so verify it in the UI.

**Multiple presence providers:** Consider choosing just one general/background presence provider when 3CX, Teams and Outlook are installed together. Other integrations can still supply focused call or meeting overrides. Both event types are transmitted only on change, startup or re-registration; ongoing presence does not require constant re-posting.

## Security

All settings are per user. API secret storage uses DPAPI CurrentUser. A user with the same Windows account can extract secrets using custom code. Do not distribute an elevated shared 3CX client secret to untrusted endpoints without accepting this risk. No client secrets or tokens should be committed to GitHub.

## Licensing

[PolyForm Noncommercial 1.0.0](LICENSE.md). Commercial use requires separate permission from the rights holder; the project is source-available, not OSI open source. The project does not claim endorsement by 3CX or Plenom.

## Testing notes

The automated tests exercise the single-source registration, explicit event-type priorities, all call animations, Off semantics, HTTP connection behavior and retry logic, as well as state and validation behavior. Windows CI captures Cobertura coverage reports. Coverage of WinForms, DPAPI, WebSocket, installation and hardware interactions remains incomplete; **100% total line coverage is not claimed**.

## Diagnostics

From the tray menu select **Open diagnostic log**. Per-user/per-session logs are
stored at `%LOCALAPPDATA%\3cxKuando\events-session-<session-ID>.log`.
The previous log is rotated as `.log.old` after about 512 KB.

The log includes 3CX state changes, local kuandoHUB POST operations and their HTTP
status (or exception class/socket code), registration failures and connection-health changes.
Sensitive credentials, raw API payloads, token values, and caller details are not logged.

The executable is `3cxKuando.exe`.

## Development builds

Only current `3cxKuando` configuration and log paths are supported; migration from older development builds is not included.
