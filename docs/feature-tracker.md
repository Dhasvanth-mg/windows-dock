# Feature tracker

## System expansion features

- Status: First feature increment built, installed, and verified in the tray session. The wider specification is in progress.
- Scope: Implement the feature workflows from the supplied system expansion specification in working increments. Reuse the current design and integrated dock surfaces. Preserve the existing redesign, settings, and taskbar behavior.
- Acceptance criteria: Confirm the installed baseline and run the existing suite. Each shipped increment has a real entry point, functional behavior and an error path, persistence where applicable, and targeted verification. Record live, simulated, and deferred work distinctly.
- Implementation: Added global Search with Ctrl+Alt+K, offline calculator results and history, Wi-Fi and Bluetooth panels, grouped favorites, and named workspace window layouts. Search and Widgets share the new entries. Bluetooth pairing and new Wi-Fi credentials use Windows Settings. Existing shortcut-only workspaces remain supported.
- Verification: The original native installation and 43 baseline tests passed. The final working-tree suite passed all 80 tests, and the staged feature snapshots passed their own suites. The x64 native controller and managed Release publish built. Windows UI Automation verified a calculator result, Wi-Fi signal and paired Bluetooth status, saving and finding a favorite again, and capturing open windows. Unit tests checked workspace placement on a secondary and a smaller work area.
- Performance: One warm tray Search reveal took 120 ms from the controller's hotkey message to visible panel. Windows released the dashboard worker after closing it. The tray controller and monitor then used 37.7 MiB private memory and an 83 MiB combined working set, with no CPU time measured during the five-second idle sample.
- Limitations: Live radio toggling, switching networks, and moving the owner's open windows were left untested. Workspace capture stores supported visible windows and geometry. Exact app state, browser tabs, and documents still depend on manual workspace links or the application restoring its own session. The calculator works offline and has no live exchange rates.
- Remaining work: Restore the native launcher in a shell session that accepts the existing taskbar connection. Continue the remaining Windows control and workspace features, provider and MCP expansion, calendar and document integrations, voice, local indexing, knowledge graph, encrypted vault, and broader performance checks.

### Continue implementation and installed-build checks

- Status: Installed. Search and the feature entry points were checked on Windows. Native launcher recovery is pending.
- Scope: Continue the feature expansion and finish the installed-build checks for Search, the Windows controls, favorites, workspaces, sign-in, and panel locking.
- Acceptance criteria: Verify the installed entry points and behavior, recover the native launcher if Explorer accepts the connection, and record any remaining limitations.
- Outcome: Ctrl+Alt+K registered in the running controller and opens Search. Windows already had Ctrl+Alt+Space registered. The new and previous controllers both fell back to the tray after Explorer rejected the existing taskbar connection. The installed build stays on the native-preferred startup path and can use its tray launcher while the current Explorer session rejects the connection.
- Remaining work: Verify the taskbar launcher and four-slot row when Explorer accepts native attachment again.

### Ollama for Assistants

- Status: Installed. Simulated model discovery and conversations are verified.
- Scope: Allow Assistants to use installed Ollama text models through the local Ollama service.
- Acceptance criteria: Selecting Ollama lists available local models and requires no API key. The assistant sends to the loopback service, retains the existing dock action approval flow, and handles missing models or a stopped service. Simulated tests cover listing and sending.
- Implementation: Added Ollama to the Assistant provider picker. It fetches installed local models on selection or refresh. It checks that the selected model is still installed before sending, filters cloud-backed aliases, and uses a 30-second model keep-alive so the local service can release model memory after a turn.
- Verification: The provider tests covered request formatting, roles, loopback transport and rejection of remote or uninstalled models before sending any message. The installed app handled a stopped service, refreshed against a synthetic loopback service, selected the local model, and displayed its reply. The staged suite passed all 68 tests.
- Remaining work: Verify generation time and memory with an actual installed local model. No Ollama model was installed during this check.

### ChatGPT browser sign-in

- Status: Implemented, installed, and verified with synthetic OAuth callbacks. A real account sign-in still requires the owner.
- Scope: Fix the ChatGPT plan callback that reached an unavailable loopback listener.
- Acceptance criteria: The active sign-in can survive using the browser and leaving the assistant panel. It is cancelable, expires cleanly, and still validates the OAuth state and account before saving credentials. Verify the callback with synthetic data.
- Implementation: The dock session owns the sign-in task for up to five minutes. The assistant panel can close or switch without canceling its loopback listener. Reopening the assistant can resume observing that task. Explicit cancel and dock shutdown still stop the attempt.
- Verification: The TCP callback tests accepted a matching synthetic response after the panel observer closed, rejected a mismatched state, and confirmed single-attempt and cancel behavior. No real account tokens were requested during verification.
- Remaining work: Start a fresh browser sign-in through Continue with ChatGPT and confirm the owner's account exchange and access.

### Independent panel dismissal and locks

- Status: Implemented, installed, and verified from Windows UI Automation.
- Scope: Close only the selected agent or tool panel and restore Dashboard when it was open. Closing Dashboard closes its unlocked panels. Add a session lock to Dashboard and each panel.
- Acceptance criteria: Close buttons, Escape, and panel gestures respect the active panel. A locked panel survives navigation and another panel's dismissal and can be unlocked from the dock. A normal Dashboard close returns the dock to its idle state when no locked panel or task remains.
- Implementation: Tool close returns to the previously open Dashboard. Dashboard close dismisses unlocked panels. Dashboard, compact Coding chats, and tools have session locks. The Locked panels menu returns to a locked panel after switching surfaces. Closing a surface leaves any active background job or browser sign-in running.
- Verification: The installed build passed Dashboard lock and unlock, Assistants-only and Coding-chats-only close, restoration of a previously open Dashboard, and survival of the same locked Assistants window after Dashboard closed.
- Limitations: Locks reset when the dock process exits. Explicit application exit closes its surfaces.
- Remaining work: None for the verified close-button flows.

## Dock startup and panel reliability

- Status: Implemented, verified, and installed in native taskbar mode.
- Scope: Address the native startup failures exposed by the redesign rollout and make Dashboard and Coding chats open reliably from the existing launchers, shortcuts, and sign-in entry.
- Acceptance criteria: The controller publishes a usable entry point only after it is ready. The requested view opens on a cold launch and when the dashboard is already running. Native startup uses a single shell attachment and restores its styling on failure or exit. A styling conflict keeps the dock usable through a working tray session. Preserve the existing redesign and Hitodama coexistence.
- Implementation: Decoupled appearance ownership and cleanup from controller lifetime, prevented a partial visual tree subscription from aborting another ready callback, and added a bounded retry for Explorer's startup window. Native styling denial falls back to the tray. The sign-in and shortcut launchers share a ready-controller check, and a click on the tray icon opens Dashboard.
- Verification: The native Release build and self-contained managed publish passed. All 43 working-tree tests and all 31 tests in the staged source passed. The rollout reproduced an abandoned native appearance and verified automatic tray fallback. Refreshing Explorer cleared the earlier session's styling. The installed build then stopped, restored, and reattached in the same Explorer process. The sign-in command started one controller, two launcher requests showed the same dashboard, and a native slot opened inline Coding chats.
- Limitations: The initial shell refresh cleared styling left by a previous build. Hitodama's appearance is paused for native mode while its assistant is still running. Sign-in registration and the launch command were verified in the current session.
- Remaining work: None for the installed controller and panel routes.

### Faster reveal and original dock direction

- Status: Implemented and verified on the installed native dock.
- Scope: Shorten the delay between requesting the dock and seeing it. Refine the taskbar and first-open experience against the owner's original centered, macOS-inspired frosted dock direction.
- Acceptance criteria: Record repeatable launch or reveal timings, preserve low background resource usage, and give the primary dock launcher, four-slot status row, and Windows tray and clock clear placement on a centered surface. Preserve fixed-size, same-window chat interactions and the current feature access.
- Implementation: Refined the shared acrylic, grouped the launcher, four wider animated slots, system tray, and clock, and shortened the upward pull threshold. A launcher press can start the hidden dashboard ahead of the release gesture. The managed panel renders before its live refresh, pauses its scan while hidden, and uses a compact 896 DIP main width with the same 454 DIP height.
- Verification: Reviewed the original brief and reference video. Windows UI Automation found the visible launcher and four same-row slots, opened a selected chat inline, and measured 896-by-454 and 764-by-454 panels at 96 DPI. Captured the real taskbar. On this machine, the earlier tray run recorded 2,692 ms cold and a 446 ms warm median. The final native run recorded 687 ms cold and a 15 ms warm median across five warm samples, with one 381 ms warm sample.
- Remaining work: None for this visual and reveal pass.

## Fix top repository-analysis findings

- Status: Built and verified by builds, tests, and standalone dashboard measurements. Not yet run on the installed native taskbar.
- Scope: Fix the highest-priority findings from the full repository analysis: no logging, no committed tests, unconfirmed `dock_lookup`, loose coding-job confinement and environment scrubbing, silent overwrite of corrupt data files, hook-config backup growth, comment loss and install-path fragility, an untracked reference video, and two small code nits. Out of scope: the 750 ms in-Explorer timer, splitting `DashboardWindow`, and removing the legacy companion.
- Acceptance criteria: Managed and native Release builds pass with no new warnings. A committed test project covers the new logic and passes. No prompts, replies, or credentials are written to logs.
- Implementation: `DockLog` (managed `dock.log`) and `Log.h` (native `native.log`), both size-rotated and writing exception types and HRESULTs, with messages only for startup and crash events. Logging added at crash, startup, controller failure, Explorer styling, MCP, and job failure points. `DockData` moves unreadable files aside (newest three kept) and `Update` aborts instead of overwriting after a read failure. `dock_lookup` now asks for desktop approval like `dock_action`. Coding workspaces reject drive roots, the user profile, and system folders, and job environment scrubbing now covers secret, credential, cookie, `_PAT`, `_AUTH`, `AWS_`, `AZURE_`, and similar names. Hook and defaults writes keep the newest three backups, refuse to rewrite a JSON config that contains comments, take the install path from `AppContext.BaseDirectory`, and give status hooks a 5 second budget. Added `tests/WindowsDock.Tests` (xUnit). Added `inspiration/` to `.gitignore`, fixed a `%zu` argument type in `DockTray.cpp`, and fixed an indentation nit.
- Verification: Managed and native Release builds pass with 0 warnings and 0 errors. 31 of 31 tests pass. The 5 new native and 1 changed managed files compiled in the scratch build output.
- Limitations: Hook installs made earlier keep their 2 second timeout until reinstalled. The environment scrub is still a name-pattern heuristic. Native logging was compiled but not exercised in Explorer, because the app was not installed over the running dock. `dock_lookup` approval was not exercised against a real MCP client.
- Remaining work: Install the new build to confirm the native log and the dashboard size on the real taskbar.

### Dashboard window resizes every time it opens

- Status: Fixed and verified for the standalone dashboard. Native taskbar not re-run.
- Scope: Message received during the fixes above. The user reports that each time the dashboard appears its tab or window is resized. The screenshot shows the Overview page clipped at the bottom of the panel, with the calendar, weather, and CPU/RAM tiles cut off.
- Acceptance criteria: The dashboard opens at the same size on every show, with the same anchor above the taskbar, and the Overview content fits inside it.
- Findings: Repeated standalone opens were already a stable 960 by 412. Overview needed about 297 px of a 290 px viewport, so its bottom was clipped. Dashboard (412) and Chats (454) had different heights, so moving between them resized the window vertically. A pre-show measurement was tried and rejected, because media, weather, and clock text are filled in after layout and it under-measured by about 12 px.
- Implementation: Dashboard and Chats now share one height, `PanelHeight` 454 DIP, set in `ConfigureWidth`. The handoff document states the new size.
- Verification: Three repeated opens each gave 960 by 454 at the same position (240, 392) at 96 DPI. Overview now has a 332 px viewport for about 297 px of content and is no longer scrollable. A window opened from the Chats panel could not be tested without the native controller.
- Limitations: Switching between Dashboard and Chats still changes the width (960 to 764). A user with a large DPI scale or a small display is clamped by `MaxHeight`, so Overview can scroll there.
- Remaining work: Confirm on the installed native taskbar.

## Full repository analysis

- Status: Complete. Analysis delivered in chat; no source changes.
- Scope: Analyse the whole Windows Dock repository (structure, architecture, code quality, risks, and verification state) and report findings. Analysis only; no source changes requested.
- Acceptance criteria: Cover the native C++ taskbar module and the .NET WPF app, their boundaries, persistence and security-relevant surfaces, documentation accuracy, and the result of a fresh build where possible. Findings cite files.
- Implementation: Read the docs, project files, the whole native layer (Main, Diagnostics, TaskbarStyle, TaskbarAgents, DockTray, Connection, AgentGridState, BatteryAlerts), and the managed entry point, data, credentials, approvals, hooks, MCP, actions, coding jobs, workspace editor, summary and ChatGPT clients, monitor, shared-memory bridge, startup, and launch paths. The Dashboard\*.cs UI partials were sampled (widgets, meetings, GitHub, agents), not read line by line.
- Verification: Fresh managed Release build (0 warnings, 0 errors) and native Release build (CMake with VS 18 Build Tools) both succeeded, with output in a scratch directory outside the repo. Confirmed the C++ and C# shared-memory offsets agree (tile 528, selection 204, quota offset 2320). Confirmed 37 commits, all by the owner identity, with no coauthor or generation trailers. The app was not launched, because that would restyle Explorer, and no automated tests exist in the repository to run.
- Limitations: Static review only. Findings about runtime behavior, UI confirmation text, and external service contracts (ChatGPT plan OAuth, model IDs, API version) are unverified.
- Remaining work: None for the analysis. Follow-up fixes are up to the owner.

## Code review and UI redesign handoff

- Status: Complete.
- Scope: Document the current Windows Dock functions and implementation for a new agent performing a code review and UI redesign.
- Acceptance criteria: Provide a source-linked handoff that covers entry points, features, architecture, limitations, and verification. Provide a separate prompt ready to paste into a fresh agent chat, with review and redesign deliverables and user preferences.
- Implementation: Added the engineering handoff and paste-ready review and redesign prompt, and linked both from the README. The handoff inventories all eleven Dashboard tabs, all twelve widgets, the native and tray entry points, Coding chats, persona providers and jobs, and the persistence and permission boundaries.
- Verification: Audited the source and the recorded `7ceca1b` smoke and installed checks. Rebuilt managed and native Release targets, produced a self-contained x64 staging publish, and verified the internal document links and table structure.
- Limitations: This work documents prior live results and open hardware, account, and compatibility checks. The new agent performs the review and redesign.
- Remaining work: None for the handoff.

## Repository initialization

- Status: Complete.
- Scope: Initialize the empty Windows Dock repository and connect the local checkout to GitHub.
- Acceptance criteria: Create a main branch, add the project README and repository instructions, and verify the initial commit on GitHub.
- Implementation: Added README.md, AGENTS.md, and this tracker in the initial commit.
- Verification: The initial commit passed the staged whitespace check, and the GitHub main ref matched the local commit.
- Limitations: This request covered repository setup. Application requirements and implementation have yet to be defined.
- Remaining work: None for repository initialization.

## Standalone Windows Dock

- Status: In progress.
- Scope: Build the Windows 11 dock, visual Studio, system integration, profiles, extensions, recovery, and distribution described in the product brief.
- Acceptance criteria: Install a standalone application, edit the centered glass layout live, use it for daily switching and system access, and restore native Windows behavior safely.
- Implementation: Added the shell feasibility matrix and a WPF companion dock with grouped window discovery, executable and shortcut pins, running indicators, live appearance settings, clock/date, system Settings links, and backup-backed JSON. Added single-instance startup and a session-only Safe Mode.
- Verification: Release build and self-contained x64 publish passed. Windows UI Automation verified dock visibility, the Explorer pin, duplicate prevention, Studio opening, live dock resizing, and clean shutdown with Studio open.
- Limitations: Real app activation, minimize/restore, mixed-DPI monitor transitions, and settings recovery still need integration coverage. The initial layout includes Explorer, settings links, and the clock. ChatGPT and Brave defaults, Start, the tray layout, and live system status remain pending.
- Remaining work: Complete companion integration testing, then implement the visual layout editor, full default preset, system integration, auto-hide, profiles, extensions, replacement validation, and installer/release.

### Commit cadence and attribution

- Status: Verified for this milestone.
- Scope: Commit stable milestones regularly using the configured owner identity.
- Acceptance criteria: New commits use the owner as author and committer, with no AI attribution or coauthor trailers in commit metadata or project outputs.
- Verification: Architecture and implementation were committed separately. All commits use Dhasvanth-mg as author and committer and contain no coauthor or generation trailers.
- Remaining work: Continue this cadence and recheck attribution at each milestone.

## Centered panels, reveal, and Studio

- Status: Built and smoke-tested on one Windows display.
- Scope: Center dock-owned popups, add an overflow panel and tray access, improve reveal, and expand Studio with appearance and widget controls.
- Acceptance criteria: Dock-owned popups center above the dock, reveal works from configured triggers, and Studio changes appear immediately.
- Implementation: Centered dock menus and added a quick-controls overflow panel, a Windows notification-area icon, edge and hotkey reveal, and a dark Studio with four settings pages. Added battery, network, memory, and date widgets.
- Verification: Release build and self-contained x64 publish passed. Windows UI Automation checked overflow geometry above the dock, live memory widget insertion, idle hide, physical edge reveal, and the hotkey message handler.
- Limitations: Windows owns the location of its own flyouts and tray menu. The dock overflow opens Windows Settings for system actions. Mixed-DPI, fullscreen reveal, the physical hotkey chord, and tray clicks were not verified. Widget support covers the four built-ins.
- Remaining work: Drag-and-drop layout, custom widgets, inline system toggles, and broader monitor and fullscreen testing.

## Dividers, shortcuts, media, and assistant widgets

- Status: Built and smoke-tested on Windows.
- Scope: Add editable dividers, shortcuts, and media controls, and assess a compact mixer and optional API-key chat.
- Acceptance criteria: Studio edits update the dock live, the media panel opens centered, and any unavailable integrations have an explicit fallback.
- Implementation: Added live layout editing for dividers and shortcuts at four section boundaries, a centered media and mixer popup using Windows Core Audio, and an optional Gemini/Groq chat widget with a window-scoped key and conversation.
- Verification: Release build and self-contained publish passed. Windows UI Automation opened Studio directly, added a divider, opened the media popup and found transport controls, and opened the Assistant key form.
- Remaining work: Live provider calls require credentials. Actual playback changes, per-app volume changes, and shortcut launches still need manual coverage. Mixer targets the default output and offers Windows Settings for routing.

### Settings access

- Status: Verified on Windows.
- Scope: Provide a visible, direct Studio entry point after the screenshot showed the Windows tray overflow.
- Acceptance criteria: Open Studio directly from a labeled dock button.

- Outcome: Settings is a labeled button outside the scrollable app row. UI Automation verified that it was visible and opened Studio in the published executable. The screenshot shows the Windows tray overflow.

## Restart Windows Dock

- Status: Verified.
- Scope: Restart the latest published Windows x64 build.
- Acceptance criteria: Verify that the published process stays running and exposes its Settings button.
- Outcome: Started the published executable and verified its dock window and Settings button through Windows UI Automation.

## Publish local milestones

- Status: Verified.
- Scope: Push local main commits to GitHub.
- Acceptance criteria: GitHub main matches local main and new commits use the configured owner identity.
- Outcome: Pushed the five local milestones and confirmed that GitHub main matched the local tip. Verified owner attribution and commit messages.

## Match the reference dock and flyout

- Status: Built and verified on one Windows display.
- Scope: Match the screenshots with a compact dark dock, grouped status icons, and a centered quick-controls flyout.
- Acceptance criteria: Verify compact geometry and flyout placement on Windows and preserve direct Studio access.

- Follow-up: User rejected the visual quality. Prioritize the screenshot proportions and replace the list flyout with a custom tile surface.
- Outcome: Reworked the live dock into a compact dark bar, grouped status icons, and a centered six-tile quick-controls surface with volume and battery. Kept the Studio gear outside the scrolling row.
- Verification: Published self-contained x64, launched the updated build, measured a 38 px dock, checked flyout centering and Studio access, and visually inspected the captured output.
- Remaining differences: The reference shows native Quick Settings with a media card, brightness slider, and live system toggles. The custom popup uses Settings links for its tiles, with its own output-volume slider. App icons depend on running and pinned applications.

## Style the native Windows taskbar

- Status: Standalone dock stopped. Native styling pending administrator access.
- Scope: Apply a macOS-inspired style to Explorer’s existing taskbar while keeping native Windows controls and flyouts.
- Acceptance criteria: The separate dock is stopped and Explorer still owns the taskbar, icons, tray, and flyouts.
- Outcome: Stopped the project’s standalone process and found Windhawk Taskbar Styler 1.11.1 installed with the DockLike theme and existing overrides.
- Verification: The standalone process exited. Launching the installed Windhawk application started its service. Taskbar Styler remains disabled, and Windows denied direct write access to its protected settings.
- Remaining work: Enable Taskbar Styler from an administrator Windhawk session, apply DockLike or refine its existing overrides, then visually verify the native dock and Windows flyouts.

## Hitodama-inspired custom dock

- Status: Custom dock built and visually checked. In-place taskbar styling remains open.
- Scope: Research Hitodama and implement a cohesive macOS-style dock with integrated date, time, and status, independent of Windhawk.
- Acceptance criteria: Verify the visual treatment on Windows and document whether the result modifies Explorer in place.
- Outcome: Replaced the taskbar-like appearance with a floating layered glass treatment, larger native app icons, running dots, and an integrated clock and status group. The status and clock buttons invoke Windows shortcuts for Quick Settings and notifications. No Windhawk dependency was added.
- Research: Hitodama publicly describes a Windows assistant. Its taskbar implementation could not be verified. Apple's macOS Tahoe design informed the dock visuals.
- Verification: Self-contained x64 publish passed. Started the build and captured the 529 by 56 px dock on Windows. UI Automation found Explorer, the status button, and the Settings gear. The Quick Settings invocation did not produce a conclusive automation result.
- Remaining work: The dock still runs as a separate companion surface. Explorer's taskbar remains active. In-place restyling, confirmed native flyout invocation, and production release testing remain open.

## Native taskbar design correction

- Status: In-place styling and restoration verified on Windows 11 build 26200.
- Scope: Replace the rejected companion preview with custom styling of Explorer's native Windows 11 taskbar. Keep native Start, apps, notification area, clock, and flyouts. The visual target is a centered macOS-inspired glass dock with a single shared surface and no magnification.
- Acceptance criteria: Use no Windhawk dependency, verify that Explorer still owns the interactive taskbar, validate the rendered layout on this Windows build, and verify a way to restore the original appearance.
- Verification: XAML Diagnostics reached the Explorer-owned app and tray controls. The compact bar rendered with the native clock and tray on the same glass surface. A stop request restored the full-width bar without restarting Explorer.

### Video and DG-Dock reference

- Status: The first clip is reviewed. The native dock and core dashboard pages are implemented.
- Scope: Inspect the saved inspiration video and DG-Dock source, then reproduce the demonstrated appearance and behavior using the applicable implementation route.
- Acceptance criteria: Verify the video reference, account for any differences between it and the source, and test the resulting UI on Windows.

### Hitodama implementation reference

- Status: In-place taskbar styling and the dashboard are working on the tested build.
- Scope: Inspect Hitodama's taskbar and the supplied demo, then reproduce their visible design and interactions with independent project code.
- Acceptance criteria: Native Explorer taskbar styling and a same-edge dashboard, with evidence that the two work together on Windows.
- Research: The installed app has a taskbar appearance component running inside Explorer. The public site focuses on the assistant. DG-Dock was reviewed as a separate reference for interactions and its code is excluded from this implementation.

### Additional reference clip

- Status: Reviewed and applied to the current dashboard.
- Scope: Inspect the newly attached MOV and incorporate compatible visual or interaction details into the native dock and dashboard.
- Acceptance criteria: Record the relevant details and verify the resulting implementation on Windows.
- Outcome: The second clip showed Docktor's widgets, lyrics, focus timer, and live app previews. Implemented the compact media/mixer and focus drawers, with Explorer retaining native app previews. Verified the live system statistics, media slider and audio-session list, calendar page, and timer countdown on Windows.
- Remaining work: Added opt-in LRCLIB line-synced lyrics. Live track matching and synchronization remain unverified. Full power/sleep scheduling, independent inline widgets, and the richer wallpaper carousel are outstanding. The dashboard currently uses Windows settings links for radio/display controls, a local wallpaper picker, and manual city search for weather.

### Continue native dock verification

- Status: Built and smoke-tested on Windows 11 25H2.
- Scope: Complete the pending native dock and dashboard build, live checks, and milestone publication.
- Acceptance criteria: Publish a build, verify the dashboard can reopen in native mode with auto-hide restored, and record remaining gaps.
- Outcome: Built and launched the x64 native dock and dashboard. Scoped styling to the primary Explorer taskbar. Added an opt-in lyrics drawer and kept the dashboard host alive between openings.
- Verification: Rebuilt and published both components. Confirmed normal restore and controller-termination recovery without restarting Explorer, and confirmed a second native dashboard request reuses the host with Windows auto-hide enabled. Earlier live UI checks covered the media session list, calendar timer, and system graphs. A sample weather request returned a five-day forecast. The final screen capture was unavailable.
- Desktop state: Restored the original Windows auto-hide preference. Turned off Hitodama's taskbar appearance while this dock is active to avoid styling the same Explorer controls twice. The original appearance rules are backed up locally in the ignored artifacts directory.
- Remaining work: Physical reveal and click-through with the final build, live track lyrics verification, other Windows builds, other monitors, and full reference parity.

## Mobile screenshot download

- Status: Verified.
- Scope: Package the two Windows screenshots as a phone-accessible download.
- Acceptance criteria: Verify the archive includes both PNGs and provide a working remote link.
- Outcome: Published a screenshot-only GitHub prerelease with both PNGs in a ZIP. Verified the public download and archive contents.

## Desktop coding session dashboard

- Status: Built and smoke-tested on Windows.
- Scope: Review the Omarchy reference and add a dock view for Claude Code and Codex desktop sessions.
- Acceptance criteria: Show useful session information without requiring terminal sessions, and verify the dashboard on Windows.

### Desktop approvals and model controls

- Status: Built. Provider-level confirmation pending.
- Scope: Surface approval requests, display observed model and reasoning effort, and expose supported default model and effort settings from the dock.
- Acceptance criteria: Approval alerts lead back to the provider, default changes preserve unrelated configuration, and desktop behavior is accurately labeled.

### Keep approval and settings actions in the dock

- Status: Built and verified with simulated requests.
- Scope: Evaluate in-dock one-time decisions via supported hooks, and keep default model and thinking edits inside the dashboard.
- Acceptance criteria: Decisions are tied to a single pending request, and provider prompts remain available when the dock cannot handle a request.
- Outcome: Added the Coding chats dashboard with provider filters, recent desktop sessions, observed model and effort, status chips, and an approval queue. Optional per-provider hooks relay explicit one-time choices locally and yield to the provider after 30 seconds. The defaults editor writes supported provider settings and creates backups.
- Verification: Published x64 and restarted the native dock. Windows UI Automation verified local session cards and default controls. Simulated Claude and Codex requests exercised deny and allow through the Windows relay. Temporary TOML and hook configuration tests verified edits and preservation of unrelated settings.
- Remaining work: The live hooks require user enablement and provider trust where applicable. A genuine desktop approval from each provider and provider application of the saved defaults remain unverified. The editor does not change the model or effort of a chat already open in another client.

## Active chat controls and reply summaries

- Status: Summary popup built and smoke-tested. Active desktop chat model changes remain open.
- Scope: Investigate per-chat model and thinking controls and add a dock popup for summarizing recent assistant replies.
- Acceptance criteria: Verify any supported active-chat controls, and keep summary requests explicit and scoped to the selected chat.
- Outcome: The popup reads the selected chat's latest completed local reply or accepts pasted text. It previews the editable input, supports Gemini and Groq, and links back to the chat. Summary requests require an explicit click.
- Verification: Windows popup and transcript fixtures passed for both providers. Gemini and Groq response fixtures passed.
- Remaining work: Live summarization needs a provider key. The installed Codex desktop server uses private stdio, and its separately launched account server cannot control those turns. Its generated protocol exposes model and effort updates when connected to the host of a loaded chat. Claude Desktop applies per-session changes through its own picker. Existing desktop turns still require the provider's controls.

## Coding chat token usage and resets

- Status: Built and verified with live Codex account data and local Claude metadata.
- Scope: Add token usage and provider rate-limit reset times to the Coding chats dashboard.
- Acceptance criteria: Show reported counts and reset times where available, label their scope and freshness, and verify behavior when a provider does not report them.
- Outcome: Added the Usage panel with Codex account totals, quota percentages and reset countdowns, selected-chat token counts, and Claude Desktop's cached percentages. Claude transcript counts include input and cache usage where present.
- Verification: The signed-in Codex app server returned two quota windows with reset timestamps. Fixtures verified current Codex token records, deduplicated Claude input counts, and missing fields.
- Remaining work: Exact Claude reset times require the linked Claude usage page on this setup. Claude's local assistant records only expose placeholder output counts.

## Coucou-inspired coding agents

- Status: Built. Native attachment and panel routing verified.
- Scope: Review Coucou and redesign the Coding chats interaction around animated status, hover details, and slide-up actions.
- Acceptance criteria: Recognizable states for working, approval required, finished, and quota reached. A mouse or keyboard user can reach chat actions, reply summaries, and usage without leaving the dock.
- Outcome: Coucou's compact status, hover, and click patterns informed four original status tiles. Working, approval, reply, and usage-limit states have distinct symbols, colors, and animation timing. Reduced-motion settings are respected.
- Verification: The Explorer module attached, and the native event opened the centered slide-up panel. The four slots populated with local chats. Physical hover and animation capture were unavailable in the verification session.

### Minimal dock interface

- Status: Built and smoke-tested.
- Scope: Bring the new coding-agent experience into the existing native dock, drawing on Coucou's compact interaction and Hitodama's restrained styling.
- Acceptance criteria: A small 2×2 grid on the native taskbar displays up to four chat states. Hover identifies the chat and its current status. Click opens the chat's actions in a compact panel from the dock.
- Outcome: The 2×2 grid occupies the existing taskbar surface beside the dashboard button. Clicking routes to a 506-pixel chat panel on the tested display. The dashboard page initializes on first open.

### Dock and tray refinement

- Status: Built and smoke-tested on Windows 11 25H2.
- Scope: Refine the dock surface, tray, date, clock, and calendar presentation while keeping Windows controls usable and background work small.
- Acceptance criteria: Shared spacing and styling across the native dock and flyouts, with the clock and tray controls still using Windows interactions. Keep refresh and animation work scoped to what is visible or changing.
- Outcome: Refined the glass rim, tray and clock treatment, calendar, and battery readout. The native tray can display a compact charge glyph inside the existing Quick Settings control when the expected battery element is present.
- Verification: Both components built and the native controller attached to Explorer. The controller used about 3 MB private memory and the hidden dashboard about 55 MB before first open on the tested machine.
- Remaining work: Native battery glyph and hover visuals could not be captured in this desktop session. Other screen sizes and Windows builds remain unverified.

### Grid previews, navigation, and assignments

- Status: Built. Panel routing smoke-tested.
- Scope: Show a quick preview when hovering over a tile. Open an all-chat menu from the grid, surface pending approvals and fresh replies, and provide a direct open action and saved chat or project assignments.
- Acceptance criteria: Hover shows the tile's chat and status, click keeps all chats accessible and highlights an urgent or recently finished chat, and slot assignments survive a restart.
- Outcome: Each tile exposes a hover preview. The slide-up list prioritizes a pending approval or recent reply, includes a direct chat action, and can save a chat or project assignment per slot.
- Verification: The slot assignment, reply summary, and usage windows opened. The main and compact dashboard views reopened through the native event. A fixture confirmed that pinned chat and project slots retain their assigned positions. An off-screen WPF render verified the compact layout with sample sessions.
- Remaining work: Physical hover, deep-link handling in each provider, and persistence after a user changes a slot on the installed build remain to be checked.

### Automatic weather location and styled controls

- Status: Built. Forecast service verified with sample coordinates and city search.
- Scope: Add weather location detection and make the dashboard's controls visually consistent with the native dock.
- Acceptance criteria: Weather can use the Windows location permission flow with city search still available. Custom inputs, selectors, buttons, and scrolling remain readable and usable by keyboard.
- Outcome: Weather can request a Windows location fix, round the coordinates, and refresh when the dashboard is open. City search remains available. Dashboard buttons, selectors, scrollbars, checkboxes, fields, and menus use shared dark styles.
- Remaining work: Windows location permission and a live device fix require an interactive check.

### Widget ideas

- Status: Shortlist delivered. The owner selected all eight and asked for more ideas.
- Scope: Suggest additional widgets that fit the dock.
- Acceptance criteria: Provide a short list of useful, distinct widgets with the action each would make easier.
- Outcome: Shared the initial eight choices, then suggested mic/camera activity, saved window layouts, network status, a capture shelf, battery details, and a branch/worktree picker for a later selection.

### Selected widgets and notification summaries

- Status: Built and smoke-tested. Account and device limitations remain.
- Scope: Add the audio switchboard, meeting card, clipboard and file shelf, GitHub checks and reviews, workspace shortcuts, download queue, focus controls, command palette, and notification summaries. Suggest more widgets separately.
- Acceptance criteria: Every enabled widget has a usable dock entry, operates on real local or connected data, and identifies any Windows or service limitations. Notification text leaves the device only when the user sends it for summarization.
- Outcome: The Widgets launcher and Ctrl+K palette provide entry to the switchboard, meetings, clipboard and files, GitHub checks, workspace shortcuts, downloads, focus, and notifications. The digest reuses the editable Gemini/Groq summary popup.
- Verification: Nine WPF panels opened and laid out. An off-screen render verified the widget launcher. Recurring calendar import and meeting links passed with a fixture. Audio endpoints enumerated and an isolated process successfully changed and cleared its app route. Windows allowed clipboard and notification reads. The signed-in GitHub CLI returned this repository's empty PR, review, and Actions lists. The Windows transfer queue also responded with an empty result.
- Remaining work: Verify a physical output switch between devices, notification summaries with a real API key, and actual review and transfer rows. Meetings currently use a local calendar export. Browser transfer details come from partial files, and system Do Not Disturb is controlled through Windows Focus.

### Continue implementation and verification

- Status: Native build published locally and restarted. Scoped limitations are recorded above.
- Scope: Complete the queued native dock, coding chat, weather, and widget work, then publish a tested Windows build.
- Acceptance criteria: Build and launch the native dock, smoke-test new UI flows, record remaining provider constraints, and push the commits.
- Outcome: Release C++ and .NET builds passed. The native controller and dashboard loaded, and repeated native signals switched between the compact chat list and full dashboard. The installed build uses the existing native publish directory.

### Playful dock and panel refresh

- Status: Built and running locally. Release builds and panel smoke tests passed.
- Scope: Give the dock and its panels a more playful visual identity, with expressive agent tiles and a more distinctive widget launcher.
- Acceptance criteria: Preserve the compact 2×2 grid and native tray behavior. Make common actions easy to spot and keep status readable through shapes, labels, and reduced-motion behavior.
- Outcome: Added original pixel faces and state-specific motion to the agent grid, with static alternatives for reduced motion and high contrast. The chat panel now highlights the selected chat, its status, and its actions. The widget launcher groups actions into labeled tiles under a search field. The native dock and panels share a plum and lavender palette.
- Verification: The Release native and self-contained dashboard builds passed. Nine WPF panels opened and rendered, and the chat and widget previews were inspected. Explorer loaded the refreshed module, the four grid slots received live chat data, and native dock signals opened both centered panel sizes and switched back to the compact chat view.
- Remaining work: Inspect the native hover animation and high-contrast appearance on the physical desktop.

### Feature coverage and full dashboard refresh

- Status: Built, smoke-tested, and running in the native dock.
- Scope: Check the previously requested features in the current native dock and extend the visual refresh to the dashboard and its individual widgets.
- Acceptance criteria: Verify that the selected widgets and existing dashboard pages remain reachable from the native build. Give each surface a consistent layout and recognizable controls. Distinguish what works in the native dock from companion-only features and provider-dependent behavior.
- Outcome: Added labeled dashboard tabs, redesigned overview, audio, controls, calendar, weather, stats, file shelf, wallpapers, power, and settings surfaces, and styled each widget popup. The switchboard and focus timer open over the compact dock panel. The Gemini/Groq assistant now has a native dashboard popup. Widget popups have a back button and can open the command palette with Ctrl+K.
- Verification: All eleven dashboard pages laid out at 1060 and 640 DIP. Ten launcher routes opened from the compact chat panel, and the timer preset, countdown, reset, audio output list, assistant provider control, and back buttons worked. Inspected rendered dashboard and widget previews. Account quota, transcript, provider response, calendar recurrence, slot, and audio routing fixtures passed.
- Remaining work: Live Gemini/Groq replies and summaries, a physical audio output switch, an automatic location fix, and real approval events remain unverified. Active chat model and thinking changes remain in the provider. Custom dividers and inline shortcuts are companion Studio features. Native mode has Windows app pins and workspace shortcuts.

#### Continue the full dashboard refresh

- Status: Verified on the installed build.
- Scope: Resume the dashboard and widget changes in the current checkout.
- Acceptance criteria: Finish rendering and smoke checks, restart the native dashboard with the verified build, and publish the commit.
- Outcome: Published the self-contained x64 dashboard into the existing dock installation and relaunched it through the native controller. The controller stayed active, and its signals reopened the full dashboard, the compact chat panel, and the full dashboard again.
- Remaining work: Provider and device checks listed in the preceding entry.

### Faster widget and chat navigation

- Status: Built, smoke-tested, and running in the native dock.
- Scope: Improve everyday task and widget access in the native dashboard.
- Acceptance criteria: Frequently used widgets can be favorited and reopened from the dock. Search prioritizes strong matches and supports keyboard selection. Users can find chats, dashboard pages, and common actions through the same search.
- Outcome: Added saved widget favorites to the launcher and Overview. Search now ranks direct matches first and includes local chats, dashboard pages, widgets, installed apps, and workspaces. Selecting a chat keeps its details and actions in the dock. The search panel supports arrow keys and Enter, and closing a popup restores focus when its original control is still visible.
- Verification: Favorite and unfavorite round-tripped through local settings. A reopened search loaded the saved favorite first, ranked a dashboard page and widget keyword correctly, and opened a sample chat. The eleven pages and ten widget routes from the previous pass still passed. Inspected the launcher and search render.

#### Reduce idle memory

- Status: Built and running. Dashboard lifecycle measured on Windows 11 25H2.
- Scope: Reduce memory used by the native dashboard between openings and measure the running build.
- Acceptance criteria: Release unused UI and media resources when idle, preserve live chat status and timer behavior, and report measured memory and its limits.
- Outcome: The native controller starts a monitor without loading WPF. The monitor publishes chat status and tracks focus deadlines. The dashboard starts when requested and exits after twenty seconds hidden. The rollout scan reuses a byte buffer and decodes only candidate status records.
- Verification: Release native build and self-contained x64 publish passed. The monitor populated the live grid. Full and compact panels shared one process within an open session. The Close button hid the panel, its process exited after idle, and the native Agent event reopened it in a fresh process. Private committed memory across the controller and UI processes dropped from about 151 MiB to 31–33 MiB on this desk. The final idle sample showed 29 MiB of private resident memory across the controller and monitor. The figures exclude the in-process Explorer module.
- Desktop state: Explorer restarted during deployment. Hitodama re-enabled its appearance after that restart and blocked native attach. Its taskbar appearance was paused, with the original rules backed up locally.
- Remaining work: Cold opens pay the dashboard startup cost. API keys entered in the UI expire when the dashboard process exits. The running timer countdown passed the smoke test. Audible expiry while the UI is closed needs a live check. The memory figures reflect a single Windows session and can vary with activity and paging.

### Additional lightweight feature ideas

- Status: Shortlist ready for selection.
- Scope: Suggest additional dock features for the owner to choose from.
- Acceptance criteria: Give each idea a concrete use, build on existing dock behavior, and identify the strongest candidates for an on-demand implementation.
- Outcome: Proposed an activity inbox, running-window search, project resume cards, audio presets, paste transforms, a local project scratchpad, quota reset reminders, and a file action menu. Prioritized the inbox, window search, and presets, with the latter two evaluated only on demand.
- Remaining work: Await feature selection.

### Run alongside Hitodama

- Status: Built, smoke-tested, and running with Hitodama.
- Scope: Make Windows Dock's dashboard, chat grid data, and widgets accessible while Hitodama styles Explorer's taskbar.
- Acceptance criteria: Both applications can run together with one owner of the taskbar appearance. Windows Dock remains accessible on demand and keeps the lean idle lifecycle. Document the entry points and the tradeoff for the in-taskbar chat tiles.
- Outcome: Added `--tray` mode to the native controller. It uses a four-cell status icon in the Windows notification area. Its click and Ctrl+Alt+D open the compact chat panel, and its menu also exposes the full dashboard and an exit command. The mode shares the same monitor and on-demand dashboard.
- Verification: Native Release and self-contained dashboard builds passed. Started tray mode and restored Hitodama's previously paused taskbar appearance. Hitodama reported styling 23 elements. The tray controller did not load the XAML taskbar styler. Windows registered the new icon, and the tray activation and hotkey handlers opened the compact panel. The full panel opened from its controller signal, and the compact panel's Widgets button opened the installed launcher. The dashboard exited after idle and reopened on a fresh process. The controller and monitor used about 25 MiB of private resident memory in the post-close sample.
- Remaining work: Physical tray clicks, hover visuals, and right-click menu selection still need a manual check. Tray mode displays the four states inside one notification-area icon.

#### Clarify how the apps interact

- Status: Answered in chat.
- Scope: Explain how a separate assistant and a separate dock can share the same Windows desktop.
- Acceptance criteria: Distinguish the independently running apps from the shared Explorer taskbar appearance.
- Outcome: Each app runs independently. Their appearance modules can target the same Explorer taskbar. The tray mode provides an independent entry point for Windows Dock when Hitodama styles the taskbar.

### Activity inbox, window search, and audio presets

- Status: Built, smoke-tested, and running in tray mode.
- Scope: Build the three prioritized features from the shortlist.
- Acceptance criteria: Track recent chat completions, approvals, and usage limits with read state. Find and activate open windows through Ctrl+K. Save and recall an output device and per-app volume settings from the audio switchboard.
- Outcome: The monitor records up to 80 recent replies, approvals, and usage limits. The bell in Coding chats opens the inbox with a saved read position. Ctrl+K searches running window titles and `window:` filters to those results. Presets capture the output device, master level and mute state, and levels and mute states for available apps.
- Verification: Inbox fixtures covered persistence across scans and duplicate suppression. The installed build opened the inbox and the live monitor populated it. The smoke harness found a separate process by window title, saved and removed a preset against the real audio endpoint, and verified the disconnected-output path before audio changes. Twelve launch routes and eleven dashboard pages passed.
- Remaining work: Verify a physical switch between separate outputs and the foreground transition when selecting a live window on the desktop. Saved app levels can be applied while the app has an audio session.

### Assistant accounts, personalities, and battery alerts

- Status: Built and running. Local flows verified. Live provider authorization and alert delivery still need interactive checks.
- Scope: Review the attached clip. Investigate paid ChatGPT and Claude account access, support Gemini and Grok fallbacks, add four personalities, and alert at 25, 20, 15, 10, and 5 percent.
- Acceptance criteria: Label subscription and API access accurately, preserve explicit consent before changing providers, and keep battery alerts lightweight and de-duplicated while unplugged.
- Outcome: The attached Atticus demo informed the centered, shortcut-first composer. Ctrl+Alt+A opens the assistant from the controller. Research, Housekeeping, Coding, and Casual chat maintain separate drafts and histories for the dashboard session. ChatGPT plan access uses the browser authorization flow and Windows-protected credentials. Claude API has separate key billing. Gemini and Grok are selectable after usage failures and require an explicit send. The native controller alerts while unplugged at 25%, 20%, 15%, 10%, and 5%.
- Verification: Release native and self-contained dashboard builds passed. Windows opened the installed assistant with its four roles and sign-in button. The harness checked draft separation, loopback state validation, completed and quota-failed response streams, and the five battery thresholds, duplicate suppression, and recharge reset. Hitodama remained styled while tray mode ran at about 27 MiB private resident memory across the controller and monitor.
- Remaining work: Complete a real browser grant and one response on the owner's ChatGPT account. Live Claude, Gemini, Grok, and Groq replies require API keys. Physically verify Windows notification delivery at a battery threshold. The four roles currently provide text assistance. The clip's voice capture, screen context, and learned desktop routines were reference material.

#### Clarify subscription tokens

- Status: Answered in chat. Supported account flow built and ready for sign-in.
- Scope: Confirm whether the assistant can consume existing consumer subscriptions.
- Acceptance criteria: Use the official ChatGPT plan grant and keep Claude Console billing separate from Claude Pro.
- Outcome: Official documentation confirms plan usage through Sign in with ChatGPT for eligible accounts. Anthropic directs third-party apps to Claude Console API keys and does not permit reuse of Claude Pro credentials.
- Remaining work: The owner's browser consent and a live response will verify plan eligibility on this installation.

### Named assistants and role-specific MCP connections

- Status: Built and running in tray mode. Three MCP connections verified live.
- Scope: Give the four dock assistants distinct voices and useful, independently configurable MCP connections.
- Acceptance criteria: Each assistant has its own name, style, and starter prompts. MCP tools are discoverable and callable from the selected assistant only. Show the user which connections are available and keep casual chat tool-free by default.
- Outcome: Vega uses a skeptical research voice and Exa web search. Miso uses a practical organizing voice and privately reads dock reminders and the imported calendar through a local, read-only MCP server. Patch uses a direct debugging voice and can query Microsoft Learn and the optional read-only GitHub PR server. Fizz stays conversational with no connections. Connection settings are per role. Each result can be previewed, attached to a draft, and removed before sending to the selected model.
- Verification: The Windows Release build and x64 publish passed. Live MCP discovery and calls succeeded on Exa, Microsoft Learn, and the local planner. The WPF smoke harness verified no tools for Fizz, separate drafts, the planner preview and attachment path, direct search by persona name, and all twelve launcher routes. The installed tray build opened Patch and its MCP connection selector while Hitodama reported active styling.
- Remaining work: GitHub PR access requires a user-supplied `GITHUB_MCP_TOKEN` and a live PR check. A model response using attached context needs a signed-in plan or configured provider key. This flow uses user-initiated reads and reviewable attachments.

#### Quick personality grid

- Status: Built, verified, and running in tray mode.
- Scope: Add a compact 2×2 assistant picker with distinct character art and a fast route into each role.
- Acceptance criteria: All four assistants are identifiable by name and appearance. Selecting a tile opens that role directly and preserves its draft. The grid works from the existing assistant shortcut and tray mode.
- Outcome: Ctrl+Alt+A, the tray Assistant entry, and the Assistant widget open the centered 2×2 picker. Each character has original pixel art, its own color, and a one-click route into its chat. The picker supports keys 1 through 4, arrow navigation, and Esc.
- Verification: Inspected the Windows picker and chat renders. The installed shortcut opened all four tiles, and selecting Patch opened its chat. The smoke harness checked separate drafts as it switched between characters and reopened the picker.

#### Assistant character refresh

- Status: Built, verified, and running in tray mode.
- Scope: Reimagine the four assistants’ characters, voices, and artwork and refine the quick picker.
- Acceptance criteria: Each character is recognizable at a glance and has a distinct way of talking and starting a conversation. The 2×2 picker remains keyboard-accessible. Existing drafts and connections continue to map to the same roles.
- Outcome: Lark researches like a field reporter, Fern helps make a busy day manageable, Bolt starts by reproducing a failure, and Orbit picks up conversational tangents. The compact picker uses original vector portraits, and each character offers two editable draft starters. The role IDs and MCP mappings stayed the same.
- Verification: The Release build and x64 publish passed. Inspected the Windows render and verified all four names, starter behavior, draft switching, MCP routing, and planner context attachment in the smoke harness. Live MCP discovery and calls passed for Exa, Microsoft Learn, and the local planner. The installed tray opened the refreshed picker and Bolt’s chat while Hitodama remained running.
- Remaining work: The revised prompts still need live response evaluation with a configured provider. This refresh verified prompt setup and local behavior without sending an assistant message.

#### Quick actions and reusable assistant context

- Status: Built, verified, and running in tray mode.
- Scope: Add character-specific actions within reach of the 2×2 picker and make assistant replies easier to reuse.
- Acceptance criteria: Each character offers a useful shortcut that preserves existing drafts. Context is gathered by explicit actions and can be reviewed before any model send. The picker remains compact and usable by keyboard.
- Outcome: Lark previews Exa results from an explicit search. Fern previews today or seven days from the Dock Planner. Bolt can attach a reviewed excerpt of pasted logs and Orbit’s Idea jar adds a starter without overwriting the current draft. Assistant replies expose a copy button. All four shortcuts sit under their character tiles.
- Verification: Release build and x64 publish passed. Windows smoke checks exercised draft preservation, both live planner ranges, stale-preview invalidation, the log size limit and attachment preview, Idea jar insertion, and presence of the copy action on a fixture reply. Exa and Microsoft Learn MCP calls passed. UI Automation opened all four actions in the installed tray app, and Hitodama remained running.
- Remaining work: Live model replies using the new attachments and clipboard output from Copy reply were not exercised in this run.

#### Frontend design skill pass

- Status: Built, verified, and running in tray mode.
- Scope: Load the requested frontend-design skill and use its design guidance for the native assistant interface.
- Acceptance criteria: Read the complete skill instructions and supporting files it requires. Apply a coherent visual direction to the assistant picker while preserving existing shortcuts, accessibility, and draft routing.
- Outcome: The skill command completed successfully and its full output and supporting license were reviewed. The picker uses a compact slate surface and each character has one compartment with two separate targets for chat and its quick action. Type hierarchy comes from the native Segoe UI Variable family, with portraits and role colors reserved for character recognition. The displayed 1 to 4 keys map directly to chat shortcuts.
- Verification: Release build, x64 publish, Windows smoke checks, and visual review of the 366-DIP render passed. The installed tray build advertised all four keyboard shortcuts to Windows UI Automation, exposed all four quick actions as focusable, and opened Orbit’s Idea jar. Hitodama remained running.
- Remaining work: High-contrast theme and alternate monitor scaling were not exercised live in this pass.

#### Black assistant surfaces and frosted taskbar

- Status: Built, verified, and running in tray mode.
- Scope: Rerun frontend-design, review Astryx, and refine the native assistant interface around true black and neutral surfaces. Preserve the taskbar material and current styling mode.
- Acceptance criteria: The picker and assistant panels use a consistent neutral palette and clear keyboard focus. The 2×2 grid and quick actions keep their existing routes. The taskbar retains its frosted appearance.
- Outcome: Reran the requested skill and reviewed Astryx’s dark surfaces and interaction states. The picker uses a single black surface with neutral rules, plain shortcut numbers, and color in the portraits. Assistant chats and tools use the same black surface and neutral inputs, buttons, reply cards, and focus states. No change was made to native acrylic styling or Hitodama settings.
- Verification: Release build, x64 publish, and Windows smoke checks passed. The harness checked opaque black picker and chat surfaces, neutral provider controls, and quick-action behavior. Inspected the picker, chat, and connection screenshots. The installed tray build exposed all four numbered chats and quick actions and opened Lark’s chat. The native taskbar binary hash and Hitodama processes matched before and after installation.
- Remaining work: Alternate display scaling and high-contrast mode were not exercised live.

#### Taskbar launcher and docked panels

- Status: Built, verified, and running in native dock mode.
- Scope: Make the dock accessible directly from the taskbar with a new icon. Replace hover-driven access and overlapping panels with a click-driven flow tied to taskbar visibility.
- Acceptance criteria: The taskbar launcher opens the dock without the hidden-icons menu, the taskbar stays visible while the panel is in use, and panel navigation stays in one visible surface. Preserve the frosted taskbar styling and verify the launch and close behavior on Windows.
- Outcome: The taskbar launcher uses a multiresolution silver-and-amber grid icon. It opens the black Widgets panel with direct access to Coding chats, Assistants, and Dashboard. Navigating into a panel hides the underlying dashboard surface. The taskbar stays visible while the dock is open and auto-hide resumes on close. Companion mode can open the launcher via `WindowsDock.exe --open` for a standard Windows taskbar shortcut.
- Verification: The native Release build, x64 publish, and Windows widget smoke checks passed. In the installed native mode, UI Automation found the launcher and live taskbar grid, activated both, and verified that Widgets sits six pixels above the measured taskbar. Auto-hide changed from on to off while Widgets was open and returned to on after close. Hitodama's taskbar appearance was paused so the native dock could own the glass styling. Its app remained running, and the previous appearance rules were backed up locally.
- Remaining work: A permanent pin in companion mode is a separate Windows shell action. It was not used for the installed native widget.

##### Double-height taskbar and live grid

- Status: Live grid built and verified. Double-height resize remains open.
- Scope: Double the current taskbar height and place the live 2×2 chat widget on the main taskbar.
- Acceptance criteria: The frosted bar and its reserved desktop space grow together. The grid displays live states and opens chats on click. Existing shell controls remain usable.
- Outcome: The wider 2×2 status grid lives in Explorer's main taskbar beside the new launcher. Its four faces still follow live chat states and accept direct clicks. The taskbar uses the native acrylic material.
- Verification: Windows exposed the live state of the first slot and opened Coding chats from a grid click. The tested Windows build held both the shell window and reserved taskbar height at 48 pixels after the resize probes.
- Remaining work: Validate an Explorer sizing path that moves the shell window, XAML layout, and reserved desktop area together before shipping 96 pixels. Verify app overflow, shell flyouts, scaling, and auto-hide with the taller geometry.

#### Gesture navigation

- Status: Built and installed in native mode. Gesture thresholds, horizontal wheel routing, and click navigation verified.
- Scope: Use Hitodama's compact, taskbar-first interaction as a reference for gesture navigation.
- Acceptance criteria: Gestures start on a clear, dedicated target and mirror the same actions available by click or keyboard. Scrolling, text selection, and normal Windows taskbar input retain their existing behavior. Navigation stays in one visible panel.
- Outcome: The native taskbar icon accepts an upward pull to open Widgets. Widgets, Coding chats, and Assistants share a bottom section switcher. The handle accepts mouse and touch swipes and horizontal wheel input for section changes. Dragging down dismisses the panel. In panels with a Back button, dragging right follows the same route. Section changes use a directional transition and respect the Windows animation preference.
- Verification: Release, x64 publish, Windows smoke, and screenshot review passed. The gesture fixture checked axis thresholds, small and diagonal rejection, horizontal wheel routing and accumulation on the handle, clicks between sections, and all twelve launcher routes. The native module loaded in Explorer and the installed launcher started from its open command.
- Remaining work: Live pointer and hardware touch tests could not be completed. Computer Use received access denied from GetCursorPos and the available capture showed the display idle screen. Verify an upward pull on the Explorer button and physical touch and touchpad input on an unlocked desktop.

##### Dashboard-first trackpad navigation

- Status: Built, verified with Windows wheel messages, and running in native mode.
- Scope: Make Dashboard the dock's default entry. An upward scroll opens Widgets, and a sideways swipe moves between Dashboard tabs.
- Acceptance criteria: Taskbar activation opens Dashboard, trackpad gestures work across its surface, and a single swipe moves only one tab. Scrollable content can still scroll to its top, and coming back from Widgets preserves the selected tab.
- Outcome: The taskbar launcher and `--open` open Dashboard. A fresh upward scroll over its header or content opens Widgets when the content under the pointer has reached the top. Horizontal navigation follows the visible tab order and consumes the momentum of each gesture. Returning from Widgets or compact coding chats restores the last Dashboard tab.
- Verification: Native Release, x64 publish, and Windows smoke checks passed. The fixture tested partial wheel deltas, momentum, tab boundaries, nested scrolling, and tab preservation. The installed build opened Dashboard, switched to Audio with a horizontal wheel message, opened Widgets with an upward wheel message, and returned to Audio from the Explorer taskbar button. Explorer was restarted after the old shell session refused to reload the native integration.
- Remaining work: Physical touchpad input was not exercised. Windows wheel messages verified the input routing.

#### Native dock visual refresh and animated horizontal slots

- Status: Built, verified, and running in native mode.
- Scope: Apply uncodixify to the native dashboard, widgets, and overlay panels. Arrange the live coding slots and assistant picker in one row of four.
- Acceptance criteria: Preserve the taskbar height and existing panel widths. The four slots are interactive and animate to communicate state or focus. Dashboard navigation, forms, and popovers share styling and keep their existing routes.
- Outcome: Dashboard and popovers share black surfaces, neutral controls, and simpler row separators. The assistant picker keeps its 366 DIP width and shows the characters side by side. The native coding strip stays on the taskbar and uses state-specific motion. Both surfaces respect the Windows animation setting.
- Verification: Release builds, Windows smoke tests, and Windows renders passed. Verified all dashboard pages at 1060 and 640 DIP, four assistant tiles in one row, and the existing click and wheel navigation. An installed Explorer slot opened Coding chats.
- Remaining work: None.

##### Taskbar contrast and agent spacing

- Status: Built and running in native mode. Installed geometry and routing verified.
- Scope: Address the light-background screenshot and use the allowed extra width for the four live agent slots.
- Acceptance criteria: The frosted taskbar remains legible over light content. The native faces are readable and separated, and the shell height stays fixed. The assistant picker uses the existing width.
- Outcome: Added a neutral shade over the acrylic to limit its brightness and brightened the date. Removed the heavy fill from the agent strip. Each slot now has a 22 DIP column and a larger face.
- Verification: Installed native build loaded in Explorer. At 96 DPI, all four click targets measured 20 by 36 pixels, spaced 22 pixels apart. The taskbar stayed 48 pixels tall.
- Remaining work: The new native glass was not visually rechecked over a white browser page.

##### Remember summary choices

- Status: Built, verified, and installed.
- Scope: Remember the last selected provider and each provider's model across reply and notification summary popups and dock restarts.
- Acceptance criteria: Both popups open ready to use with the saved selection. Switching providers restores its own model, including a CLI default. Opening a new popup does not require navigating to another settings screen.
- Outcome: The two summary popups share locally saved provider and per-provider model preferences. Changing provider or leaving the model field saves the choice without a summary request. Clearing a model restores that provider's default. The installed profile was initialized to Codex with the CLI default from the screenshot.
- Verification: Windows smoke checks passed for switching providers, reopening both summary types, invalid saved models, clearing a custom model, and restored preferences in a fresh process. A fixture confirmed the key stays out of the preference file. An installed Reply summary opened directly with Codex and the CLI default.
- Remaining work: None.

#### Pinned widgets and dock assistant actions

- Status: Built, verified, and installed.
- Scope: Put one live widget above four favorite shortcuts and the main widget list. Let dock assistants carry out app actions and use their relevant MCP connections in chat. Add a Windows sign-in option.
- Acceptance criteria: The pinned widget stays usable inside Widgets. Up to four favorites remain easy to reach, and selections survive a restart. Every dock persona can use the shared actions for volume, reminders, timers, Wi-Fi, Bluetooth, and startup. Role-specific MCP tools are available through the conversation.
- Outcome: Widgets opens with one live panel, four optional favorites, and the remaining catalog. A favorite and the existing pin trade places when the favorite is pinned. All personas can propose supported dock actions and read from their enabled MCP connections. Installed external coding clients have the same dock controls through the local MCP server.
- Verification: Windows fixtures checked pinned controls, the four favorites, persistence, and the shared reminder and timer stores. The installed MCP server exposed three tools, read live state, and rejected an out-of-range volume. Codex and Claude Code both have the installed stdio connection.
- Remaining work: Actual Wi-Fi and Bluetooth radio transitions were not exercised during deployment.

##### Background assistant jobs and Orbit handoffs

- Status: Built, verified, and installed.
- Scope: Let Orbit delegate tasks to specialists. Keep accepted jobs running when the conversation closes and surface their status, results, and cancellation in the dock. Add a writing specialist and a workspace-scoped coding path.
- Acceptance criteria: Orbit can propose the right handoff based on the task. Confirming it starts a background job that can be tracked without leaving another app. Writing produces usable drafts. Coding targets an explicitly selected workspace. Jobs report completed, failed, canceled, or interrupted states.
- Outcome: Orbit routes to Lark, Scribe, Fern, or Bolt. Confirm a handoff to start it. Jobs shows progress, cancellation, and saved results. The dock stays running while jobs are active. Bolt can edit project text through a workspace-scoped tool when the Windows shell runner is unavailable.
- Verification: A fixture ran a writing handoff to completion after the dock window hid and verified the saved result. The live coding fixture wrote and checked a file in an isolated Git project. The Windows shell runner returned a package-identity error and the scoped text editor completed the edit.
- Remaining work: Shell-based coding checks remain unverified on this Windows installation.

##### Launch the dock

- Status: Verified.
- Scope: Open the installed Windows Dock while the new widget and assistant work continues.
- Acceptance criteria: The launch command opens Dashboard through the installed controller.
- Outcome: Launched the previous installed build during development and reopened Dashboard after installing the native update.
- Verification: Windows reported the visible Dashboard window and the final native controller signaled Dashboard open.
- Remaining work: None.

##### Audio routing, fixed windows, and saved setup

- Status: Built and installed. Windows routing and shell layout verified.
- Scope: Repair browser and multi-device routing, keep panel dimensions stable, retain provider credentials and weather preferences, add in-app radio controls, and restore native mode at sign-in with the visible four-slot widget.
- Acceptance criteria: Brave routing targets the correct Windows audio processes and shows whether the requested route is active or pending. Data changes scroll inside stable windows. Local provider setup and weather location and units survive restart. Wi-Fi and Bluetooth have direct controls and native startup keeps the launcher and 1×4 row visible.
- Outcome: Mixer routing follows related browser processes and verifies the saved device separately from active playback. Windows account encryption stores provider keys. Weather saves the chosen location even if the network fetch fails and renders °F or °C from one forecast. Controls toggles radios. The installed controller uses native mode and the Windows sign-in entry points to that mode. Hitodama's taskbar appearance was paused so the two apps can coexist.
- Verification: Windows confirmed an isolated app route to the connected Bluetooth Echo Spot and restored it afterward. Smoke checks verified fixed dashboard size, the saved unit preference, and provider and model persistence. Explorer exposed the visible dock launcher and four live slots.
- Remaining work: Live Brave playback on Bluetooth and separate simultaneous streams across devices were not heard or verified. The sign-in entry was inspected during the existing Windows session.

##### Agent completion glow

- Status: Built, verified, and installed.
- Scope: Give completed coding turns and finished dock jobs a brief rim animation on the native taskbar and any open dock window.
- Acceptance criteria: A new completion triggers a single cue. Old completions do not continually replay. Animations respect Windows accessibility preferences and do not keep a render loop active.
- Outcome: A new finished turn or background job signals a single brief mint rim pulse. The cue respects Windows animations and high contrast.
- Verification: The managed fixture measured animation and restoration and confirmed it does not replay. The installed Explorer module consumed the native completion event.
- Remaining work: No pixel capture of the native pulse was recorded.

##### Compact dock and richer graphics

- Status: Built, rendered, and installed.
- Scope: Reduce excess space in fixed-size windows and improve the visual hierarchy, especially Dashboard Overview, Widgets, and the shared panel controls.
- Acceptance criteria: Windows stay stable while navigating, with content scrollable when needed. The dashboard uses artwork, weather graphics, and status visuals at useful sizes. Controls remain accessible and the black surfaces and frosted taskbar remain consistent.
- Outcome: Overview uses a compact fixed window, larger media artwork, an illustrated forecast, a calendar tile, and CPU and RAM gauges. Widgets and other popups have fixed per-panel heights.
- Verification: Windows rendered the dashboard and panels. Layout and scrolling fixtures passed in the final managed build.
- Remaining work: None.

##### Taller taskbar glass

- Status: Built and installed.
- Scope: Increase the visible native taskbar surface height by approximately 15 percent.
- Acceptance criteria: Grow the 42-pixel glass surface to the full 48-pixel taskbar at 96 DPI. Preserve Explorer's reserved desktop area, launcher access, and the 1×4 layout.
- Outcome: Removed the six combined pixels of vertical margin around the native glass at 96 DPI. The surface now fills the existing 48-pixel shell height.
- Verification: The updated DLL attached to Explorer. Windows reported a 48-pixel reserved taskbar and exposed the launcher and four equal-height buttons in a single row.
- Remaining work: None.

##### One-click summaries and quieter scrollbars

- Status: Built, verified, and installed.
- Scope: Open a reply summary and generate it with the remembered model on the first click. Replace the wide scrollbar chrome with a slim indicator throughout the dock.
- Acceptance criteria: Summarize reply immediately runs with a configured provider. Source and provider settings remain reachable, and missing reply text or credentials have a recovery path. Wheel, touch, keyboard, and dragging still scroll.
- Outcome: Summarize reply runs with the remembered provider and model. Source editing and provider settings are available in the optional details. Outer panel tracks are hidden and inner scroll indicators are slim.
- Verification: Windows fixtures verified saved provider and model selection and the recovery path for a missing reply. A synthetic reply generated a live summary with the saved Codex setting from the initial click. The Coding chats render showed the outer track hidden.
- Remaining work: None.

##### Coding chats controls redesign

- Status: Built and installed.
- Scope: Reorganize the Coding chats panel and surface relevant controls within the dock.
- Acceptance criteria: The selected chat and its next action are easy to find. Users can switch chats, open summaries, assign slots, review usage and approvals, and reach jobs and provider defaults from the panel.
- Outcome: The selected conversation and summary action lead the panel. Open chat, assignment, and copy are next to them. Jobs, Usage, and Setup sit in the header. All, Codex, and Claude filter the list below. Setup shows the selected provider's hook and default controls first.
- Verification: Windows rendered the redesigned Coding chats panel and verified existing chat navigation. Invoking a native slot opened the installed 506 by 454 pixel panel with Jobs and Setup visible.
- Remaining work: None.

##### Single-window Coding chats workspace

- Status: Implemented, verified, and installed in native taskbar mode.
- Scope: Replace the stacked Coding chats layout with a conversation rail and a work pane. Keep reply summaries and related controls inside the Coding chats window.
- Acceptance criteria: The native taskbar slots and the Dashboard Chats tab open the new layout. Selecting a conversation keeps its summary, approvals, slot assignment, usage, provider settings, jobs, and activity accessible from the same window. One click uses the saved summary provider and model. Navigation keeps the window size and selected conversation stable.
- Implementation: Added a searchable conversation rail, inline original reply and summary views, and same-window controls for approvals, slots, usage, provider setup, jobs, and activity. The compact view uses a width of 764 DIP. At narrow widths, a conversation selector replaces the rail. Both panes scroll independently.
- Verification: Release build and widget smoke tests passed, including narrow layout, inline navigation, and saved provider settings. A live Codex request summarized a synthetic reply and retained the result across slot and usage navigation. The installed native taskbar opened the workspace from its four-slot grid and kept the same window handle and dimensions when opening Agent slot.
- Limitations: Summaries use completed replies available in local provider history. Provider setup and approval controls use the provider's existing integrations.
- Remaining work: None for this request.

##### Coding providers for summaries

- Status: Built and installed. Codex verified live. Claude Code sign-in required on this machine.
- Scope: Add signed-in Codex and Claude Code as selectable sources for coding reply summaries and notification digests.
- Acceptance criteria: Both choices work through the installed local CLIs, use the editable preview, and require no separate API key in the dock. A summary call runs without project access or tool use. Existing providers remain available.
- Outcome: Reply and notification summaries share a provider picker with Gemini, Groq, Codex, and Claude Code. The CLI routes use saved sign-in, accept an optional model override, and read the reviewed text from standard input. They run outside project folders with model tools and session persistence disabled. API providers retain their existing key fields.
- Verification: Release build, x64 publish, and Windows smoke checks passed. Both preview panels preserved edits across provider changes. A synthetic live request succeeded through Codex, and the installed Claude Code CLI reported that it needs sign-in. The installed notification flow exposed both new options, and the native taskbar controller stayed running during the managed update.
- Remaining work: Sign in to the installed Claude Code CLI and verify a live summary with that account.

## Uncodixify frontend skill

- Status: Installed and structurally validated.
- Scope: Turn the supplied frontend design guidance into a personal Codex skill.
- Acceptance criteria: Install a discoverable skill that preserves the component rules, recurring pitfalls, and supplied palettes and passes skill validation.
- Implementation: Installed the personal uncodixify skill. Consolidated duplicate rules and removed stray diff markers while retaining the component guidance and four supplied palettes.
- Verification: The bundled skill validator passed, and all four palette rows match the source exactly.
- Limitations: Automatic selection and generated frontend output have yet to be tested in a fresh Codex session.
- Remaining work: None for installation.
