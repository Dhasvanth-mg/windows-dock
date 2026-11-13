# Windows Dock

Windows Dock styles Explorer's taskbar in place on Windows 11 x64 25H2 (build 26200). It centers the native app list, tray, and clock on one dark glass surface. The grid button opens a dashboard from the same screen edge. Native app switching, jump lists, window previews, Start, notifications, and Quick Settings remain Explorer features.

## Run the native dock

Keep the contents of the native publish directory together and run NativeTaskbar.exe. Click the grid button beside the tray to open the dashboard. Ctrl+Alt+D, the Windows Dock notification-area menu, and the dashboard's Settings page can restore the normal appearance. The console launcher also accepts `--restore <controller PID>`.

Use only one taskbar appearance tool at a time. The native controller exits if it cannot find the expected taskbar layout. Explorer stays in charge of taskbar auto-hide and screen space. If Explorer restarts, run the controller again.

The dashboard includes media sessions and volume, Windows settings shortcuts, a calendar, local reminders, a focus timer, system statistics, a file shelf, a wallpaper picker, and optional city-based weather. Entering a city and fetching a forecast uses Open-Meteo. In Music and mixer, the lyrics button sends track metadata to LRCLIB to look up line-synced lyrics.

## Build the native dock

Use MSVC, the Windows SDK, CMake, and the .NET 10 SDK on Windows:

```powershell
cmake -S src/NativeTaskbar -B artifacts/native-build -G "Visual Studio 18 2026" -A x64
cmake --build artifacts/native-build --config Release
dotnet publish src/WindowsDock -c Release -r win-x64 --self-contained -o artifacts/native-dock
Copy-Item artifacts/native-build/Release/NativeTaskbar.exe,artifacts/native-build/Release/NativeTaskbar.Xaml.dll artifacts/native-dock
.\artifacts\native-dock\NativeTaskbar.exe
```

Explorer can retain the native DLL until it exits. If that locks the build output during development, use a different build directory or set `NATIVE_TASKBAR_MODULE_NAME` to a new DLL basename and copy the matching DLL with the controller.

## Legacy companion build

From the repository root with the .NET 10 SDK on Windows:

```powershell
dotnet build src/WindowsDock -c Release
dotnet publish src/WindowsDock -c Release -r win-x64 --self-contained -o artifacts/win-x64
.\artifacts\win-x64\WindowsDock.exe
```

Keep the complete publish directory together. Run with `--safe-mode` to load defaults and keep edits in memory for the session. Normal settings are stored in `%LOCALAPPDATA%\WindowsDock\dock.json` with a previous-version backup.

See the [architecture and feasibility matrix](docs/ARCHITECTURE.md) and [roadmap](ROADMAP.md) for the remaining product milestones.
