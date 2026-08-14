# Security policy

PC Monitor USB is designed for local USB operation. It does not expose a public network service, use cloud infrastructure, or accept arbitrary commands from the Android device.

## Supported version

Security fixes are applied to the newest release. Version 2.3.1 is the current supported version; 2.1.1 introduced authentication on every API endpoint and protected elevated startup.

## Security boundaries

- Kestrel binds only to `127.0.0.1`.
- Every `/api/*` request requires a temporary random 192-bit token.
- The token is regenerated when the Windows server starts and is delivered to the Android activity through the authorized ADB session.
- The phone sends command IDs only; executable paths and targets remain in the local Windows configuration.
- Request bodies are limited to 8 KiB and command requests are rate-limited.
- Android cleartext traffic is permitted only for `127.0.0.1`; app backup is disabled.
- Elevated automatic startup uses a protected Program Files copy rather than a user-writable portable executable.
- Wake-on-LAN opens no listening port. The APK accepts only an authenticated, server-generated Ethernet MAC/subnet broadcast pair and sends only local broadcasts to internal fixed UDP ports 9 and 7.
- FPS capture uses the official embedded PresentMon 2.5.1 console binary. Its SHA-256 is pinned and verified before extraction/execution; it runs hidden at below-normal priority, writes no capture CSV, and adds no network listener or game injection.

## Reporting a vulnerability

Do not publish credentials, private logs, or exploit details in a public issue. Report findings directly to the repository owner or use GitHub private vulnerability reporting when it is enabled.

Include the affected version, reproduction steps, expected impact, and whether physical USB access or an already-compromised local account is required.

## Limitations

No security review can prove that software is impossible to compromise. PC Monitor USB relies on Windows, Android, ADB authorization, installed drivers, and the physical security of the computer and phone. Keep Windows, Android Platform-Tools, GPU drivers, and the application updated.
