# Windows Dock

Windows Dock is a standalone Windows 11 companion dock. It displays pinned and running applications on a centered translucent surface above the current monitor work area's bottom edge.

Open the three-dot menu to access Customization Studio or exit. Right-click an application to pin, unpin, or choose a window. Studio changes icon size, opacity, corner radius, elevation, and clock visibility live. Network, audio, battery, and time controls open the corresponding Windows Settings pages. Explorer continues to handle the native taskbar and tray.

## Build and run

From the repository root with the .NET 10 SDK on Windows:

```powershell
dotnet build src/WindowsDock -c Release
dotnet publish src/WindowsDock -c Release -r win-x64 --self-contained -o artifacts/win-x64
.\artifacts\win-x64\WindowsDock.exe
```

Keep the complete publish directory together. Run with `--safe-mode` to load defaults and keep edits in memory for the session. Normal settings are stored in `%LOCALAPPDATA%\WindowsDock\dock.json` with a previous-version backup.

See the [architecture and feasibility matrix](docs/ARCHITECTURE.md) and [roadmap](ROADMAP.md) for the remaining product milestones.
