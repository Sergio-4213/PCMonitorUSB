# Security assessment — PC Monitor USB 2.1.1

Assessment date: August 11, 2026.

## Outcome

The tested build has no public network listener and no remotely supplied arbitrary-command path. Two weaknesses were found and fixed before producing version 2.1.1:

1. **Local sensor endpoints lacked authentication.** Commands required the token, but `/api/stats`, `/api/system`, and `/api/config` returned data to unauthenticated local requests. All `/api/*` endpoints now require the temporary token.
2. **Elevated startup referenced a user-writable portable EXE.** A malicious process already running as the same user could replace that file and wait for the elevated logon task. Automatic startup now copies the EXE and APK to a protected Program Files directory and points the elevated task there.

The existing scheduled task on the test PC was inspected but not modified during the assessment. Installing 2.1.1 and saving the automatic-startup setting migrates its target to the protected location.

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

### Dependencies and repository

- `dotnet list package --vulnerable --include-transitive`: no known vulnerable NuGet packages reported.
- Android `releaseRuntimeClasspath`: no third-party runtime dependencies.
- Repository secret-pattern scan: no private keys or common credential formats found.
- Build runtime: .NET 8.0.29, the current .NET 8 patch during the assessment.
- Android release lint and R8 build completed successfully.
- Microsoft Defender was enabled with real-time protection and current signatures on the build machine.

## Residual considerations

- The Windows EXE is not Authenticode-signed because no commercial code-signing certificate is configured. Download it only from the private GitHub release and verify the published SHA-256 hash.
- The current private-preview APK is signed and passes Android v1/v2/v3 verification, but a dedicated long-term release key should be protected before public distribution.
- A process that already has administrator access can control or replace other administrator-level software; this is outside the application's security boundary.
- Physical USB debugging must remain trusted. Revoke old debugging authorizations from Android Developer options if the PC or phone changes owner.
- No test can guarantee that compromise is impossible; the results apply to the reviewed source and generated 2.1.1 artifacts.
