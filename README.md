# Windows Dock

Windows Dock styles Explorer's taskbar in place on Windows 11 x64 25H2 (build 26200). It centers the native app list, tray, and clock on one dark glass surface. The grid button opens a dashboard from the same screen edge. Native app switching, jump lists, window previews, Start, notifications, and Quick Settings remain Explorer features.

## Run the native dock

Keep the contents of the native publish directory together and run NativeTaskbar.exe. Click the grid button beside the tray to open the dashboard. Ctrl+Alt+D, the Windows Dock notification-area menu, and the dashboard's Settings page can restore the normal appearance. The console launcher also accepts `--restore <controller PID>`.

Use only one taskbar appearance tool at a time. The native controller exits if it cannot find the expected taskbar layout. Explorer stays in charge of taskbar auto-hide and screen space. If Explorer restarts, run the controller again.

The dashboard includes media sessions and volume, Windows settings shortcuts, a calendar, local reminders, a focus timer, system statistics, a file shelf, a wallpaper picker, and optional city-based weather. Entering a city and fetching a forecast uses Open-Meteo. In Music and mixer, the lyrics button sends track metadata to LRCLIB to look up line-synced lyrics.

## Coding chats

Hover over a square in the 2×2 grid beside the tray to see the chat and its status. Click the grid to open **Coding chats**. The panel brings an approval or recent reply to the front and keeps the chat list below it. **Assign slot** pins a chat, follows the latest matching chat in a project, or restores automatic selection. The grid reads local desktop metadata and turn events, so cloud-only chats may be missing.

Under **Connections and defaults**, enable the provider hooks for live updates. Turn on **Allow or deny individual requests here** before enabling the hooks if you want the dock to handle one-time tool approvals. Codex requires you to review the new hooks in **Settings → Hooks**. Start or resume a Claude Code session for the new settings to take effect. Each supported request shows its tool, working directory, and arguments. After 30 seconds the dock yields to the provider's own approval flow. The dock stores status metadata locally and removes the approval payload when the request ends.

**Save defaults** updates the provider's user configuration and creates a backup. These are defaults for new sessions when the provider loads its settings. Existing chats can have their own model and thinking overrides. Use **Remove dock hooks** to remove this integration from a provider.

**Summarize reply** previews the selected chat's latest completed local reply. Choose Gemini or Groq, enter an API key, and click **Summarize** to send the editable text to that provider. You can also set `GEMINI_API_KEY` or `GROQ_API_KEY` in your Windows environment. Keys entered in the popup stay in memory for the current dock session. Paste a reply when a transcript is unavailable.

**Usage** shows the selected chat's recorded token counts, Codex account usage and quota reset times, and Claude Desktop's last cached plan percentages. The Claude usage page opens from the panel for reset times that aren't in its local cache.

## Widgets

Open the dashboard with the larger grid button, then choose **Widgets** on the right. Press **Ctrl+K** while the dashboard is open to search installed desktop shortcuts, workspaces, and dock actions.

- **Audio switchboard** changes the active output device and offers per-app volume and output routing. Some apps apply a new route when they reopen their audio stream.
- **Meetings** reads a local `.ics` export and offers Join and Copy link for supported meeting providers.
- **Clipboard and files** shows Windows clipboard history when it is enabled. Pin text, images, or files and drag them into other apps.
- **GitHub checks** reads your PRs and review requests through the signed-in GitHub CLI. Enter a repository as `owner/repo`.
- **Workspace shortcuts** group a folder, websites, and local app shortcuts into a single action.
- **Downloads** shows recent files and partial browser downloads from the Downloads folder, alongside Windows background transfers. Browser progress is limited to what the partial files expose.
- **Focus timer** runs locally. **Windows Focus** opens the system controls for a focus session with Do Not Disturb.
- **Notifications** reads recent Windows alerts when access is allowed. Select alerts and review the draft before sending it to Gemini or Groq.

In **Weather**, choose **Use my location** for automatic forecasts, or search for a city. The automatic option requests Windows location access and sends rounded coordinates to Open-Meteo when refreshing the forecast.

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
