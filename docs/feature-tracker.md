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
