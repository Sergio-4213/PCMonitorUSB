# Security assessment — PC Monitor USB 2.3.1

Assessment date: August 13, 2026.

## Outcome

The tested build has no public network listener and no remotely supplied arbitrary-command path. Two weaknesses were found and fixed before producing version 2.1.1:

1. **Local sensor endpoints lacked authentication.** Commands required the token, but `/api/stats`, `/api/system`, and `/api/config` returned data to unauthenticated local requests. All `/api/*` endpoints now require the temporary token.
2. **Elevated startup referenced a user-writable portable EXE.** A malicious process already running as the same user could replace that file and wait for the elevated logon task. Automatic startup now copies the EXE and APK to a protected Program Files directory and points the elevated task there.

The existing scheduled task on the test PC was inspected but not modified during the assessment. Running 2.1.1 with automatic startup already enabled migrates its target to the protected location; enabling the option later performs the same protected installation after confirmation.

Version 2.2.0 adds Wake-on-LAN without creating a network listener. The authenticated server calculates the subnet broadcast from a real, active, physical Ethernet adapter. Android stores that validated configuration privately, accepts exactly six MAC bytes, rejects loopback/multicast/unspecified destinations, requires IPv4, and fixes the destination port to UDP 9.

Version 2.3.0 adds a real FPS source using the official standalone PresentMon 2.5.1 console binary. The component is embedded in the signed build, extracted only under the application's LocalAppData directory, and checked against the official SHA-256 before it can run. Its arguments are constant, it writes frame CSV data only to a private redirected pipe, creates no capture files, accepts no Android-supplied process/path/argument, and runs below normal priority.

Version 2.3.1 hardens Wake-on-LAN reliability without adding any inbound listener. The APK binds the datagram socket to the active Wi-Fi network when Android supports it, derives the current Wi-Fi subnet broadcast locally, and transmits only the fixed standard Wake-on-LAN payload to the authenticated server-provided target MAC. Destinations are limited to the validated configured broadcast, the locally derived Wi-Fi broadcast, and the IPv4 limited broadcast; ports are internally fixed to UDP 9 and 7. No destination, port, payload, or command can be supplied by an Android command request.

## Tests performed

### Network exposure

- Active listener verified as `127.0.0.1:8765` only.
- Requests to the machine's LAN and virtual-adapter IPv4 addresses on port 8765 failed.
- No `Server` implementation header was exposed.
- No permissive CORS header was present.
- Security headers include `Content-Security-Policy`, `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`, and `Permissions-Policy`.

### Authentication and request abuse

- Missing token: rejected with HTTP 401.
- Incorrect token: rejected with HTTP 401.
- Duplicate token header: rejected with HTTP 401.
- Correct token: authenticated sensor/configuration endpoints returned HTTP 200.
- Non-JSON command body: rejected with HTTP 400.
- Body larger than 8 KiB: rejected with HTTP 413.
- Immediate repeated command: rejected with HTTP 429.
- The token contains 192 random bits and is regenerated for each server process.

### Command injection

The allowlist rejected examples containing PowerShell, CMD arguments, path traversal, direct action names, command separators, oversized IDs, and disabled custom-button IDs. The Android request contains only a command ID. Program paths, URLs, and hotkeys cannot be supplied through the API.

### Wake-on-LAN boundary

- Broadcast calculation was tested for `/24` and `/16` IPv4 networks.
- The API exposes the Wake-on-LAN target only after token authentication.
- The destination is derived on Windows; the Android control screen has no editable address, MAC, or port field.
- The sender emits a standard 102-byte magic packet and opens no inbound socket.
- No router port forwarding, UPnP, internet endpoint, cloud relay, or arbitrary UDP payload was added.

### FPS/PresentMon boundary

- Embedded binary SHA-256: `9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191`, matching the official PresentMon 2.5.1 release asset.
- Extraction hash is verified before every replacement and an invalid temporary file is deleted.
- Command-line arguments and ETW session name are constants in the Windows application.
- FPS is calculated only from recent valid `MsBetweenPresents` samples for the foreground process and busiest swap chain; invalid, stale, missing, or implausible values become `null`/`--`.
- PresentMon runs as a hidden child process with below-normal priority and no output file; it is terminated with the Windows application.
- No DLL injection, game memory access, remote endpoint, inbound port, or arbitrary process selector was added.

### Dependencies and repository

- `dotnet list package --vulnerable --include-transitive`: no known vulnerable NuGet packages reported.
- Android `releaseRuntimeClasspath`: no third-party runtime dependencies.
- Repository secret-pattern scan: no private keys or common credential formats found.
- Build runtime: .NET 8.0.29, the current .NET 8 patch during the assessment.
- Android release lint and R8 build completed successfully.
- Microsoft Defender was enabled with real-time protection and current signatures on the build machine.

## Residual considerations

- The Windows EXE is not Authenticode-signed because no commercial code-signing certificate is configured. Download it only from the private GitHub release and verify the published SHA-256 hash.
- The current APK is signed and passes Android v1/v2/v3 verification, but the project still uses its existing Android debug certificate for upgrade compatibility. A protected long-term release key is recommended before broader distribution.
- A process that already has administrator access can control or replace other administrator-level software; this is outside the application's security boundary.
- Physical USB debugging must remain trusted. Revoke old debugging authorizations from Android Developer options if the PC or phone changes owner.
- No test can guarantee that compromise is impossible; the results apply to the reviewed source and generated 2.3.1 artifacts.
