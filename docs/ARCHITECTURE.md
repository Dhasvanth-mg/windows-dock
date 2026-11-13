# Native dock architecture

The native dock uses a small x64 controller and a C++/WinRT XAML diagnostics site. The controller connects to the Explorer shell on the tested build. The site checks for the taskbar and tray frames under the expected parent and changes their layout and paint properties on Explorer's XAML dispatcher. It inserts one non-interactive glass background and one dashboard button. The native frames and their buttons keep their original input handlers and flyout anchors. No other shell surfaces are styled.

The controller and dashboard use session-local events. The dashboard only opens when requested and owns its media, calendar, weather, file and settings UI outside Explorer. A named stop event or controller process exit triggers restoration of the captured XAML values and column definitions. The diagnostic DLL can remain loaded until Explorer exits. No taskbar registry values or startup settings are required by the project.

Windows exposes XAML Diagnostics as a developer API. Explorer's XAML tree is an implementation detail, so this version is limited to build 26200 and guarded by runtime layout checks. Secondary taskbars, vertical taskbars, Explorer restarts, and future cumulative updates need independent verification.

References [Microsoft XAML Diagnostics](https://learn.microsoft.com/windows/win32/api/_xaml_diagnostics/), [DG-Dock](https://github.com/Dippy1234/DG-Dock), and [Docktor](https://docktorapp.com/). The supplied clips and installed Hitodama taskbar were visual and behavioral references.

# Legacy companion architecture

The companion dock uses WPF on .NET 10 for transparent desktop rendering, keyboard-accessible controls, and direct Win32 interop. WinUI 3 is a candidate for a separate Studio process after the configuration and IPC contracts stabilize. DirectComposition can be evaluated if rendering measurements warrant it.

ShellIntegration owns window discovery and activation. The dock and settings window share the same configuration, persisted as JSON under LocalAppData. Companion mode uses monitor work areas and leaves Explorer in charge of the taskbar and third-party notification icons.

| Feature | Feasibility | Approach |
| --- | --- | --- |
| Window tracking | Supported | EnumWindows and out-of-context WinEvent hooks. Filter tool windows and DWM-cloaked windows. |
| Window activation | Supported with restrictions | ShowWindowAsync and SetForegroundWindow. Windows controls activation rights. |
| Screen-space reservation | Supported | SHAppBarMessage registers a dock-owned AppBar. |
| Monitor placement | Supported | Monitor work areas and Per-Monitor V2 DPI awareness. Mixed-DPI behavior needs live verification. |
| Virtual desktops | Partial | IVirtualDesktopManager exposes window membership. Broader desktop management needs compatibility work. |
| Window previews | Supported with constraints | Evaluate DWM thumbnails. |
| Fullscreen detection | Compatibility work | Compare foreground bounds against monitor bounds and test maximized and borderless fullscreen windows separately. |
| Third-party tray icons | No documented general replication API | Shell_NotifyIcon manages application-owned icons. Preserve access through Explorer. |
| System flyouts | Partial | Open supported settings entry points and leave placement to Windows. |
| Taskbar progress and badges | Partial | Windows lets applications publish their own state. A general reader needs compatibility research. |
| Taskbar replacement | Compatibility work | Gate on tested restoration, essential controls, tray access, and Explorer restart behavior. |

Sources include [WinEvent hooks](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwineventhook), [AppBars](https://learn.microsoft.com/windows/win32/shell/application-desktop-toolbars), [DWM](https://learn.microsoft.com/windows/win32/dwm/functions), and the [Windows notification area](https://learn.microsoft.com/windows/win32/shell/notification-area). [SideDock](https://github.com/IACBI/SideDock) illustrates WPF and Win32 AppBar integration in a standalone dock.
