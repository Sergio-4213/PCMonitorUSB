# Security policy

PC Monitor USB is designed for local USB operation. It does not expose a public network service, use cloud infrastructure, or accept arbitrary commands from the Android device.

## Supported version

Security fixes are applied to the newest release. Version 2.1.1 or later is required for authentication on every API endpoint and protected elevated startup.

## Security boundaries

- Kestrel binds only to `127.0.0.1`.
- Every `/api/*` request requires a temporary random 192-bit token.
- The token is regenerated when the Windows server starts and is delivered to the Android activity through the authorized ADB session.
- The phone sends command IDs only; executable paths and targets remain in the local Windows configuration.
- Request bodies are limited to 8 KiB and command requests are rate-limited.
- Android cleartext traffic is permitted only for `127.0.0.1`; app backup is disabled.
- Elevated automatic startup uses a protected Program Files copy rather than a user-writable portable executable.

## Reporting a vulnerability

Do not publish credentials, private logs, or exploit details in a public issue. While the repository is private, report findings directly to the repository owner. If the repository becomes public, enable GitHub private vulnerability reporting before accepting external reports.

Include the affected version, reproduction steps, expected impact, and whether physical USB access or an already-compromised local account is required.

## Limitations

No security review can prove that software is impossible to compromise. PC Monitor USB relies on Windows, Android, ADB authorization, installed drivers, and the physical security of the computer and phone. Keep Windows, Android Platform-Tools, GPU drivers, and the application updated.
