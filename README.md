# Windows Dock

Windows Dock is a standalone Windows 11 companion dock. It displays pinned and running applications on a centered translucent surface above the current monitor work area's bottom edge.

Click Settings at the right end of the dock to open Customization Studio. Right-click Settings to exit. The notification-area icon also offers Studio access. Click the status group for Windows Quick Settings, or right-click it for the dock-owned quick controls. Click the clock for Windows notifications and calendar. Studio includes Appearance, Behavior, Widgets, Applications, and Layout pages. Layout adds dividers and shortcuts at section boundaries. Enable Media in Widgets for playback buttons and the default-output mixer. Enable Assistant for optional Gemini or Groq chat. Enter a provider model ID and API key in the Assistant window. The key and conversation live only in that window, and Send shares the conversation with the selected provider. Enable auto-hide in Behavior, then reveal from the bottom edge or with Ctrl+Alt+D when available. The tray icon also offers Reveal dock. Windows Settings links open the corresponding system pages. Explorer handles its own tray and flyouts.

## Build and run

From the repository root with the .NET 10 SDK on Windows:

```powershell
dotnet build src/WindowsDock -c Release
dotnet publish src/WindowsDock -c Release -r win-x64 --self-contained -o artifacts/win-x64
.\artifacts\win-x64\WindowsDock.exe
```

Keep the complete publish directory together. Run with `--safe-mode` to load defaults and keep edits in memory for the session. Normal settings are stored in `%LOCALAPPDATA%\WindowsDock\dock.json` with a previous-version backup.

See the [architecture and feasibility matrix](docs/ARCHITECTURE.md) and [roadmap](ROADMAP.md) for the remaining product milestones.
