# 3cxKuando

Windows tray connector for **3CX v20 Presence** and **Call Control**, integrated into **kuandoHUB using the official Plenom Busylight SDK**.

## Status

**Early development / pre-release.** This branch uses the official `com.plenom.BusylightSDK` NuGet package, version `4.0.7`. SDK public methods were verified with a Windows .NET reflection probe. Automated tests cover the adapter boundary with a mock SDK; behavior with a physical light and an installed kuandoHUB must still be validated on Windows.

The previously developed HTTP transport is archived separately in `legacy/http-transport` and is intentionally not part of `main`.

## Features

- One kuandoHUB data source, `3CX Connector`, with independently configurable `Presence` and `Alert` event-type priorities
- Separate state channels keyed by `(sender, event type)` – an Alert event cannot overwrite Presence deduplication
- Presence profiles from 3CX XAPI; call state from 3CX Call Control REST with immediate WebSocket wake-ups and timed fallback polling
- Automatic 3CX reconnect, OAuth token renewal, SDK re-registration and event replay
- Per-Windows-user settings at `%APPDATA%\3cxKuando`; client secret protected using Windows DPAPI CurrentUser
- Windows tray interface, per-user autostart, independent call and presence processing

## Requirements

- Windows 10/11 or supported Windows Server user session (x64)
- .NET 10 SDK for building; published installer provides a self-contained runtime
- 3CX v20 service principal with Configuration API permission to read the target extension's `CurrentProfileName`
- Call Control permission with the target extension assigned
- An installed, running, SDK-compatible kuandoHUB in **the same Windows user session** as the tray connector
- Plenom SDK through NuGet (restored at build time). No HTTP API, HTTP port, access token, URL override or manual listener setup is needed.

**Priority safety:** The connector checks `IsHubConnected` before sending light effects and requires a running kuandoHUB. The Plenom `KuandoSDK` itself is hybrid: when kuandoHUB is unavailable, it can switch to standalone USB operation. We instantiate it with `slavemode: true` so that initialization and shutdown do not change the light state (according to the SDK XML documentation). **Slave mode does not prevent direct USB fallback**, nor does a prior connection check provide a hard guarantee against a hub disconnect during an SDK call. Priority entries can be moved in kuandoHUB by the user; verify `Alert` is above `Presence` and that the relative order against Teams/Outlook is appropriate.

## Build and automated tests

```powershell
dotnet restore 3cxKuando.slnx
dotnet build 3cxKuando.slnx -c Release
dotnet test 3cxKuando.slnx -c Release
dotnet publish src/3cxKuando.App/3cxKuando.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/publish
```

GitHub Actions builds and tests on Windows, collects coverage, and builds an Inno Setup 6 installer. Physical integration, SDK/kuandoHUB attachment, priority ordering, terminal sessions and animations require separate manual validation.

## Per-user setup

1. Install the Windows application and start its tray icon **in the interactive Windows user session**.
2. Enter the HTTPS PBX URL, 3CX service-principal client ID, secret and extension.
3. Ensure kuandoHUB is running. The official SDK discovers it automatically; there is no HTTP integration to enable.
4. The connector registers one `3CX Connector` source with event types `Presence` and `Alert`, and requests separate `All events` priority entries. Adjust priorities in kuandoHUB so active calls can override Presence while preserving other applications' desired priorities.

Previous development builds may have left old `3CX Presence`, `3CX Call`, or duplicate `3CX Connector` priorities. After checking the new entries, remove obsolete duplicates **manually** in kuandoHUB. The application does not delete or rearrange user-customized entries.

## Event model and animations

| 3CX state | kuandoHUB event type | Default effect |
| --- | --- | --- |
| Available | Presence | Light (green) |
| Away | Presence | Light (yellow) |
| OutOfOffice / DND | Presence | Light (purple) |
| Custom1 / Custom2 | Presence | Light (configurable) |
| Dialing | Alert | Light (blue) |
| Ringing | Alert | Blink (blue) |
| Connected | Alert | Light (red) |
| Hold / Held | Alert | Pulse (yellow) |

The default Hold/Held color is yellow on the 0–100 RGB scale (`100, 100, 0`). Older saved configuration files are upgraded from the previous default red Pulse (`100, 0, 0`) when loaded. Explicitly different custom colors or effects are preserved. A custom style identical to the old default cannot be distinguished from that default and will also be migrated.

**Light** is steady illumination. **Blink** alternates between illuminated and off phases; by default the SDK receives values `5, 5` for on/off timing. **Pulse** gradually changes intensity using a seven-step sequence (3, 21, 36, 50, 36, 21, 10). Effect colors are specified on the same 0–100 scale as the existing configuration.

When a call completes, the application uses the SDK's `EndEvent("Alert", previousEventName)` for the last active event (such as `Connected`), without clearing `Presence`. When an event changes within a channel, only the previous event in that same channel is ended. The SDK's global `AutomaticEndEventMode` is disabled to prevent call events from clearing presence events. `Off` is **not** a selectable call state or registered event name and is never sent as an event name. No artificial ending is emitted at startup or on a failed 3CX read. Active calls are replayed after successful hub re-registration; a successfully ended call is not replayed.

Monitor restarts are serialized: the previous 3CX/WebSocket/SDK monitor must be stopped before a new SDK instance starts. Invalid or unknown Call Control responses are treated as errors (not as `Idle`), avoiding inadvertent call-priority removal.

Important SDK behavior to field-check: the Plenom SDK identifies the event by type and name. Animation and priority handling must be checked with installed kuandoHUB, especially after reconnect and on multi-user hosts. The existence and signatures of the SDK methods have been verified, but real hardware behavior is not established by unit tests.

## Security

Credentials are specific to each Windows user. 3CX API client secrets are stored with DPAPI `CurrentUser`; this does not prevent another process running as the same user from extracting them. Do not deploy an elevated shared 3CX secret to untrusted endpoints without accepting this risk. Never commit tokens, secrets or call details.

## Licensing

[PolyForm Noncommercial 1.0.0](LICENSE.md). Commercial use requires the rights holder's permission. The application is source-available, not OSI-open-source, and is not affiliated with 3CX or Plenom.

**Third-party SDK:** `com.plenom.BusylightSDK` 4.0.7 carries Plenom's separate proprietary `license.txt` (Product License Agreement for Busylight UC), including restrictions on third-party integration and redistribution. The project's PolyForm license applies only to the author's own code and does not grant rights to redistribute Plenom's binaries. Obtain written permission from Plenom before publicly distributing installers or deploying the connector commercially; the exact rights for this integration remain unconfirmed.

## Diagnostics

Tray menu → **Open diagnostic log**. Session-scoped logs are stored under `%LOCALAPPDATA%\3cxKuando\events-session-<session-ID>.log` and rotated to `.log.old` after approximately 512 KB. Logs record 3CX observations, SDK registration, events and connection-health changes, without credentials, API payloads or caller details.

The executable is `3cxKuando.exe`.

## Migration

No HTTP transport is shipped from `main`. Existing per-user JSON settings can retain obsolete fields from previous development builds; unknown fields are ignored by the current settings deserializer. The legacy branch remains available as a reference for the previous transport.
