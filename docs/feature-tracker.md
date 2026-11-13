# Feature tracker

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

- Status: In progress.
- Scope: Investigate per-chat model and thinking controls and add a dock popup for summarizing recent assistant replies.
- Acceptance criteria: Verify any supported active-chat controls, and keep summary requests explicit and scoped to the selected chat.
- Remaining work: Implement and test reply loading, summarization, and the in-dock display. Verify which live chat controls the installed clients expose.

## Coding chat token usage and resets

- Status: In progress.
- Scope: Add token usage and provider rate-limit reset times to the Coding chats dashboard.
- Acceptance criteria: Show reported counts and reset times where available, label their scope and freshness, and verify behavior when a provider does not report them.
- Remaining work: Identify what Codex and Claude Code expose to the dock, then implement and verify the display.

## Coucou-inspired coding agents

- Status: In progress.
- Scope: Review Coucou and redesign the Coding chats interaction around animated status, hover details, and slide-up actions.
- Acceptance criteria: Recognizable states for working, approval required, finished, and quota reached. A mouse or keyboard user can reach chat actions, reply summaries, and usage without leaving the dock.
- Remaining work: Verify the reference, implement the dock interaction and state visuals, and test on Windows.

### Minimal dock interface

- Status: In progress.
- Scope: Bring the new coding-agent experience into the existing native dock, drawing on Coucou's compact interaction and Hitodama's restrained styling.
- Acceptance criteria: A small 2×2 grid on the native taskbar displays up to four chat states. Hover identifies the chat and its current status. Click opens the chat's actions in a compact panel from the dock.
- Remaining work: Build and connect the native grid, then verify the live states, hover, clicks, and layout on Windows.

### Dock and tray refinement

- Status: In progress.
- Scope: Refine the dock surface, tray, date, clock, and calendar presentation while keeping Windows controls usable and background work small.
- Acceptance criteria: Shared spacing and styling across the native dock and flyouts, with the clock and tray controls still using Windows interactions. Keep refresh and animation work scoped to what is visible or changing.
- Remaining work: Build and test the native and managed layout together.

### Grid previews, navigation, and assignments

- Status: In progress.
- Scope: Show a quick preview when hovering over a tile. Open an all-chat menu from the grid, surface pending approvals and fresh replies, and provide a direct open action and saved chat or project assignments.
- Acceptance criteria: Hover shows the tile's chat and status, click keeps all chats accessible and highlights an urgent or recently finished chat, and slot assignments survive a restart.
- Remaining work: Implement the preview and routing, saved assignments, and Windows verification.

### Automatic weather location and styled controls

- Status: In progress.
- Scope: Add weather location detection and make the dashboard's controls visually consistent with the native dock.
- Acceptance criteria: Weather can use the Windows location permission flow with city search still available. Custom inputs, selectors, buttons, and scrolling remain readable and usable by keyboard.
- Remaining work: Implement the location flow and control styles, then verify both paths and the resulting Windows layout.

### Widget ideas

- Status: Shortlist delivered. The owner selected all eight and asked for more ideas.
- Scope: Suggest additional widgets that fit the dock.
- Acceptance criteria: Provide a short list of useful, distinct widgets with the action each would make easier.
- Outcome: Shared audio switching, a meeting card, a clipboard and file shelf, PR and build checks, workspace shortcuts, a download queue, a focus switch, and a command palette. The request is for ideas to review before choosing features to build.

### Selected widgets and notification summaries

- Status: In progress.
- Scope: Add the audio switchboard, meeting card, clipboard and file shelf, GitHub checks and reviews, workspace shortcuts, download queue, focus controls, command palette, and notification summaries. Suggest more widgets separately.
- Acceptance criteria: Every enabled widget has a usable dock entry, operates on real local or connected data, and identifies any Windows or service limitations. Notification text leaves the device only when the user sends it for summarization.
- Remaining work: Complete the dock and coding-agent build, extend the existing widgets, add the new widgets, and test the supported Windows and account flows.
