#include "Connection.h"
#include "DockTray.h"

#include <algorithm>
#include <cstdio>
#include <cwchar>
#include <filesystem>
#include <memory>
#include <iterator>
#include <shellapi.h>
#include <thread>
#include <winrt/base.h>
#include <xamlom.h>

namespace
{
    HANDLE activeStop = nullptr;

    void ReportFailure(const wchar_t* message)
    {
        std::fwprintf(stderr, L"%ls\n", message);
        if (GetConsoleWindow() && IsWindowVisible(GetConsoleWindow()))
            MessageBoxW(nullptr, message, L"Windows Dock", MB_OK | MB_ICONINFORMATION);
    }

    struct Session
    {
        explicit Session(DWORD pid)
            : ready(CreateEventW(nullptr, TRUE, FALSE, SessionName(pid, L"Ready").c_str())),
              restored(CreateEventW(nullptr, TRUE, FALSE, SessionName(pid, L"Restored").c_str())),
              stop(CreateEventW(nullptr, TRUE, FALSE, SessionName(pid, L"Stop").c_str())),
              dashboard(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"Dashboard").c_str())),
              showDashboard(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"ShowDashboard").c_str())),
              agent(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"Agent").c_str())),
              showAgent(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"ShowAgent").c_str())),
              assistant(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"Assistant").c_str())),
              showAssistant(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"ShowAssistant").c_str())),
              openLauncher(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"OpenLauncher").c_str())),
              showLauncher(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"ShowLauncher").c_str())),
              panelVisible(CreateEventW(nullptr, TRUE, FALSE, SessionName(pid, L"PanelVisible").c_str())),
              visibilityChanged(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"PanelVisibilityChanged").c_str())),
              completion(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"AgentCompletion").c_str())),
              agentGrid(CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(AgentGridState),
                  SessionName(pid, L"AgentGrid").c_str()))
        {
            if (!ready || !restored || !stop || !dashboard || !showDashboard || !agent || !showAgent ||
                !assistant || !showAssistant || !openLauncher || !showLauncher || !panelVisible || !visibilityChanged || !completion || !agentGrid)
                winrt::throw_last_error();
        }

        winrt::handle ready, restored, stop, dashboard, showDashboard, agent, showAgent, assistant, showAssistant, agentGrid;
        winrt::handle openLauncher, showLauncher, panelVisible, visibilityChanged, completion;
    };

    class TaskbarVisibility
    {
    public:
        ~TaskbarVisibility() { Hold(false); }

        void Hold(bool visible)
        {
            APPBARDATA bar{sizeof(APPBARDATA)};
            bar.hWnd = FindWindowW(L"Shell_TrayWnd", nullptr);
            if (!bar.hWnd)
                return;
            const auto state = SHAppBarMessage(ABM_GETSTATE, &bar);
            if (visible && !held && (state & ABS_AUTOHIDE))
            {
                bar.lParam = state & ~ABS_AUTOHIDE;
                SHAppBarMessage(ABM_SETSTATE, &bar);
                held = !(SHAppBarMessage(ABM_GETSTATE, &bar) & ABS_AUTOHIDE);
            }
            else if (!visible && held)
            {
                held = false;
                if (!(state & ABS_AUTOHIDE))
                {
                    bar.lParam = state | ABS_AUTOHIDE;
                    SHAppBarMessage(ABM_SETSTATE, &bar);
                }
            }
        }

    private:
        bool held{};
    };

    int Run(int argc, wchar_t** argv)
    {
        if (argc == 3 && std::wstring_view(argv[1]) == L"--restore")
        {
            wchar_t* end = nullptr;
            const auto pid = std::wcstoul(argv[2], &end, 10);
            if (!pid || *end != L'\0')
                return 1;
            winrt::handle stop(OpenEventW(EVENT_MODIFY_STATE, FALSE, SessionName(pid, L"Stop").c_str()));
            if (!stop || !SetEvent(stop.get()))
            {
                std::fputws(L"No active native dock session with that process ID.\n", stderr);
                return 1;
            }
            return 0;
        }

        const bool openOnStart = argc == 3 && std::wstring_view(argv[2]) == L"--open";
        const bool trayMode = (argc == 2 || openOnStart) && std::wstring_view(argv[1]) == L"--tray";
        if (!trayMode && argc != 1)
        {
            std::fputws(L"Usage: NativeTaskbar.exe [--tray [--open] | --restore <controller PID>]\n", stderr);
            return 1;
        }

        OSVERSIONINFOW version{};
        version.dwOSVersionInfoSize = sizeof(version);
        const auto ntdll = GetModuleHandleW(L"ntdll.dll");
        const auto getVersion = reinterpret_cast<LONG (WINAPI*)(OSVERSIONINFOW*)>(
            GetProcAddress(ntdll, "RtlGetVersion"));
        if (!trayMode && (!getVersion || getVersion(&version) != 0 || version.dwBuildNumber != TestedWindowsBuild))
        {
            ReportFailure(L"This native dock build supports Windows 11 x64 25H2 (build 26200).");
            return 1;
        }

        winrt::handle owner(CreateMutexW(nullptr, TRUE, L"Local\\WindowsDock.NativeTaskbar.Controller"));
        if (!owner || GetLastError() == ERROR_ALREADY_EXISTS)
        {
            std::fputws(L"A native dock session is already running.\n", stderr);
            return 1;
        }

        DWORD explorerPid = 0;
        GetWindowThreadProcessId(GetShellWindow(), &explorerPid);
        winrt::handle explorer(OpenProcess(SYNCHRONIZE, FALSE, explorerPid));
        if (!explorerPid || !explorer)
        {
            std::fputws(L"Explorer's desktop window was not found.\n", stderr);
            return 1;
        }

        wchar_t path[MAX_PATH];
        const auto pathLength = GetModuleFileNameW(nullptr, path, ARRAYSIZE(path));
        if (!pathLength || pathLength == ARRAYSIZE(path))
            return 1;
        const auto directory = std::filesystem::path(path).parent_path();
        const auto module = directory / NATIVE_TASKBAR_DLL;
        if (!trayMode && !std::filesystem::is_regular_file(module))
        {
            std::fwprintf(stderr, L"%ls must be beside this executable.\n", NATIVE_TASKBAR_DLL);
            return 1;
        }
        const auto panel = directory / L"WindowsDock.exe";
        if (!std::filesystem::is_regular_file(panel))
        {
            std::fputws(L"WindowsDock.exe must be beside this executable.\n", stderr);
            return 1;
        }

        const auto controllerPid = GetCurrentProcessId();
        const Session session(controllerPid);
        activeStop = session.stop.get();
        SetConsoleCtrlHandler([](DWORD event) -> BOOL
        {
            if (event == CTRL_C_EVENT || event == CTRL_BREAK_EVENT || event == CTRL_CLOSE_EVENT)
            {
                SetEvent(activeStop);
                return TRUE;
            }
            return FALSE;
        }, TRUE);

        std::unique_ptr<AgentGridState, UnmapAgentGrid> grid(trayMode
            ? static_cast<AgentGridState*>(MapViewOfFile(session.agentGrid.get(), FILE_MAP_WRITE, 0, 0, sizeof(AgentGridState)))
            : nullptr);
        if (trayMode && !grid)
            winrt::throw_last_error();
        DockTray tray(trayMode, session.stop.get(), session.dashboard.get(), session.agent.get(), session.assistant.get());
        if (!tray.HasEntryPoint())
            return 1;
        tray.Watch(grid.get());
        const auto initialization = std::to_wstring(controllerPid);

        if (trayMode)
            std::wprintf(L"Windows Dock tray mode active. Controller PID: %lu.\n", controllerPid);
        else
        {
            const auto xaml = LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
            if (!xaml)
                return 1;
            const auto attach = reinterpret_cast<decltype(&InitializeXamlDiagnosticsEx)>(
                GetProcAddress(xaml, "InitializeXamlDiagnosticsEx"));
            if (!attach)
                return 1;

            bool attached = false;
            HRESULT lastError = E_FAIL;
            for (int index = 1; index <= 16 && !attached; ++index)
            {
                const auto endpoint = L"VisualDiagConnection" + std::to_wstring(index);
                std::thread attempt([&]
                {
                    lastError = attach(endpoint.c_str(), explorerPid, nullptr, module.c_str(),
                        TaskbarSiteId, initialization.c_str());
                });
                attempt.join();
                attached = SUCCEEDED(lastError);
            }

            const HANDLE readySignals[] = {session.ready.get(), session.restored.get(), explorer.get()};
            if (!attached || WaitForMultipleObjects(ARRAYSIZE(readySignals), readySignals, FALSE, 4000) != WAIT_OBJECT_0)
            {
                SetEvent(session.stop.get());
                wchar_t message[200];
                swprintf_s(message, L"Native dock could not style the current taskbar (0x%08lX). "
                    L"Turn off other taskbar appearance tools and try again.", static_cast<unsigned long>(lastError));
                ReportFailure(message);
                return 1;
            }

            std::wprintf(L"Native dock active in Explorer %lu. Controller PID: %lu.\n", explorerPid, controllerPid);
        }
        tray.Explain();
        std::fflush(stdout);
        const auto launchPanel = [&](std::wstring const& args)
        {
            SHELLEXECUTEINFOW launch{};
            launch.cbSize = sizeof(launch);
            launch.fMask = SEE_MASK_NOCLOSEPROCESS;
            launch.lpFile = panel.c_str();
            launch.lpParameters = args.c_str();
            launch.nShow = SW_SHOWNORMAL;
            return winrt::handle(ShellExecuteExW(&launch) ? launch.hProcess : nullptr);
        };
        auto monitor = launchPanel(L"--monitor " + initialization);
        auto monitorRetry = GetTickCount64() + 6000;
        winrt::handle dashboardProcess;
        const auto ensureDashboard = [&]
        {
            if (dashboardProcess && WaitForSingleObject(dashboardProcess.get(), 0) == WAIT_TIMEOUT)
                return true;
            ResetEvent(session.panelVisible.get());
            dashboardProcess = launchPanel(L"--dashboard " + initialization + (trayMode ? L" --tray" : L" --on-demand"));
            return static_cast<bool>(dashboardProcess);
        };
        if (openOnStart)
            SetEvent(session.openLauncher.get());
        TaskbarVisibility visibility;
        const HANDLE signals[] = {session.stop.get(), explorer.get(), session.dashboard.get(), session.agent.get(),
            session.assistant.get(), session.openLauncher.get(), session.visibilityChanged.get()};
        const HANDLE panels[] = {session.showDashboard.get(), session.showAgent.get(), session.showAssistant.get(),
            session.showLauncher.get()};
        for (;;)
        {
            HANDLE waitFor[ARRAYSIZE(signals) + 2];
            std::copy(std::begin(signals), std::end(signals), waitFor);
            DWORD count = ARRAYSIZE(signals);
            const auto monitorIndex = monitor ? count++ : MAXDWORD;
            const auto dashboardIndex = dashboardProcess ? count++ : MAXDWORD;
            if (monitor)
                waitFor[monitorIndex] = monitor.get();
            if (dashboardProcess)
                waitFor[dashboardIndex] = dashboardProcess.get();
            const auto result = MsgWaitForMultipleObjects(count, waitFor, FALSE, monitor ? INFINITE : 6000, QS_ALLINPUT);
            if (result == WAIT_OBJECT_0 || result == WAIT_OBJECT_0 + 1 || result == WAIT_FAILED)
                break;
            if (result == monitorIndex)
            {
                monitor.close();
                continue;
            }
            if (!monitor && GetTickCount64() >= monitorRetry)
            {
                monitor = launchPanel(L"--monitor " + initialization);
                monitorRetry = GetTickCount64() + 6000;
            }
            if (result == dashboardIndex)
            {
                dashboardProcess.close();
                ResetEvent(session.panelVisible.get());
                visibility.Hold(false);
                continue;
            }
            if (result == ARRAYSIZE(signals) - 1)
            {
                visibility.Hold(dashboardProcess && WaitForSingleObject(session.panelVisible.get(), 0) == WAIT_OBJECT_0);
                continue;
            }
            if (result >= WAIT_OBJECT_0 + 2 && result < WAIT_OBJECT_0 + 2 + ARRAYSIZE(panels))
            {
                if (ensureDashboard())
                {
                    AllowSetForegroundWindow(GetProcessId(dashboardProcess.get()));
                    SetEvent(panels[result - WAIT_OBJECT_0 - 2]);
                }
                continue;
            }

            MSG message{};
            while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE))
            {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
        }

        SetEvent(session.stop.get());
        if (trayMode)
            return 0;
        const HANDLE shutdownSignals[] = {session.restored.get(), explorer.get()};
        const auto restored = WaitForMultipleObjects(ARRAYSIZE(shutdownSignals), shutdownSignals, FALSE, 3000);
        return restored == WAIT_TIMEOUT || restored == WAIT_FAILED ? 2 : 0;
    }
}

int wmain(int argc, wchar_t** argv)
{
    try { return Run(argc, argv); }
    catch (const winrt::hresult_error& error)
    {
        std::fwprintf(stderr, L"%ls\n", error.message().c_str());
        return 1;
    }
    catch (const std::exception& error)
    {
        std::fprintf(stderr, "%s\n", error.what());
        return 1;
    }
}
