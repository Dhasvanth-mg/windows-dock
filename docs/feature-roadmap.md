# Feature roadmap

Reviewed November 19, 2026. These are proposed increments for the existing Windows Dock. The current request covers verification and deployment of coding controls and standalone Project canvases.

## Suggested order

| Priority | Improvement | What becomes easier | Completion check |
| --- | --- | --- | --- |
| 1 | Reliable coding conversation controls | See whether a chat is connected, send a follow-up, steer or stop a turn, and set its model and thinking from one pane. | The dock confirms the selected chat and turn before each action. A real shared session receives the message, shows the response and honours Stop. Private desktop sessions clearly offer their saved copy. |
| 2 | Canvas selection and editing | Shift-select or drag a selection box, move several items, duplicate them, align them and use redo. | Connectors follow a moved selection. Copying a group remaps its internal connectors. Undo and redo restore one complete user action, including after an autosave. |
| 3 | Frames and a useful canvas overview | Put a project into named sections, lock a finished section and jump between sections from a minimap. | Frames move their contents together. Locked items resist accidental editing. Fit selection and the minimap work with rotated items and negative coordinates. |
| 4 | Canvas files and recovery | Export a board as an image or PDF, save an editable board backup and recover a recent version. | Exports contain the whole board at the chosen scale. An imported backup restores pictures and connectors. A failed import leaves the existing board usable. |
| 5 | Agent work attached to a board | Turn selected notes into an editable task brief and keep the result beside the original ideas. | The draft lists the selected content. The resulting job links back to the board and can add a proposed note or checklist after review. |
| 6 | A useful project home | Link a board, repository, saved window layout, reminders, contacts and voice memos. | Each link opens the original item. A standalone canvas remains usable independently. Removing a project link preserves its underlying item. |
| 7 | Jobs that recover | Reopen a specialist job with its completed steps, output files and next action after the dock exits. | An interrupted job shows its actual last step. Resuming distinguishes already applied file changes from pending actions. Cancellation releases its worker. |
| 8 | A more capable daily planner | Connect calendars, edit recurring events, attach meeting notes and turn a voice memo into a draft reminder. | Changes round-trip with the connected calendar, handle time zones and conflicts, and retain provider errors for retry. |

## Coding controls

Start with connection discovery and clear per-chat capabilities. The current Codex connection accepts a local shared app server. A desktop client's private connection needs a provider-supported attachment route before live control can reach that exact session. A separate saved copy can continue its history with the selected CLI account.

Then keep one connection alive while its Coding chats pane is open. Show progressive replies, tool progress and pending decisions as events arrive. Reconnect by reading current state before enabling another send. Retain the draft on timeout and distinguish delivery confirmation from a completed reply. Use the server's available models and supported thinking levels for the picker.

Exercise approvals with a real provider hook. The pane should show the requested command or edit, its target and the current decision state. A completed or expired request must stop accepting decisions. Surface account and quota failures next to the composer, with the existing saved provider choices available.

## Canvas editing

Keep the existing standalone board shelf, tool rail, local autosave and fullscreen editor. Add selection and a small contextual toolbar first. Grouping, alignment and redo should share one history model so a group move, connector adjustment and duplicate each remain a single undo step.

Next add frames, locking, zoom-to-selection and a minimap that renders from saved geometry. Add export and version recovery after those operations have stable geometry and undo behaviour. Board images, imported items, tables and drawings need the same copy and export paths.

Useful follow-ups are searchable board text, bookmarks to a region, reusable personal templates, checklist items and a presentation mode that moves through named frames. Agent-created notes should show their source conversation or job.

## More ideas worth considering

| Idea | Useful interaction |
| --- | --- |
| Recent work strip | Reopen a recent board, coding chat or project with one click. |
| Meeting scratchpad | Start a note and timer from an event, then link the note or recorded memo to that event. |
| Audio scenes | Apply saved app volumes and output routes for coding, calls or music, with the current device shown for each app. |
| Quick capture | Save a selected screenshot, idea or link directly onto a chosen board. |
| Project focus | Open a saved layout, start a timer and show the relevant chats and reminders. |
| Personal templates | Turn an existing board selection into a reusable planning or design template. |
| Usage forecast | Combine recorded provider usage with reported reset times and show which assistant is available. |

## Delivery constraints

Use the existing native controller, dashboard and local storage. Keep work such as indexing, transcription and model calls on demand. The file library continues to search explicit dock-owned uploads. Measure hidden memory, first open and board manipulation before and after each substantial change.

The current test suite and fixtures cover local behaviour. Shared-session controls, account authorization, audible routing and hardware gestures also need their corresponding live checks. Each increment belongs in the feature tracker with its own built, verified and remaining-work notes.
