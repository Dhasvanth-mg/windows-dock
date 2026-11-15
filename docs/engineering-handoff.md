# Windows Dock engineering handoff

The application baseline for this handoff is `7ceca1b` on `main`. Start with this document for the current feature and source map, then use the [review and redesign prompt](review-redesign-prompt.md) to brief the new agent. The [feature tracker](feature-tracker.md) records how individual features were built and tested. Some older entries describe screens that have since changed.

## How the app opens

| Mode | Entry point | What the user sees |
| --- | --- | --- |
| Native, the primary experience | `NativeTaskbar.exe` | Explorer's native apps, tray, and clock sit on one centered frosted glass surface. The silver-and-amber Windows Dock button opens Dashboard. The adjacent horizontal row of four animated coding-chat slots opens Coding chats. Explorer handles Start, app switching, previews, jump lists, notifications, and Quick Settings. |
| Tray companion for Hitodama | `NativeTaskbar.exe --tray` | Windows Dock has an icon in the notification area, with four compact status cells and menu entries for its panels. Hitodama can own taskbar appearance. A shortcut to `WindowsDock.exe --open` can be pinned through Windows for a visible launcher. |
| Legacy WPF companion | `WindowsDock.exe` | A separate bar with window groups, pins, dividers, custom shortcuts, clock, built-in system widgets, reveal settings, and a Studio for appearance and layout. `--safe-mode` uses the default configuration for that session. |
| Managed UI development | `WindowsDock.exe --dashboard` | Opens Dashboard directly. Use this entry point for WPF layout work, then validate the real taskbar routes separately. |

In native mode, click or pull up on the dock button to open Dashboard, then use the same button to close it. A panel holds the taskbar visible and restores the user's auto-hide state on close. Panels are centered above the taskbar. The native glass fills the existing 48-pixel taskbar at 96 DPI. The usual Dashboard size is 960 × 412 DIP and the taskbar Coding chats size is 764 × 454 DIP, clamped to the display. Content scrolls within those bounds.

`Ctrl+Alt+D` opens Coding chats and `Ctrl+Alt+A` opens Assistants in either controller mode. `Ctrl+K` searches open windows, chats, apps, workspaces, widgets, and settings. Prefix a query with `window:` to limit it to windows on the current desktop. Dashboard opens to Overview in a fresh session and remembers the selected tab when returning from Widgets. Two-finger horizontal movement switches Dashboard tabs. A fresh two-finger scroll up opens Widgets when content cannot scroll farther up. The bottom navigation gives Widgets, Coding chats, and Assistants visible buttons, a sideways swipe or wheel gesture to switch sections, and a downward drag to dismiss. Tool panels with a Back action also support a rightward drag on the handle.

## Runtime and source map

| Layer | Responsibility |
| --- | --- |
| Native controller | [Main.cpp](../src/NativeTaskbar/Main.cpp) owns the controller, Explorer attachment, taskbar visibility, process lifecycle, and native/tray routing. [DockTray.cpp](../src/NativeTaskbar/DockTray.cpp) owns the notification-area icon, menu, hotkeys, and battery notification delivery. |
| Explorer appearance | [Diagnostics.cpp](../src/NativeTaskbar/Diagnostics.cpp) locates the expected XAML tree. [TaskbarStyle.cpp](../src/NativeTaskbar/TaskbarStyle.cpp) lays out the native frames, glass, launcher, clock/date, tray and battery styling, and rim animation. [TaskbarAgents.cpp](../src/NativeTaskbar/TaskbarAgents.cpp) paints and handles the four status slots. The x64 native styling gate currently accepts Windows 11 25H2 build 26200. Restoration uses saved XAML values and columns. Explorer can retain the DLL until it exits. |
| Entry points and background monitor | [App.xaml.cs](../src/WindowsDock/App.xaml.cs) dispatches CLI modes before constructing WPF. The controller starts `WindowsDock.exe --monitor <pid>` for status, inbox updates, and focus timer expiry. The WPF dashboard starts on demand, hides on dismissal, and normally exits after about 20 seconds hidden once its jobs finish. [DockMonitor.cs](../src/WindowsDock/DockMonitor.cs) scans about every six seconds and can wake at a focus deadline. |
| Native and managed contract | [Connection.h](../src/NativeTaskbar/Connection.h) and [NativeDashboardSession.cs](../src/WindowsDock/NativeDashboardSession.cs) use named events scoped to the controller PID for launch, visibility, and stop. [AgentGridState.h](../src/NativeTaskbar/AgentGridState.h) and [AgentGrid.cs](../src/WindowsDock/AgentGrid.cs) define the matching shared-memory layout. [DockCompletion.cs](../src/WindowsDock/DockCompletion.cs) and [DashboardCompletion.cs](../src/WindowsDock/DashboardCompletion.cs) carry one-shot completion cues. |
| WPF dashboard | [DashboardWindow.xaml](../src/WindowsDock/DashboardWindow.xaml) and its [code-behind](../src/WindowsDock/DashboardWindow.xaml.cs) own window sizing, anchoring, tabs, and page lifetime. [DashboardControls.xaml](../src/WindowsDock/DashboardControls.xaml) holds shared control styles. Page and panel builders are `Dashboard*.cs` partials of `DashboardWindow`. [DashboardGestures.cs](../src/WindowsDock/DashboardGestures.cs) arbitrates wheel direction, scrolling, section changes, and the drag handle. |
| Legacy companion | [MainWindow.xaml.cs](../src/WindowsDock/MainWindow.xaml.cs) and [ShellIntegration.cs](../src/WindowsDock/ShellIntegration.cs) manage the bar and running-window groups. [Studio.cs](../src/WindowsDock/Studio.cs), [Layout.cs](../src/WindowsDock/Layout.cs), and [DockSettings.cs](../src/WindowsDock/DockSettings.cs) own live appearance, edge reveal, widgets, pin order, dividers, and custom shortcuts, with a backup for the local config. [QuickControls.cs](../src/WindowsDock/QuickControls.cs) gives this mode its own overflow controls. Native mode uses Windows taskbar pins and the dock's workspace shortcuts. |

Run one taskbar appearance module at a time. The tray companion and Hitodama can run together. For native recovery, use **Restore taskbar appearance** in Dashboard Settings, the Windows Dock tray menu, or `NativeTaskbar.exe --restore <controller PID>`. If Explorer restarts, run the controller again. [DockStartup.cs](../src/WindowsDock/DockStartup.cs) implements **Start at sign-in** through the current user's Run entry and preserves the selected mode.

## Dashboard tabs

| Tab | Current behavior | Owning files |
| --- | --- | --- |
| Overview | Shows live media and artwork, playback controls, volume and quick links, calendar, weather, CPU and memory, and favorite widget shortcuts. | [DashboardMedia.cs](../src/WindowsDock/DashboardMedia.cs), [DashboardOverviewGraphics.cs](../src/WindowsDock/DashboardOverviewGraphics.cs) |
| Audio | Media playback and session details, master volume and mute, default output, per-app volume and route, and optional line-synced lyrics through LRCLIB using track metadata. | [DashboardMedia.cs](../src/WindowsDock/DashboardMedia.cs), [DashboardLyrics.cs](../src/WindowsDock/DashboardLyrics.cs), [DockAudio.cs](../src/WindowsDock/DockAudio.cs), [AudioRouting.cs](../src/WindowsDock/AudioRouting.cs) |
| Controls | Wi-Fi and Bluetooth toggles when Windows grants radio access, links to choose networks or pair devices, and Windows sound, notification, display, and other settings links. | [DashboardMedia.cs](../src/WindowsDock/DashboardMedia.cs), [DockRadios.cs](../src/WindowsDock/DockRadios.cs), [DesktopControls.cs](../src/WindowsDock/DesktopControls.cs) |
| Calendar | Month navigation, day agenda, local reminders, and an imported `.ics` calendar with recurring events and supported meeting links. | [DashboardCalendar.cs](../src/WindowsDock/DashboardCalendar.cs), [DockReminders.cs](../src/WindowsDock/DockReminders.cs), [DashboardMeetings.cs](../src/WindowsDock/DashboardMeetings.cs) |
| Weather | Windows location with permission, or a city search. Saves the location and unit separately. Sends rounded coordinates for automatic forecasts and uses Open-Meteo forecast data with a °F/°C switch. | [DashboardWeather.cs](../src/WindowsDock/DashboardWeather.cs) |
| Chats | The same Coding chats workspace that a taskbar slot opens. | [DashboardCodingWorkspace.cs](../src/WindowsDock/DashboardCodingWorkspace.cs) |
| Stats | CPU, memory, system drive, and network utilization. | [DashboardStats.cs](../src/WindowsDock/DashboardStats.cs) |
| Files | A local file shelf with pinned items and drag-and-drop. | [DashboardUtilities.cs](../src/WindowsDock/DashboardUtilities.cs) |
| Wallpapers | Add local images, use the wallpaper library, and apply a selection through Windows. | [DashboardUtilities.cs](../src/WindowsDock/DashboardUtilities.cs) |
| Power | Battery and charging status, uptime, Windows power settings, and lock. The controller alerts at 25%, 20%, 15%, 10%, and 5% on battery and resets the sequence when power reconnects. | [DashboardUtilities.cs](../src/WindowsDock/DashboardUtilities.cs), [BatteryAlerts.h](../src/NativeTaskbar/BatteryAlerts.h) |
| Settings | Windows taskbar settings, restore or exit, start at sign-in, and power and display settings. | [DashboardUtilities.cs](../src/WindowsDock/DashboardUtilities.cs), [DockStartup.cs](../src/WindowsDock/DockStartup.cs) |

## Widgets and search

The [Widgets catalog](../src/WindowsDock/DashboardWidgets.cs) mounts one live widget at the top, up to four starred shortcuts below, and then the rest of the catalog. Pinning an existing favorite swaps it with the old pin. The same favorites are available on Overview. [DockWidgetLayout.cs](../src/WindowsDock/DockWidgetLayout.cs) owns IDs, the four-favorite limit, and migration from the older favorites file. [WidgetCommand.cs](../src/WindowsDock/WidgetCommand.cs) supports the shared command palette and ranking.

| Widget | Function and source |
| --- | --- |
| Audio switchboard | Default output, media transport, and app mixer across render devices. The mixer distinguishes a saved route from the endpoint carrying a live stream. A browser can keep its old stream until it is resumed or reloaded. See [DashboardMedia.cs](../src/WindowsDock/DashboardMedia.cs) and [AudioRouting.cs](../src/WindowsDock/AudioRouting.cs). |
| Audio presets | Save an output, master level and mute, and available app levels and mute. Reapply after an absent app creates a session. See [DashboardAudioPresets.cs](../src/WindowsDock/DashboardAudioPresets.cs) and [AudioPresets.cs](../src/WindowsDock/AudioPresets.cs). |
| Meetings | Upcoming meetings from the calendar import, with Join and Copy link for supported URLs. See [DashboardMeetings.cs](../src/WindowsDock/DashboardMeetings.cs). |
| Clipboard and files | Reads Windows clipboard history when enabled. Pin text, images, or files and copy or drag them out. See [DashboardClipboard.cs](../src/WindowsDock/DashboardClipboard.cs). |
| GitHub checks | Reads pull requests, review requests, and recent Actions runs through the signed-in `gh` CLI for a selected repository. The assistant's PR MCP reader is a separate integration. See [DashboardGitHub.cs](../src/WindowsDock/DashboardGitHub.cs). |
| Workspace shortcuts | Open a saved set of a folder, sites, and local app shortcuts. See [DashboardWidgets.cs](../src/WindowsDock/DashboardWidgets.cs). |
| Downloads | Recent Downloads folder files, partial browser downloads, and Windows background transfers. Browser progress depends on the information available from its partial files. See [DashboardTransfers.cs](../src/WindowsDock/DashboardTransfers.cs). |
| Focus timer | Local countdown and expiry alert, with a link to Windows Focus settings. See [DashboardCalendar.cs](../src/WindowsDock/DashboardCalendar.cs) and [DockMonitor.cs](../src/WindowsDock/DockMonitor.cs). |
| Notifications | Reads Windows alerts when access is granted. Select alerts, review an editable draft, then send it to a summary provider. See [DashboardNotifications.cs](../src/WindowsDock/DashboardNotifications.cs). |
| Activity inbox | Deduplicated recent coding replies, approval requests, and usage limits. Saves the read position and can open the associated chat or Usage. See [AgentInbox.cs](../src/WindowsDock/AgentInbox.cs) and [DashboardInbox.cs](../src/WindowsDock/DashboardInbox.cs). |
| Assistant | Opens the four-persona picker. An inline pinned version offers direct character shortcuts. See [DashboardAssistantGrid.cs](../src/WindowsDock/DashboardAssistantGrid.cs). |
| Assistant jobs | Tracks background jobs with status, cancellation, saved results, copy, and Markdown export. See [DashboardAssistantJobs.cs](../src/WindowsDock/DashboardAssistantJobs.cs). |

## Coding chats and the taskbar strip

[AgentSessions.cs](../src/WindowsDock/AgentSessions.cs) merges local Codex session index and rollout data, Claude Desktop metadata, and optional provider hook events. [AgentTranscripts.cs](../src/WindowsDock/AgentTranscripts.cs) reads completed replies and recorded counts while the user stays in the desktop app. A cloud-only chat or unsupported local format may have no usable history. The monitor publishes the four ranked slots. Each slot can pin a chat, follow the latest matching chat in a project, or return to automatic selection. A tooltip gives a brief status and a click opens the relevant conversation. Recent, ready, working, attention, finished, failed, interrupted, and quota states have distinct visuals. New coding completions and dock job completions can pulse the rim once. Motion respects Windows settings.

The same [Coding workspace](../src/WindowsDock/DashboardCodingWorkspace.cs) serves Dashboard **Chats** and clicks on the native strip. The conversation rail searches or filters Codex and Claude chats. A compact selector replaces it at narrow widths. The right pane has **Reply**, **Approval** when pending, **Agent slot**, and **Usage**. **Jobs**, **Activity**, and **Setup** mount in that pane, including job compose and detail. **Open chat** intentionally hands off to the provider app.

- **Reply and summary.** The original reply and result live in the same window. **Summarize reply** immediately uses the saved provider and model. **Source & provider** is optional and supports editing or pasting the source, choosing a model, and managing a key. Copy summary, regenerate, and return to the original are available. A summary for the selected chat stays available when switching pane tabs. [DashboardAgentPanels.cs](../src/WindowsDock/DashboardAgentPanels.cs) also supplies the shared summary builder for notifications.
- **Approval.** [AgentHookSetup.cs](../src/WindowsDock/AgentHookSetup.cs) installs or removes dock-managed hooks while preserving user configuration. Enabling live status and enabling one-time approval relay are separate choices. Codex hook review and loading new Claude Code settings are provider steps. [AgentApprovals.cs](../src/WindowsDock/AgentApprovals.cs) shows the pending tool, location, and arguments, returns allow or deny, and yields back to the provider after a 30-second timeout. Temporary approval payloads are local.
- **Setup.** [AgentDefaults.cs](../src/WindowsDock/AgentDefaults.cs) writes provider defaults with backups. It can set model and thinking where supported, and manages defaults for sessions that load them. An already-open session can have its own overrides.
- **Usage.** [AgentUsage.cs](../src/WindowsDock/AgentUsage.cs) and [CodexUsageClient.cs](../src/WindowsDock/CodexUsageClient.cs) combine recorded chat tokens with Codex plan percentages and reset times when available. Claude Desktop contributes its last locally cached percentages. The Claude usage page covers account details and resets that the cache does not provide.

## Personas, providers, and jobs

The 1×4 [assistant picker](../src/WindowsDock/DashboardAssistantGrid.cs) is a separate surface from the taskbar coding slots. Keys `1` through `4` select a character. [AssistantPersonas.cs](../src/WindowsDock/AssistantPersonas.cs) owns the voices, art, and starters. [DashboardAssistant.cs](../src/WindowsDock/DashboardAssistant.cs) keeps separate drafts and conversation histories per persona in the dashboard process. Context previews can attach to a draft, replies can be copied, and `Ctrl+Enter` sends the current draft.

| Character | Role and available context |
| --- | --- |
| Lark | Research and evidence checks. Quick action previews an Exa web search. |
| Fern | Housekeeping and day planning. Quick action previews today or a week of local reminders and imported events. The planner omits calendar join links. |
| Bolt | Coding, debugging, and review. Quick action accepts a reviewable excerpt of pasted logs. Microsoft Learn and the optional read-only GitHub PR reader are available. The PR reader uses `GITHUB_MCP_TOKEN`. |
| Orbit | Conversation, brainstorming, and dispatch. Idea jar adds a starter. The casual role also supports Exa when enabled. Orbit can propose background work for Lark, Fern, Bolt, or Scribe. |
| Scribe | Writing and editing specialist available through an Orbit handoff. Writing jobs can use enabled Exa context. |

[AssistantConnections.cs](../src/WindowsDock/AssistantConnections.cs) defines the role allowlist and remote MCP tools. [PlannerMcp.cs](../src/WindowsDock/PlannerMcp.cs) serves the local schedule over stdio through `--planner-mcp`. A user can fetch and review context manually, then attach it before a send. An assistant turn can also request a bounded number of reads from its enabled tools relevant to the task. Keep the manual attachment flow and the in-turn lookup flow distinct when reviewing data disclosure.

| Provider surface | Current channels |
| --- | --- |
| Reply and notification summaries | Gemini and Groq API keys, or signed-in Codex CLI and Claude Code CLI. The saved provider and per-provider model apply across both surfaces. CLI requests receive the selected source text in an isolated temporary directory, with tool use and session history disabled. See [TextModelClient.cs](../src/WindowsDock/TextModelClient.cs) and [CodingSummaryClient.cs](../src/WindowsDock/CodingSummaryClient.cs). |
| Persona chat | The two signed-in local CLIs, Gemini, Groq, Grok, Claude API, and the browser-authorized ChatGPT plan path. The Claude API route uses a separate Console key. A plan or quota error can offer a switch to a fallback provider followed by an explicit resend. See [DashboardAssistant.cs](../src/WindowsDock/DashboardAssistant.cs) and [ChatGptPlan.cs](../src/WindowsDock/ChatGptPlan.cs). |
| Background jobs | Orbit can propose a brief for a specialist. Confirming starts the job. Bolt also has a direct compose form and requires a selected local Git root. [DockCodingJob.cs](../src/WindowsDock/DockCodingJob.cs) invokes a standalone Codex CLI with a workspace-write sandbox. [DockWorkspaceEditor.cs](../src/WindowsDock/DockWorkspaceEditor.cs) is the scoped text-edit path when a suitable CLI or shell runner is unavailable. The job result distinguishes shell checks from text-only edits. |

[DockAssistantTurn.cs](../src/WindowsDock/DockAssistantTurn.cs) interprets assistant tool proposals. [DockActions.cs](../src/WindowsDock/DockActions.cs) validates a fixed set of operations. Every persona can read supported dock state and propose master or app volume changes, mute, default output, preset recall, playback, reminders, timers, radio state, start at sign-in, widget layout, and marking Activity read. The user reviews the concrete proposal and chooses **Apply changes**. An external coding agent can run `WindowsDock.exe --dock-mcp` (append `--tray` for tray mode) for `dock_status`, `dock_lookup`, and `dock_action`. The [MCP server](../src/WindowsDock/DockMcp.cs) prompts on the desktop before applying an external write.

Jobs run in the dashboard process and can outlive the visible panel. [DockJobs.cs](../src/WindowsDock/DockJobs.cs) stores status and results. The app runs up to two jobs concurrently, exposes cancellation, and presents a job as interrupted if its worker is gone. [DashboardAssistantJobs.cs](../src/WindowsDock/DashboardAssistantJobs.cs) owns handoff confirmation, direct coding-job creation, and job actions. Job prompts, transcripts, notifications, attached context, tokens, and paths can be private. Use synthetic text and a disposable Git workspace for tests and screenshots.

## State and integration boundaries

- [DockData.cs](../src/WindowsDock/DockData.cs) stores managed state under `%LOCALAPPDATA%\WindowsDock\Dashboard`. It writes through sibling temporary files and uses a named mutex for read-modify-write operations. Key examples are `widget-layout.json`, `summary-preferences.json`, `weather-city.json`, `weather-celsius.json`, `focus.json`, `reminders.json`, and `assistant-jobs.json`. Agent slots and the legacy `dock.json` live under `%LOCALAPPDATA%\WindowsDock`.
- [DockCredentials.cs](../src/WindowsDock/DockCredentials.cs) protects saved API keys for the current Windows user. The ChatGPT account cache is also Windows-protected. The API and CLI routes can send user content to their selected providers. The ChatGPT plan route uses browser consent and account eligibility. The Claude API route uses Claude Console credentials. The CLI routes use the provider's local sign-in and usage limits.
- Windows permissions and active devices constrain notification history, location, clipboard history, radio changes, foreground activation, and audio routing. The app surfaces access failures or opens appropriate system settings. Native shell layout and some audio routing interfaces require Windows-version coverage.

## Build and verify

Use Windows, the .NET 10 SDK, MSVC x64, and the Windows SDK with C++/WinRT headers. The project requires CMake 3.25 or newer. The Visual Studio 2026 generator below requires CMake 4.2 or newer. If CMake is bundled with Visual Studio, use its Developer PowerShell or its CMake executable. This stages a build without replacing the installed app.

```powershell
dotnet build src/WindowsDock -c Release
dotnet publish src/WindowsDock -c Release -r win-x64 --self-contained -o artifacts/review-dock
cmake -S src/NativeTaskbar -B artifacts/review-native -G "Visual Studio 18 2026" -A x64
cmake --build artifacts/review-native --config Release
Copy-Item artifacts/review-native/Release/NativeTaskbar.exe,artifacts/review-native/Release/NativeTaskbar.Xaml.dll artifacts/review-dock
.\artifacts\review-dock\WindowsDock.exe --dashboard
```

For an installed native check, stop the existing controller gracefully before using a second build. Keep the native executable and its matching DLL beside the complete managed publish output. If Explorer has retained the DLL, build in a separate directory and set `NATIVE_TASKBAR_MODULE_NAME` to a distinct basename. See [README](../README.md) for native and tray launch commands. Test shell changes on the supported Windows build with a recovery path available.

**Prior baseline evidence.** The `7ceca1b` build passed the managed Release build, self-contained publish, and local WPF smoke suite. The suite covered all eleven Dashboard pages, all twelve widget routes, shared navigation, split and narrow chats, summary preference persistence, and saved widget layout. A signed-in Codex CLI request summarized a synthetic reply on the first click and retained it when navigating to Agent slot and Usage. Windows UI Automation invoked a real native slot, found the workspace and all four taskbar slots, and confirmed that Agent slot used the same 764 × 454 pixel window and handle at the tested DPI. The local smoke project `artifacts/WidgetSmoke` and sample preview `artifacts/coding-workspace-summary.png` are ignored artifacts. A fresh checkout requires its own fixture or manual checks.

**Checks still open.** Test physical two-finger gestures, mixed DPI and monitor boundaries, high contrast, reduced motion, native restoration after Explorer restart, and tray coexistence. Verify audible Brave playback on Bluetooth and simultaneous streams on different devices, both radio toggles, actual battery notification delivery, and startup after a real sign-in. Repeat provider checks with a signed-in Claude Code CLI, an eligible ChatGPT plan account, and API keys as available. Prior shell-based Bolt checks on this PC hit a package-identity error. The scoped editor completed a live test edit. Keep those results separate. Prior idle memory measurements cover the controller and monitor on one PC and exclude the Explorer module. Re-measure before claiming a change in footprint.

## Review and redesign priorities

1. Trace normal entry points first. Review shell attach and rollback, event and shared-memory contracts, then WPF navigation, async cancellation, and how a selected conversation, summary, draft, and timer survive a view change.
2. Exercise data and permission boundaries. Check approval expiry and session identity, persistence migration, model and MCP input handling, credential redaction, confirmation before writes, job cancellation and workspace confinement, and partial audio routing failures. Separate observed defects from risks that need a Windows, device, or account reproduction.
3. Redesign a complete primary flow in the running app. Start with taskbar launch, chosen chat, one-click summary, then one inline follow-up action. Carry the same control language into Dashboard, Widgets, and Assistants. Preserve the user's desired native layout, remembered choices, and accessible gestures and click targets.
4. Capture before and after screens using identical synthetic states. Include a long reply with a summary in the same window, an approval state, and a narrow view. Show that the fixed dimensions, scroll behavior, focus states, and native taskbar targets still work. Report live evidence separately from fixture results.
