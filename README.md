# PC Monitor USB 2.2.0

[Português (Brasil)](README.pt-BR.md)

PC Monitor USB turns a compatible Android phone into a lightweight USB hardware display and control panel for Windows. Normal operation requires no Wi-Fi, internet connection, cloud service, account, telemetry, or subscription.

## Main features

- CPU temperature, usage, current clock, and package power when exposed by the hardware.
- GPU temperature, hotspot, usage, core/VRAM clocks, VRAM, power, and fan readings when available.
- RAM, optional network throughput, and disk activity.
- Separate, responsive Monitor and Control modes in portrait and landscape.
- Media, volume, desktop, Task Manager, Steam, AMD Software, and locally configured allowlisted actions.
- Automatic USB detection, `adb reverse`, APK installation/update, and app launch after initial authorization.
- Local-only server bound to `127.0.0.1`.
- English and Brazilian Portuguese on Windows and Android.
- Optional local Wake-on-LAN screen for turning the PC on from the phone while the USB panel is disconnected.

## Language

On first run, the Windows app follows the Windows display language: Portuguese systems use Portuguese, and other systems use English. You can override this under **Settings > Language** with **Automatic (Windows)**, **Português**, or **English**. Reopen the Windows app after changing this option.

The selected Windows language is sent through the local USB API, so the Android panel automatically uses the same language. No online translation service is used.

## Initial setup

1. On the phone, enable **Developer options** by tapping **Build number** seven times.
2. Enable **USB debugging**.
3. Run `PCMonitorServer.exe` and accept the administrator prompt. Elevated access improves hardware sensor coverage.
4. Select **Set up phone**. On first use, approve the official Android Platform-Tools download.
5. Connect the phone with a data-capable USB cable.
6. Unlock the phone, accept the RSA authorization, and select **Always allow from this computer**.
7. Wait for the APK to install and open automatically.

After setup, daily use is simply: start the PC and connect the USB cable. The cable carries data and powers the phone.

## Windows application

The **Overview** page shows the real configuration detected on the current PC: Windows version, motherboard, CPU, primary/additional GPUs, and installed RAM. It also provides **Turn server on/off** and **Set up phone**. Minimizing or closing the window sends it to the notification area instead of leaving it on the taskbar.

The **Sensors** page lists hardware, sensor type, value, and stable identifiers. It can export `sensors.txt` for troubleshooting. LibreHardwareMonitor is the primary source; missing values are displayed as `--` and are never invented.

On systems where CPU temperature, clock, or power needs lower-level access, **Extend sensor support** can download the official PawnIO installer after explicit confirmation and SHA-256 verification. PC Monitor USB never restarts the computer automatically.

## Android panel

- **Monitor** prioritizes detailed CPU/GPU/RAM/VRAM data.
- **Control** keeps a compact live summary and uses the remaining space for large, aligned buttons.
- Portrait and landscape have independent layouts.
- The `⋮` menu controls activity-only brightness and optional screen protection.
- If communication is lost, stale values are replaced with `--` and visually dimmed.
- When Wake-on-LAN is enabled, a disconnected panel switches to a dedicated **Power on PC** screen. The phone stores only the validated PC name, Ethernet MAC address, subnet broadcast, and fixed UDP port 9 received through the authenticated USB API.

## Turn the PC on from the phone

Wake-on-LAN is optional and is used only while the PC is off; normal monitoring and controls still use USB only.

1. Connect the PC to the router with an Ethernet cable. The phone can use the same router over Wi-Fi.
2. Enable Wake-on-LAN or **Resume by PCI-E/Networking Device** in the PC firmware, and enable **Wake on Magic Packet** for the Ethernet adapter in Windows.
3. Under **Settings**, leave **Show 'Turn computer on' on the phone while disconnected** enabled.
4. Enable **Start with Windows** so the server, ADB reverse, and Android panel reconnect automatically after the PC boots.
5. Connect the phone once while the server is running. The authenticated configuration is stored privately by the APK.

When the USB connection disappears, open or keep the Android app on screen and tap **Power on PC**. The app sends three standard magic packets to the local subnet. It does not contact the internet, a cloud server, or a public relay. Wake-on-LAN support from a complete shutdown varies by motherboard firmware, Ethernet adapter, driver, and Windows power state; Wi-Fi-only PCs commonly cannot use this method.

## Security

The phone sends only allowlisted command IDs. It cannot submit arbitrary executable paths, PowerShell, CMD, or shell commands. Custom action targets are stored and validated on Windows.

The HTTP server listens only on `127.0.0.1`; USB transport uses authenticated ADB reverse. Every `/api/*` endpoint requires a temporary 192-bit token that is regenerated whenever the Windows server starts. Requests have strict body-size limits, commands are rate-limited, and duplicate or invalid tokens are rejected. There is no public binding, router port forwarding, UPnP, analytics, or telemetry.

Wake-on-LAN adds no listening socket. The Android app validates the server-provided MAC address, IPv4 destination, and fixed UDP port 9 before sending a standard local broadcast. It cannot choose a target through the command API.

When automatic startup is enabled, the elevated scheduled task points to a protected copy under Program Files instead of a user-writable portable EXE. See [SECURITY.md](SECURITY.md) and the [security test report](docs/SECURITY-REPORT.md).

## Compatibility

- Windows 10/11 x64.
- AMD, Intel, or NVIDIA hardware supported by LibreHardwareMonitor.
- Android 5.0/API 21 or later; Android 8.1 Go is a primary target.
- No Android Studio or separate ADB installation is required for normal use.

Exact sensor availability depends on the motherboard, GPU, firmware, and driver. The application does not change BIOS, PBO, overclock, undervolt, drivers, power plans, or GPU tuning.

## Local data

- Configuration: `%LOCALAPPDATA%\PCMonitorUSB\config.json`
- Rotating log: `%LOCALAPPDATA%\PCMonitorUSB\logs\app.log`
- Platform-Tools: `%LOCALAPPDATA%\PCMonitorUSB\platform-tools`

## Build

Windows requires the .NET 8 SDK:

```powershell
dotnet publish Windows\PCMonitorServer\PCMonitorServer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o Release-v2.2.0
```

Android requires JDK 17, Gradle 8.2.1, and Android SDK 34. Run `assembleRelease`, then align and sign the APK.

## Project structure

```text
PCMonitorUSB/
|-- Windows/PCMonitorServer/
|-- Windows/PCMonitorServer.Tests/
|-- Android/app/src/main/java/com/pcmonitorusb/
|-- Android/app/src/main/res/layout/
|-- Android/app/src/main/res/layout-land/
|-- docs/
`-- README.pt-BR.md
```

References: [ADB reverse](https://developer.android.com/develop/ui/views/layout/webapps/access-local-server), [Android Debug Bridge](https://developer.android.com/tools/adb), [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), [Microsoft Wake-on-LAN behavior](https://learn.microsoft.com/en-us/troubleshoot/windows-client/setup-upgrade-and-drivers/wake-on-lan-feature), and the [MSI B550M PRO-VDH WIFI manual](https://download.msi.com/archive/mnu_exe/mb/B550MPRO-VDHWIFICEC.pdf).
