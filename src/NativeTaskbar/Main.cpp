#include "Connection.h"
#include "DockTray.h"
#include "Log.h"

#include <algorithm>
#include <cstdio>
#include <cwchar>
#include <filesystem>
#include <memory>
#include <iterator>
#include <thread>
#include <winrt/base.h>
#include <xamlom.h>

namespace
{
    HANDLE activeStop = nullptr;

    // Failures also go to native.log: at sign-in the controller has no console, so stderr is not seen.
    void PrintFailure(const wchar_t* message)
    {
        std::wstring text(message);
        while (!text.empty() && (text.back() == L'\n' || text.back() == L'\r'))
            text.pop_back();
        DockLog(L"controller", text.c_str());
        std::fputws(message, stderr);
    }

    struct Session
    {
        explicit Session(DWORD pid)
            : ready(CreateEventW(nullptr, TRUE, FALSE, SessionName(pid, L"Ready").c_str())),
              restored(CreateEventW(nullptr, TRUE, FALSE, SessionName(pid, L"Restored").c_str())),
              appearanceStop(CreateEventW(nullptr, TRUE, FALSE, SessionName(pid, L"AppearanceStop").c_str())),
              appearanceBlocked(CreateEventW(nullptr, TRUE, FALSE, SessionName(pid, L"AppearanceBlocked").c_str())),
              stop(CreateEventW(nullptr, TRUE, FALSE, SessionName(pid, L"Stop").c_str())),
              warmDashboard(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"WarmDashboard").c_str())),
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
            if (!ready || !restored || !appearanceStop || !appearanceBlocked || !stop || !warmDashboard ||
                !dashboard || !showDashboard || !agent || !showAgent ||
                !assistant || !showAssistant || !openLauncher || !showLauncher || !panelVisible || !visibilityChanged || !completion || !agentGrid)
                winrt::throw_last_error();
        }

        ~Session() { SetEvent(appearanceStop.get()); }

        winrt::handle ready, restored, appearanceStop, appearanceBlocked, stop, warmDashboard;
        winrt::handle dashboard, showDashboard, agent, showAgent, assistant, showAssistant;
        winrt::handle openLauncher, showLauncher, panelVisible, visibilityChanged, completion, agentGrid;
    };

    bool AttachAppearance(Session const& session, DWORD explorerPid, HANDLE explorer,
        std::filesystem::path const& module, std::wstring const& initialization)
    {
        OSVERSIONINFOW version{sizeof(version)};
        const auto ntdll = GetModuleHandleW(L"ntdll.dll");
        const auto getVersion = reinterpret_cast<LONG (WINAPI*)(OSVERSIONINFOW*)>(GetProcAddress(ntdll, "RtlGetVersion"));
        if (!getVersion || getVersion(&version) != 0 || version.dwBuildNumber != TestedWindowsBuild)
        {
            DockLog(L"controller", L"Native styling requires Windows 11 25H2, build 26200. Using tray mode.");
            return false;
        }
        if (!std::filesystem::is_regular_file(module))
        {
            DockLog(L"controller", L"The native styling module is missing. Using tray mode.");
            return false;
        }

        const auto library = LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        const auto attach = library ? reinterpret_cast<decltype(&InitializeXamlDiagnosticsEx)>(
            GetProcAddress(library, "InitializeXamlDiagnosticsEx")) : nullptr;
        HRESULT result = E_NOTIMPL;
        const auto deadline = GetTickCount64() + 3500;
        const HANDLE initializing[] = {session.stop.get(), explorer};
        do
        {
            for (int index = 1; attach && index <= 16 && FAILED(result); ++index)
            {
                const auto endpoint = L"VisualDiagConnection" + std::to_wstring(index);
                std::thread attempt([&]
                {
                    result = attach(endpoint.c_str(), explorerPid, nullptr, module.c_str(),
                        TaskbarSiteId, initialization.c_str());
                });
                attempt.join();
            }
        } while (result == HRESULT_FROM_WIN32(ERROR_NOT_FOUND) && GetTickCount64() < deadline &&
            WaitForMultipleObjects(ARRAYSIZE(initializing), initializing, FALSE, 180) == WAIT_TIMEOUT);
        if (library)
            FreeLibrary(library);
        if (FAILED(result))
        {
            SetEvent(session.appearanceStop.get());
            DockLog(L"controller", L"Explorer did not accept the styling connection. Using tray mode.",
                static_cast<unsigned long>(result));
            return false;
        }

        const HANDLE signals[] = {session.ready.get(), session.appearanceBlocked.get(), session.stop.get(), explorer};
        const auto ready = WaitForMultipleObjects(ARRAYSIZE(signals), signals, FALSE, 4000);
        if (ready == WAIT_OBJECT_0)
            return true;
        SetEvent(session.appearanceStop.get());
        DockLog(L"controller", ready == WAIT_OBJECT_0 + 1 ?
            L"The taskbar already has an appearance owner. Using tray mode." :
            L"The taskbar appearance did not become ready. Using tray mode.");
        return false;
    }

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
                PrintFailure(L"No active native dock session with that process ID.\n");
                return 1;
            }
            return 0;
        }

        bool openOnStart = false;
        bool trayOnly = false;
        for (int index = 1; index < argc; ++index)
        {
            const std::wstring_view argument(argv[index]);
            if (argument == L"--open" && !openOnStart) { openOnStart = true; continue; }
            if (argument == L"--tray" && !trayOnly) { trayOnly = true; continue; }
            std::fputws(L"Usage: NativeTaskbar.exe [--tray] [--open] | --restore <controller PID>\n", stderr);
            return 1;
        }

        winrt::handle owner(CreateMutexW(nullptr, TRUE, L"Local\\WindowsDock.NativeTaskbar.Controller"));
        if (!owner || GetLastError() == ERROR_ALREADY_EXISTS)
        {
            PrintFailure(L"A native dock session is already running.\n");
            return 1;
        }

        DWORD explorerPid = 0;
        GetWindowThreadProcessId(GetShellWindow(), &explorerPid);
        winrt::handle explorer(OpenProcess(SYNCHRONIZE, FALSE, explorerPid));
        if (!explorerPid || !explorer)
        {
            PrintFailure(L"Explorer's desktop window was not found.\n");
            return 1;
        }

        wchar_t path[MAX_PATH];
        const auto pathLength = GetModuleFileNameW(nullptr, path, ARRAYSIZE(path));
        if (!pathLength || pathLength == ARRAYSIZE(path))
            return 1;
        const auto directory = std::filesystem::path(path).parent_path();
        const auto module = directory / NATIVE_TASKBAR_DLL;
        const auto panel = directory / L"WindowsDock.exe";
        if (!std::filesystem::is_regular_file(panel))
        {
            PrintFailure(L"WindowsDock.exe must be beside this executable.\n");
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

        const auto initialization = std::to_wstring(controllerPid);
        const bool trayMode = trayOnly || !AttachAppearance(session, explorerPid, explorer.get(), module, initialization);
        if (WaitForSingleObject(session.stop.get(), 0) == WAIT_OBJECT_0 ||
            WaitForSingleObject(explorer.get(), 0) == WAIT_OBJECT_0)
            return 0;

        std::unique_ptr<AgentGridState, UnmapAgentGrid> grid(trayMode
            ? static_cast<AgentGridState*>(MapViewOfFile(session.agentGrid.get(), FILE_MAP_WRITE, 0, 0, sizeof(AgentGridState)))
            : nullptr);
        if (trayMode && !grid)
            winrt::throw_last_error();
        DockTray tray(trayMode, session.stop.get(), session.openLauncher.get(), session.agent.get(), session.assistant.get());
        if (trayMode && !tray.HasEntryPoint())
            return 1;
        tray.Watch(grid.get());
        if (trayMode)
            std::wprintf(L"Windows Dock tray mode active. Controller PID: %lu.\n", controllerPid);
        else
            std::wprintf(L"Native dock active in Explorer %lu. Controller PID: %lu.\n", explorerPid, controllerPid);
        tray.Explain();
        std::fflush(stdout);
        const auto launchPanel = [&](std::wstring const& args)
        {
            auto command = L"\"" + panel.wstring() + L"\" " + args;
            STARTUPINFOW launch{sizeof(launch)};
            PROCESS_INFORMATION process{};
            if (!CreateProcessW(panel.c_str(), command.data(), nullptr, nullptr, FALSE,
                CREATE_NO_WINDOW, nullptr, directory.c_str(), &launch, &process))
            {
                DockLog(L"controller", L"Could not start the dashboard process.", GetLastError());
                return winrt::handle{};
            }
            CloseHandle(process.hThread);
            return winrt::handle(process.hProcess);
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
            session.assistant.get(), session.openLauncher.get(), session.visibilityChanged.get(), session.warmDashboard.get()};
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
            if (result == WAIT_OBJECT_0 + 7)
            {
                ensureDashboard();
                continue;
            }
            if (result == WAIT_OBJECT_0 + 6)
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
        SetEvent(session.appearanceStop.get());
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
        DockLog(L"controller", error.message().c_str(), static_cast<unsigned long>(error.code().value));
        std::fwprintf(stderr, L"%ls\n", error.message().c_str());
        return 1;
    }
    catch (const std::exception& error)
    {
        DockLog(L"controller", L"Unexpected exception; see standard error.");
        std::fprintf(stderr, "%s\n", error.what());
        return 1;
    }
}
