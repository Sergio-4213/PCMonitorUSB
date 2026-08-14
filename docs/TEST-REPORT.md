# Test report — PC Monitor USB 2.3.0

Validated locally on August 13, 2026.

## Automated Windows suite

Command:

```powershell
.\.tools\dotnet\dotnet.exe run --project Windows\PCMonitorServer.Tests\PCMonitorServer.Tests.csproj -c Release --no-restore
```

Result: **13/13 passed**.

Covered scenarios:

- authorized and unauthorized ADB parsing;
- rejection of network ADB as a physical USB device;
- sensor selection by hardware/type/name/identifier priority;
- stable primary-GPU selection without mixing integrated and discrete sensors;
- configuration normalization, including invalid-language fallback;
- Wake-on-LAN IPv4 broadcast calculation for `/24` and `/16`, authenticated configuration publication, and fixed UDP port 9;
- PresentMon frame-time conversion, quoted CSV parsing, end-to-end CSV pipeline, embedded-resource extraction, official SHA-256 verification, and executable/version validation;
- Portuguese/English localization and built-in button labels;
- protected elevated-startup destination validation;
- command allowlist rejection against injection-style payloads;
- authentication on every API endpoint, invalid and duplicate token rejection, body-size limit, and command rate limiting;
- local API, temporary token, system profile, and Android language publication;
- Windows window bounds, title, Save button, and server toggle;
- real USB Android detection and communication;
- real LibreHardwareMonitor enumeration.

Real-machine snapshot during this run:

- Android: `SM-J410G`, communicating through USB;
- CPU: `AMD Ryzen 7 3800XT`;
- GPU: `AMD Radeon RX 7600`;
- sensors enumerated: 134;
- detected Wake-on-LAN adapter: `Ethernet 4` / Realtek PCIe GbE;
- detected local broadcast: `192.168.0.255`, UDP port 9;
- Windows reports the Ethernet adapter armed for wake and its driver reports Magic Packet and shutdown Wake-on-LAN enabled;
- PresentMon 2.5.1 embedded binary extracted and executed in version/help mode; official SHA-256 prefix `9BEC3083069F…` verified;
- live Android panel CPU temperature: 67 °C;
- live Android panel CPU usage: 49%;
- live Android panel CPU clock: 4.28 GHz;
- live Android panel CPU package power: 70 W;
- live Android panel GPU temperature: 57 °C at the sampled moment.

The UI test generated screenshots in `%TEMP%\PCMonitorUSBTests` and verified that the title and Save button remained inside the application window.

## Android build and static checks

Command:

```powershell
.\.tools\gradle-8.2.1\bin\gradle.bat -p Android clean lintRelease assembleRelease --no-daemon
```

Result: **BUILD SUCCESSFUL**.

Checks included English default resources, Brazilian Portuguese resources, an always-visible FPS placeholder when enabled, dedicated Wake-on-LAN views in portrait and landscape, continuous keep-screen-on behavior for the power screen, Java compilation, R8 optimization, resource shrinking, and release lint. The signed APK was installed as an in-place update on the real `SM-J410G`; Android reported version `2.3.0` / code `9`, and the app reconnected to the USB panel.

## Verification limits

Sensor availability depends on the connected PC and installed low-level driver support. The isolated test process could not read CPU temperature while the main elevated server already owned the active hardware-monitoring session; this did not represent the running application's result. A direct request to the live server confirmed valid CPU temperature, usage, clock, and package-power readings. No reboot was performed.

The complete power-off/power-on cycle was deliberately not executed because the user requested no meeting interruption. The machine-side prerequisites, packet construction, target validation, real Ethernet configuration, Android installation, disconnection state logic, compilation, and reconnection path were tested. A final physical Wake-on-LAN test still depends on the motherboard firmware accepting the packet from the S5 state.

FPS was not fabricated for the report. Synthetic 16.666/16.667 ms samples verified the exact 60 FPS calculation, while the official executable, version, hash, CSV parser, API publication, and Android placeholder were validated. Live ETW capture requires the elevated Windows application, as declared by its manifest; a numeric in-game sample depends on a compatible game being foreground and actively presenting frames during the test. Otherwise the UI intentionally displays `FPS --`.
