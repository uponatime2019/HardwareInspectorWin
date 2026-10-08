# Contributing to New HwInspector

First off — thank you! Every bug report, feature idea, and pull request makes New HwInspector a better utility for everyone. Contributions of any size are welcome: fixing a typo in documentation is just as valuable as adding new hardware sensor monitors.

## Code of Conduct

Be kind, patient, and assume good faith. Harassment, discrimination, or hostile behavior of any kind is not tolerated.

## Ways to Help (No Code Required)

- **Report a bug** — open an issue describing what happened, your system specs, and steps to reproduce.
- **Suggest a feature** — describe the hardware component or diagnostic feature you'd like to see supported.
- **Improve documentation** — help clarify build instructions, hardware notes, or troubleshooting steps.
- **Star & share** — a star or recommendation helps other developers and PC enthusiasts discover the project.
- **Test on different hardware** — testing on various CPU, GPU, motherboard, and firmware architectures (Intel, AMD, NVIDIA, ARM64) provides invaluable telemetry and validation.

## Reporting a Bug

When filing a bug report, please include:

1. **App version** — e.g. `v1.0.0` (visible in the status bar or Settings page).
2. **Windows build & architecture** — e.g. Windows 11 23H2 (Build 22631), x64.
3. **Hardware configuration** — CPU, Motherboard, GPU model, and BIOS version if relevant.
4. **Steps to reproduce** — what screen or action triggered the issue.
5. **Logs** — local session logs located at `%LOCALAPPDATA%\NewHwInspector\logs\app_session_*.txt`.

## Setting up a Development Environment

Prerequisites:
- **Windows 10 (version 1809+) or Windows 11** (x64 or ARM64).
- **.NET 8 SDK** (or newer, targeting `net8.0-windows10.0.19041.0`).
- **Visual Studio 2022 17.8+** with the **.NET Desktop Development** and **Windows Application Development** (Windows App SDK / WinUI 3) workloads.

### Build and Run locally:

```powershell
# Clone the repository
git clone https://github.com/uponatime2019/NewHwInspector.git
cd NewHwInspector

# Build
dotnet build NewHwInspector.csproj -p:Platform=x64

# Run
dotnet run --project NewHwInspector.csproj -p:Platform=x64
```

> **Note**: Because the project targets multi-architecture `x86;x64;ARM64`, you must specify `-p:Platform=x64` (or `ARM64`) when invoking `dotnet build` or `dotnet publish`.

## Project Structure

| Path | Description |
|---|---|
| `App.xaml(.cs)` | Application entry point, global exception handlers, and theme management |
| `MainWindow.xaml(.cs)` | Primary window, navigation sidebar, real-time status bar |
| `Views/OverviewView.xaml(.cs)` | System summary dashboard (CPU, GPU, RAM, Motherboard, OS, Security) |
| `Views/HardwareView.xaml(.cs)` | Hierarchical hardware device tree with property inspectors |
| `Views/SensorsView.xaml(.cs)` | Real-time sensor monitoring grid (temperatures, clocks, loads, voltages) |
| `Views/ChartsView.xaml(.cs)` | Real-time interactive telemetry charts and trend visualizers |
| `Views/AlertsView.xaml(.cs)` | User-defined alert thresholds and trigger notifications |
| `Views/ReportsView.xaml(.cs)` | Multi-format diagnostic report generator (TXT, HTML, JSON, CSV) |
| `Views/SettingsView.xaml(.cs)` | Polling interval, temperature unit, theme, and storage configurations |
| `Services/HardwareService.cs` | WMI, registry, and OS API hardware discovery engine |
| `Services/SensorService.cs` | Polling loop, min/max/average statistics calculation, alert dispatch |
| `Services/ReportService.cs` | System and sensor snapshot exporters |
| `Services/AppSettings.cs` | Local JSON configuration persistence (`%LOCALAPPDATA%\NewHwInspector`) |
| `Helpers/WindowHelper.cs` | Native Win32 window icon and DPI presenter utilities |

## Guidelines for Contributions

- **Pure unpackaged & standalone**: Do not introduce dependencies on UWP/MSIX packaged APIs (`Package.Current`, `ApplicationData.Current`). Always use standard .NET base directories and local app data paths.
- **Privacy & local-only**: New HwInspector has zero telemetry endpoints, zero network tracking, and zero ads. All diagnostic queries are local. PRs introducing network phone-home mechanisms will be declined.
- **Keep dependencies lean**: Avoid large external dependencies when native Windows APIs or WMI provide the necessary information.
- **Clean builds**: Ensure `dotnet build NewHwInspector.csproj -p:Platform=x64` passes with 0 errors and 0 warnings.
