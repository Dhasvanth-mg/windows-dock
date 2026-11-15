# Windows Dock

Windows Dock styles Explorer's taskbar in place on Windows 11 x64 25H2 (build 26200). It centers the native app list, tray, and clock on one frosted glass surface. The Windows Dock button opens Dashboard and the adjoining 2×2 grid shows live coding chat states. Native app switching, jump lists, window previews, Start, notifications, and Quick Settings remain Explorer features.

## Run the native dock

Keep the contents of the native publish directory together and run NativeTaskbar.exe. Click or pull up on the silver-and-amber Windows Dock icon to open Dashboard. Click the icon again to close Dashboard. Use the Windows Dock notification-area menu, the dashboard's Settings page, or `--restore <controller PID>` to restore the normal appearance.

Swipe sideways with two fingers over Dashboard to switch one tab at a time. Scroll up with two fingers to open Widgets. If the content under the pointer can scroll up, finish scrolling to the top and start a fresh upward gesture. Choose **Dashboard** in Widgets to return to your selected tab.

At the bottom of Widgets, Coding chats, and Assistants, click a section or drag the handle sideways to switch. A horizontal scroll over that handle also switches sections. Drag down to close the panel. On a tool or conversation with a Back button, drag the handle right to go back.

Use one taskbar appearance module at a time. The native controller exits if it cannot find the expected taskbar layout. Panels open just above the taskbar. Windows Dock holds auto-hide off while a panel is open and restores the previous setting when the panel closes. If Explorer restarts, run the controller again.

The dashboard opens on demand and exits about 20 seconds after hiding. A smaller background process keeps coding chat status and active focus timers current. An API key typed into the dashboard lasts until that process exits. Use the Windows environment variables below if you want the key available on subsequent opens.

The dashboard includes media sessions and volume, Windows settings shortcuts, a calendar, local reminders, a focus timer, system statistics, a file shelf, a wallpaper picker, and weather. Forecasts use Open-Meteo. In the Audio tab, the lyrics button sends track metadata to LRCLIB to look up line-synced lyrics.

## Use with Hitodama

With Windows Dock's native styling stopped, run `NativeTaskbar.exe --tray` to keep the dashboard and coding chats available while Hitodama styles the taskbar. For a normal Windows taskbar pin in this mode, create a shortcut to `WindowsDock.exe --open` and pin it through Windows. That command opens Dashboard and starts the companion controller if needed. **Ctrl+Alt+D** opens Coding chats in either mode.

In either mode, **Ctrl+Alt+A** or **Open assistant** in the tray menu opens the 2×2 assistant picker. Use **1–4** to open a character, or search for their name with **Ctrl+K**. On battery, the controller sends Windows notifications at 25%, 20%, 15%, 10%, and 5%. Reconnecting power resets the alert sequence.

To switch back to Windows Dock's taskbar styling, exit tray mode from its icon, turn off Hitodama's taskbar appearance, and launch `NativeTaskbar.exe` without arguments. Hitodama's assistant can run in either mode.

## Coding chats

The four faces in the 2×2 taskbar grid change as chats work, finish, or need attention. Click a face to open **Coding chats**. The panel brings an approval or recent reply to the front and keeps the chat list below it. **Assign slot** pins a chat, follows the latest matching chat in a project, or restores automatic selection. The grid reads local desktop metadata and turn events, so cloud-only chats may be missing.

Under **Connections and defaults**, enable the provider hooks for live updates. Turn on **Allow or deny individual requests here** before enabling the hooks if you want the dock to handle one-time tool approvals. Codex requires you to review the new hooks in **Settings → Hooks**. Start or resume a Claude Code session for the new settings to take effect. Each supported request shows its tool, working directory, and arguments. After 30 seconds the dock yields to the provider's own approval flow. The dock stores status metadata locally and removes the approval payload when the request ends.

**Save defaults** updates the provider's user configuration and creates a backup. These are defaults for new sessions when the provider loads its settings. Existing chats can have their own model and thinking overrides. Use **Remove dock hooks** to remove this integration from a provider.

**Summarize reply** previews the selected chat's latest completed local reply. Paste a reply when a transcript is unavailable. Review the editable text, then choose **Codex**, **Claude Code**, **Gemini**, or **Groq** and send it to that provider. Codex and Claude Code use their installed Windows CLI and saved sign-in. Leave the model blank to use the CLI's default. Gemini and Groq use an API key from the popup, `GEMINI_API_KEY`, or `GROQ_API_KEY`. Keys entered in the popup stay in memory for the current dock session.

CLI summaries run in a temporary directory with model tools disabled and session history off. They use the selected CLI's account and usage limits. Sign in to the CLI before choosing it in the dock.

**Usage** shows the selected chat's recorded token counts, Codex account usage and quota reset times, and Claude Desktop's last cached plan percentages. The Claude usage page opens from the panel for reset times that aren't in its local cache.

## Widgets

Choose **Widgets** from the dashboard or the compact Coding chats panel. Star a widget to show it first in the launcher and add a shortcut to Overview. Press **Ctrl+K** in any dock panel to search open windows, chats, widgets, apps, workspaces, and settings. Type `window:` to limit the search to open windows on your current desktop. Use **↑** and **↓** to select a result, **Enter** to open it, and **Esc** to close the popup.

- **Audio switchboard** changes the active output device and offers per-app volume and output routing. Some apps apply a new route when they reopen their audio stream.
- **Audio presets** saves the default output, master volume, and available per-app levels and mute states. Reapply a preset after an absent app opens an audio session.
- **Activity inbox** keeps the last three days of observed chat replies, approval requests, and limits. Open it from the bell in Coding chats or from Widgets. Mark all read saves your position.
- **Meetings** reads a local `.ics` export and offers Join and Copy link for supported meeting providers.
- **Clipboard and files** shows Windows clipboard history when it is enabled. Pin text, images, or files and drag them into other apps.
- **GitHub checks** reads your PRs, review requests, and recent Actions runs through the signed-in GitHub CLI. Enter a repository as `owner/repo`.
- **Workspace shortcuts** group a folder, websites, and local app shortcuts into a single action.
- **Downloads** shows recent files and partial browser downloads from the Downloads folder, alongside Windows background transfers. Browser progress is limited to what the partial files expose.
- **Focus timer** runs locally. **Windows Focus** opens the system controls for a focus session with Do Not Disturb.
- **Notifications** reads recent Windows alerts when access is allowed. Select alerts and review the draft before sending it to any of the four summary providers.
- **Assistant** opens the 2×2 character picker. Choose **ChatGPT plan** and continue in your browser to authorize eligible plan usage. **Claude API** uses a Claude Console key. Gemini, Grok, and Groq use their own API keys. Configure them in the panel or with `ANTHROPIC_API_KEY`, `GEMINI_API_KEY`, `XAI_API_KEY`, or `GROQ_API_KEY`. On a usage error, you can switch to Gemini or Grok and resend the draft. Each send shares the current character's recent conversation with the selected provider.

### Assistant connections

- **Lark** follows claims back to their sources with Exa web search.
- **Fern** helps plan your day with a local MCP connection to dock reminders and the imported calendar. Calendar join links stay out of the result.
- **Bolt** debugs and reviews code with Microsoft Learn. Set `GITHUB_MCP_TOKEN` to a token allowed to read your pull requests, then enable the read-only GitHub PR reader. Enter a PR as `owner/repository#123`.
- **Orbit** is for conversation and brainstorming.

In the picker, **Search the web** previews Exa results, **Preview schedule** reads today or seven days from the local planner, **Attach logs** accepts an editable excerpt, and **Idea jar** adds a conversation starter to Orbit’s draft. Search results, schedules, and logs have an attachment preview before you send a message. Use **Copy reply** to put any assistant reply on the clipboard.

Open **MCP connections** inside a character's chat. **Fetch context** runs the selected read and shows a preview. **Attach to draft** adds the selected result to your next explicit send. The dock manages these connections and the model receives only the result you attach. Close the panel to cancel an in-progress read.

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

The companion Studio edits dividers and custom shortcuts for the companion bar. Native mode uses Windows app pins and the dashboard's Workspace shortcuts.

See the [architecture and feasibility matrix](docs/ARCHITECTURE.md) and [roadmap](ROADMAP.md) for the remaining product milestones.
