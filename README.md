# Windows Dock

Windows Dock styles Explorer's taskbar in place on Windows 11 x64 25H2 (build 26200). It centers the native app list, tray, and clock on one frosted glass surface. The Windows Dock button opens Dashboard and the adjoining row of four animated slots shows live coding chat states. Native app switching, jump lists, window previews, Start, notifications, and Quick Settings remain Explorer features.

## Run the native dock

Keep the contents of the native publish directory together and run NativeTaskbar.exe. Click or pull up on the silver-and-amber Windows Dock icon to open Dashboard. Click the icon again to close Dashboard. Use the Windows Dock notification-area menu, the dashboard's Settings page, or `--restore <controller PID>` to restore the normal appearance.

Swipe sideways with two fingers over Dashboard to switch one tab at a time. Click the **Widgets** tab or scroll up with two fingers at the top of a Dashboard page to open Widgets in the same window. The pinned widget and favorites appear above the catalog.

At the bottom of Coding chats and Assistants, click a section or drag the handle sideways to switch. A horizontal scroll over that handle also switches sections. Drag down to close the panel. On a tool or conversation with a Back button, drag the handle right to go back.

Use one taskbar appearance module at a time. Standard startup waits for the modified taskbar and retries the connection if Explorer is still starting or another module owns the appearance. After an Explorer restart, Windows Dock starts a fresh controller. Panels open above the taskbar. Windows Dock holds auto-hide off while a panel is open and restores the previous setting when the panel closes.

The dashboard opens on demand and exits about 20 seconds after hiding once its jobs have finished. A smaller background process keeps coding chat status and active focus timers current. Dashboard tabs and popups keep their size while content changes and scrolls. Use **Start at sign-in** in Dashboard to start the native controller automatically.

The dashboard includes media sessions and volume, a calendar, local reminders, a focus timer, system statistics, a file shelf, a wallpaper picker, and weather. **Controls** toggles Wi-Fi and Bluetooth when Windows grants radio access. Use the network and Bluetooth device links to choose a network or pair a device. Weather remembers your chosen city or rounded Windows location and has a °F/°C switch. Forecasts use Open-Meteo. In the Audio tab, the lyrics button sends track metadata to LRCLIB to look up line-synced lyrics.

## Use with Hitodama

With Windows Dock's native styling stopped, run `NativeTaskbar.exe --tray` to keep the dashboard and coding chats available while Hitodama styles the taskbar. For a normal Windows taskbar pin in this mode, create a shortcut to `WindowsDock.exe --open` and pin it through Windows. That command opens Dashboard and starts the companion controller if needed. **Ctrl+Alt+D** opens Coding chats in either mode.

In either mode, **Ctrl+Alt+A** or **Open assistant** in the tray menu opens the four-character picker. Use keys **1 to 4** to open a character, or search for their name with **Ctrl+K**. On battery, the controller sends Windows notifications at 25%, 20%, 15%, 10%, and 5%. Reconnecting power resets the alert sequence.

To switch back to Windows Dock's taskbar styling, exit tray mode from its icon, turn off Hitodama's taskbar appearance, and launch `NativeTaskbar.exe` without arguments. Hitodama's assistant can run in either mode.

## Coding chats

The four faces in the taskbar row animate as chats work, finish, or need attention. A new completion briefly lights the taskbar rim and the open dock panel. Click a face to open **Coding chats**, or use **Chats** in Dashboard. Search or filter the conversations on the left. The right pane holds replies, approvals, slot assignment, and usage. **Jobs**, **Activity**, and **Setup** use that same pane. **Agent slot** pins a chat, follows the latest matching chat in a project, or restores automatic selection. The grid reads local desktop metadata and turn events, so cloud-only chats may be missing. Motion follows the Windows animation preference.

Under **Connections and defaults**, enable the provider hooks for live updates. Turn on **Allow or deny individual requests here** before enabling the hooks if you want the dock to handle one-time tool approvals. Codex requires you to review the new hooks in **Settings → Hooks**. Start or resume a Claude Code session for the new settings to take effect. Each supported request shows its tool, working directory, and arguments. After 30 seconds the dock yields to the provider's own approval flow. The dock stores status metadata locally and removes the approval payload when the request ends.

**Save defaults** updates the provider's user configuration and creates a backup. These are defaults for new sessions when the provider loads its settings. Existing chats can have their own model and thinking overrides. Use **Remove dock hooks** to remove this integration from a provider.

**Summarize reply** sends the latest completed local reply to the saved provider and model in one click. The result appears alongside the conversation list. Use **Original reply** to return to the source or **Source & provider** to edit it, change providers, or paste a reply when a transcript is unavailable. Codex and Claude Code use their installed Windows CLI and saved sign-in. Leave the model blank to use the CLI's default. Gemini and Groq accept a key from the editor, `GEMINI_API_KEY`, or `GROQ_API_KEY`. Keys entered in the editor are saved after a successful request using Windows account encryption. Use **Forget saved key** to remove a saved key.

Reply and notification summaries remember the last selected provider and each provider's model across dock restarts. Clear the model field to restore the selected provider's default.

CLI summaries run in a temporary directory with model tools disabled and session history off. They use the selected CLI's account and usage limits. Sign in to the CLI before choosing it in the dock.

**Usage** shows the selected chat's recorded token counts, Codex account usage and quota reset times, and Claude Desktop's last cached plan percentages. The Claude usage page opens from the panel for reset times that aren't in its local cache.

## Assistant conversations

Open an assistant to search previous Windows Dock conversations or start a new one. The provider and model are selected from one menu inside the message box. Use the attachment button, drop in a file, or paste a screenshot to preview it before sending. Text and Word documents can be used with any assistant provider. PDF, PNG, and JPEG uploads require an eligible ChatGPT plan, Claude API, or Gemini model. If the last message is unanswered, **Retry reply** resends it with its original attachments.

Use **Organize this chat** in the header to rename, group, or pin it. The controls on individual messages branch the conversation, edit a prompt in a branch, or regenerate a reply in a branch. Assistant memory in the sidebar is editable for each character and is included when that character sends a message. Direct API providers and Ollama show text as it arrives. **Usage-limit fallback** in the model menu can send the same request to Gemini or Grok using your configured key when the primary provider reports a usage limit.

Choose **Chat storage folder** in the sidebar to put the chat library under a folder you select. Windows Dock copies past chats, attachment copies, and exports into the new library. Each conversation has its own transcript, attachments, and files. The folder icon in the conversation header opens its output files. Named text and code files are saved automatically when **Auto-save named files** is enabled. The controls beside replies let you save a Markdown copy or individual code blocks.

## Widgets

Choose **Widgets** from the dashboard or the compact Coding chats panel. Pick one live widget from the **Pinned** menu, then star up to four other widgets for compact shortcuts below it and on Overview. If you pin a favorite, the previous pinned widget takes its shortcut slot. Press **Ctrl+K** in any dock panel to search open windows, chats, widgets, apps, workspaces, and settings. Type `window:` to limit the search to open windows on your current desktop. Use **↑** and **↓** to select a result, **Enter** to open it, and **Esc** to close the popup.

Right-click a favorite and select **Move earlier** or **Move later** to change its position in Widgets and Today. Use the two icons beside **Favorites** to export or import the pinned widget and favorite order as a JSON profile.

- **Audio switchboard** changes the default output and offers per-app volume and output routing across connected devices. Each mixer row shows the device hosting the audio session. If the app keeps an open stream on an older device, the mixer shows the saved route and prompts you to resume playback or reload the player. Windows routes at the app level.
- **Audio presets** saves the default output, master volume, and available per-app levels and mute states. Reapply a preset after an absent app opens an audio session.
- **Activity inbox** keeps the last three days of observed chat replies, approval requests, and limits. Open it from **Activity** in Coding chats or from Widgets. Mark all read saves your position.
- **Meetings** reads a local `.ics` export and offers Join and Copy link for supported meeting providers. Create, edit, delete, and export events in the local calendar. Local events appear on Today and in Fern’s planner lookup.
- **Clipboard and files** shows Windows clipboard history when it is enabled. Pin text, images, or files and drag them into other apps.
- **GitHub checks** reads your PRs, review requests, and recent Actions runs through the signed-in GitHub CLI. Enter a repository as `owner/repo`.
- **Workspace shortcuts** group a folder, websites, and local app shortcuts into a single action.
- **Downloads** shows recent files and partial browser downloads from the Downloads folder, alongside Windows background transfers. Browser progress is limited to what the partial files expose.
- **Focus timer** runs locally. **Windows Focus** opens the system controls for a focus session with Do Not Disturb.
- **Notifications** reads recent Windows alerts when access is allowed. Select alerts and review the draft before sending it to any of the four summary providers.
- **Assistant** opens the four-character picker. Choose **ChatGPT plan** and continue in your browser to authorize eligible plan usage. **Claude API** uses a Claude Console key. Gemini, Grok, and Groq use their own API keys. Configure them in the panel or with `ANTHROPIC_API_KEY`, `GEMINI_API_KEY`, `XAI_API_KEY`, or `GROQ_API_KEY`. On a usage error, you can switch to Gemini or Grok and resend the draft. Each send shares the current character's recent conversation with the selected provider.
- **Assistant jobs** shows running tasks and saved results. Open a job to cancel it, copy its result, or save it as Markdown.
- **Voice memos** records and imports WAV files, plays them back, and searches saved transcripts. An optional transcription request sends the selected memo to Groq after you confirm. You can review the transcript and use it as an Orbit draft.
- **File search** indexes the folders you select on demand. It searches inside supported text, Markdown, code, and Word files. PDF, spreadsheet, and media results are searchable by filename. The index and excerpts stay on your PC.

### Assistant actions and background jobs

Any of the four dock assistants can look up dock state, change volume or output, manage reminders and focus timers, switch radios, change startup settings, and arrange widgets. Review the exact proposed changes in chat and select **Apply changes**. The chat can use its enabled role-specific MCP connections when a lookup is useful. Codex CLI and Claude Code CLI are also available as chat providers with their local sign-in.

Ask Orbit to hand off substantial work. Lark handles research, Scribe drafts and edits, Fern plans, and Bolt codes. Confirm the brief to start it, then open **Jobs** to check its progress after closing the chat. Bolt asks for a local Git workspace and uses the signed-in Codex CLI. A standalone CLI can run workspace commands. When the Windows command runner cannot start, Bolt can still read and edit project text files through the dock's workspace editor. The job result identifies shell checks that remain. Up to two jobs can run at once. A job that loses its dock process is marked interrupted the next time you open Jobs.

**Retry job** can start a new coding run in its original project after confirmation. For the other specialists it puts the brief in Orbit’s draft for review.

External coding agents can connect over stdio. Replace the executable path with your installed path.

```powershell
codex mcp add windows-dock -- "C:\Path\To\WindowsDock.exe" --dock-mcp
claude mcp add --scope user --transport stdio windows-dock -- "C:\Path\To\WindowsDock.exe" --dock-mcp
```

The MCP connection exposes dock status, supported edits, and the coding role's enabled lookup tools. An external write request opens a Windows confirmation with the exact change.

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
dotnet publish src/WindowsDock -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true -o artifacts/native-dock
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

Use the [engineering handoff](docs/engineering-handoff.md) and [review and redesign prompt](docs/review-redesign-prompt.md) to brief a new contributor on the current implementation.
