# Test report — PC Monitor USB 2.1.0

Validated locally on August 11, 2026.

## Automated Windows suite

Command:

```powershell
.\.tools\dotnet\dotnet.exe run --project Windows\PCMonitorServer.Tests\PCMonitorServer.Tests.csproj -c Release --no-restore
```

Result: **10/10 passed**.

Covered scenarios:

- authorized and unauthorized ADB parsing;
- rejection of network ADB as a physical USB device;
- sensor selection by hardware/type/name/identifier priority;
- stable primary-GPU selection without mixing integrated and discrete sensors;
- configuration normalization, including invalid-language fallback;
- Portuguese/English localization and built-in button labels;
- command allowlist rejection;
- local API, temporary token, system profile, and Android language publication;
- Windows window bounds, title, Save button, and server toggle;
- real USB Android detection and communication;
- real LibreHardwareMonitor enumeration.

Real-machine snapshot during this run:

- Android: `SM-J410G`, communicating through USB;
- CPU: `AMD Ryzen 7 3800XT`;
- GPU: `AMD Radeon RX 7600`;
- sensors enumerated: 134;
- live server CPU temperature: 63.88 °C;
- live server CPU usage: 43.33%;
- live server CPU clock: 4.325 GHz;
- live server CPU package power: 68.06 W;
- GPU temperature: 61 °C at the sampled moment.

The UI test generated screenshots in `%TEMP%\PCMonitorUSBTests` and verified that the title and Save button remained inside the application window.

## Android build and static checks

Command:

```powershell
.\.tools\gradle-8.2.1\bin\gradle.bat -p Android clean lintRelease assembleRelease --no-daemon
```

Result: **BUILD SUCCESSFUL**.

Checks included English default resources, Brazilian Portuguese resources, portrait layout, landscape layout, Java compilation, R8 optimization, resource shrinking, and release lint.

## Verification limits

Sensor availability depends on the connected PC and installed low-level driver support. The isolated test process could not read CPU temperature while the main elevated server already owned the active hardware-monitoring session; this did not represent the running application's result. A direct request to the live server confirmed valid CPU temperature, usage, clock, and package-power readings. No reboot was performed.
