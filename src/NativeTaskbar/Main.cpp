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
            : stop(CreateEventW(nullptr, TRUE, FALSE, SessionName(pid, L"Stop").c_str())),
              warmDashboard(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"WarmDashboard").c_str())),
              dashboard(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"Dashboard").c_str())),
              showDashboard(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"ShowDashboard").c_str())),
              agent(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"Agent").c_str())),
              showAgent(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"ShowAgent").c_str())),
              assistant(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"Assistant").c_str())),
              showAssistant(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"ShowAssistant").c_str())),
              openLauncher(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"OpenLauncher").c_str())),
              showLauncher(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"ShowLauncher").c_str())),
              command(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"Command").c_str())),
              showCommand(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"ShowCommand").c_str())),
              panelVisible(CreateEventW(nullptr, TRUE, FALSE, SessionName(pid, L"PanelVisible").c_str())),
              visibilityChanged(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"PanelVisibilityChanged").c_str())),
              completion(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"AgentCompletion").c_str())),
              agentGrid(CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(AgentGridState),
                  SessionName(pid, L"AgentGrid").c_str()))
        {
            if (!stop || !warmDashboard ||
                !dashboard || !showDashboard || !agent || !showAgent ||
                !assistant || !showAssistant || !openLauncher || !showLauncher || !command || !showCommand ||
                !panelVisible || !visibilityChanged || !completion || !agentGrid)
                winrt::throw_last_error();
        }

        winrt::handle stop, warmDashboard;
        winrt::handle dashboard, showDashboard, agent, showAgent, assistant, showAssistant;
        winrt::handle openLauncher, showLauncher, command, showCommand, panelVisible, visibilityChanged, completion, agentGrid;
    };

    struct Appearance
    {
        explicit Appearance(DWORD pid, unsigned attempt)
            : ready(CreateEventW(nullptr, TRUE, FALSE, AppearanceName(pid, attempt, L"Ready").c_str())),
              restored(CreateEventW(nullptr, TRUE, FALSE, AppearanceName(pid, attempt, L"Restored").c_str())),
              blocked(CreateEventW(nullptr, TRUE, FALSE, AppearanceName(pid, attempt, L"Blocked").c_str())),
              embedded(CreateEventW(nullptr, TRUE, FALSE, AppearanceName(pid, attempt, L"Embedded").c_str())),
              stop(CreateEventW(nullptr, TRUE, FALSE, AppearanceName(pid, attempt, L"Stop").c_str()))
        {
            if (!ready || !restored || !blocked || !embedded || !stop)
                winrt::throw_last_error();
        }

        ~Appearance() { SetEvent(stop.get()); }

        bool Restore(HANDLE explorer) const
        {
            SetEvent(stop.get());
            const HANDLE signals[] = {restored.get(), explorer};
            return WaitForMultipleObjects(ARRAYSIZE(signals), signals, FALSE, 3000) < WAIT_OBJECT_0 + ARRAYSIZE(signals);
        }

        winrt::handle ready, restored, blocked, embedded, stop;
    };

    bool SupportsAppearance(std::filesystem::path const& module)
    {
        OSVERSIONINFOW version{sizeof(version)};
        const auto ntdll = GetModuleHandleW(L"ntdll.dll");
        const auto getVersion = reinterpret_cast<LONG (WINAPI*)(OSVERSIONINFOW*)>(GetProcAddress(ntdll, "RtlGetVersion"));
        if (!getVersion || getVersion(&version) != 0 || version.dwBuildNumber != TestedWindowsBuild)
        {
            PrintFailure(L"The modified taskbar requires Windows 11 25H2, build 26200.\n");
            return false;
        }
        if (!std::filesystem::is_regular_file(module))
        {
            PrintFailure(L"The native taskbar module is missing.\n");
            return false;
        }

        return true;
    }

    std::unique_ptr<Appearance> AttachAppearance(Session const& session, DWORD controllerPid, DWORD explorerPid,
        HANDLE explorer, std::filesystem::path const& module, decltype(&InitializeXamlDiagnosticsEx) attach, unsigned& generation)
    {
        const HANDLE interrupts[] = {session.stop.get(), explorer};
        for (int index = 1; index <= 16 && WaitForMultipleObjects(ARRAYSIZE(interrupts), interrupts, FALSE, 0) == WAIT_TIMEOUT; ++index)
        {
            auto appearance = std::make_unique<Appearance>(controllerPid, ++generation);
            const auto endpoint = L"VisualDiagConnection" + std::to_wstring(index);
            const auto initialization = std::to_wstring(controllerPid) + L":" + std::to_wstring(generation);
            HRESULT result = E_FAIL;
            std::thread attempt([&]
            {
                result = attach(endpoint.c_str(), explorerPid, nullptr, module.c_str(),
                    TaskbarSiteId, initialization.c_str());
            });
            attempt.join();
            if (FAILED(result))
                continue;

            const HANDLE signals[] = {session.stop.get(), explorer, appearance->ready.get(), appearance->blocked.get()};
            const auto state = WaitForMultipleObjects(ARRAYSIZE(signals), signals, FALSE, 1800);
            if (state == WAIT_OBJECT_0 + 2)
                return appearance;
            const auto message = endpoint + (state == WAIT_OBJECT_0 + 3 ?
                L" found another taskbar appearance owner." : L" did not make the primary taskbar available.");
            if (state == WAIT_TIMEOUT || state == WAIT_OBJECT_0 + 3)
                DockLog(L"controller", message.c_str());
            if (!appearance->Restore(explorer) || state != WAIT_TIMEOUT)
                break;
        }
        return nullptr;
    }

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

        wchar_t path[MAX_PATH];
        const auto pathLength = GetModuleFileNameW(nullptr, path, ARRAYSIZE(path));
        if (!pathLength || pathLength == ARRAYSIZE(path))
            return 1;
        const auto executable = std::filesystem::path(path);
        const auto directory = executable.parent_path();
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

        const bool trayMode = trayOnly;
        if (!trayMode && !SupportsAppearance(module))
            return 1;
        const auto library = trayMode ? nullptr : LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        const auto attach = library ? reinterpret_cast<decltype(&InitializeXamlDiagnosticsEx)>(
            GetProcAddress(library, "InitializeXamlDiagnosticsEx")) : nullptr;
        if (!trayMode && !attach)
        {
            if (library)
                FreeLibrary(library);
            PrintFailure(L"Windows XAML Diagnostics is unavailable.\n");
            return 1;
        }
        DWORD explorerPid = 0;
        winrt::handle explorer;
        std::unique_ptr<Appearance> appearance;
        unsigned generation = 0;
        bool waitingReported = false;
        DWORD retryDelay = 1000;
        while (WaitForSingleObject(session.stop.get(), 0) == WAIT_TIMEOUT)
        {
            const auto taskbar = FindWindowW(L"Shell_TrayWnd", nullptr);
            DWORD shellPid = 0;
            explorerPid = 0;
            GetWindowThreadProcessId(GetShellWindow(), &shellPid);
            GetWindowThreadProcessId(taskbar, &explorerPid);
            explorer = winrt::handle(shellPid && shellPid == explorerPid ? OpenProcess(SYNCHRONIZE, FALSE, explorerPid) : nullptr);
            if (explorer)
            {
                if (trayMode)
                    break;
                appearance = AttachAppearance(session, controllerPid, explorerPid, explorer.get(), module, attach, generation);
                if (appearance && WaitForSingleObject(explorer.get(), 0) == WAIT_TIMEOUT)
                    break;
                appearance.reset();
            }
            if (!waitingReported && WaitForSingleObject(session.stop.get(), 0) == WAIT_TIMEOUT)
            {
                PrintFailure(L"Waiting for Explorer's modified taskbar. Windows Dock will retry.\n");
                waitingReported = true;
            }
            if (explorer)
            {
                const HANDLE signals[] = {session.stop.get(), explorer.get()};
                const auto result = WaitForMultipleObjects(ARRAYSIZE(signals), signals, FALSE, retryDelay);
                retryDelay = result == WAIT_TIMEOUT ? std::min<DWORD>(retryDelay * 2, 30'000) : 1000;
            }
            else
            {
                retryDelay = 1000;
                WaitForSingleObject(session.stop.get(), 600);
            }
        }
        if (library)
            FreeLibrary(library);
        if (WaitForSingleObject(session.stop.get(), 0) == WAIT_OBJECT_0 ||
            WaitForSingleObject(explorer.get(), 0) == WAIT_OBJECT_0)
            return 0;

        std::unique_ptr<AgentGridState, UnmapAgentGrid> grid(trayMode
            ? static_cast<AgentGridState*>(MapViewOfFile(session.agentGrid.get(), FILE_MAP_WRITE, 0, 0, sizeof(AgentGridState)))
            : nullptr);
        if (trayMode && !grid)
            winrt::throw_last_error();
        DockTray tray(trayMode, session.stop.get(), session.openLauncher.get(), session.agent.get(), session.assistant.get(), session.command.get());
        if (trayMode && !tray.HasEntryPoint())
            return 1;
        tray.Watch(grid.get());
        if (trayMode)
            std::wprintf(L"Windows Dock tray mode active. Controller PID: %lu.\n", controllerPid);
        else if (appearance && WaitForSingleObject(appearance->embedded.get(), 0) == WAIT_OBJECT_0)
            std::wprintf(L"Native taskbar controls active in Explorer %lu. Controller PID: %lu.\n", explorerPid, controllerPid);
        else
            std::wprintf(L"Native dock active in Explorer %lu. Controller PID: %lu.\n", explorerPid, controllerPid);
        tray.Explain();
        std::fflush(stdout);
        const auto launchProcess = [&](std::filesystem::path const& program, std::wstring const& args)
        {
            auto command = L"\"" + program.wstring() + L"\" " + args;
            STARTUPINFOW launch{sizeof(launch)};
            PROCESS_INFORMATION process{};
            if (!CreateProcessW(program.c_str(), command.data(), nullptr, nullptr, FALSE,
                CREATE_NO_WINDOW, nullptr, directory.c_str(), &launch, &process))
            {
                DockLog(L"controller", L"Could not start a Windows Dock process.", GetLastError());
                return winrt::handle{};
            }
            CloseHandle(process.hThread);
            return winrt::handle(process.hProcess);
        };
        const auto initialization = std::to_wstring(controllerPid);
        auto monitor = launchProcess(panel, L"--monitor " + initialization);
        auto monitorRetry = GetTickCount64() + 6000;
        winrt::handle dashboardProcess;
        const auto ensureDashboard = [&]
        {
            if (dashboardProcess && WaitForSingleObject(dashboardProcess.get(), 0) == WAIT_TIMEOUT)
                return true;
            ResetEvent(session.panelVisible.get());
            dashboardProcess = launchProcess(panel, L"--dashboard " + initialization + (trayMode ? L" --tray" : L" --on-demand"));
            return static_cast<bool>(dashboardProcess);
        };
        if (openOnStart)
            SetEvent(session.openLauncher.get());

        const HANDLE signals[] = {session.stop.get(), explorer.get(), session.dashboard.get(), session.agent.get(),
            session.assistant.get(), session.openLauncher.get(), session.command.get(), session.visibilityChanged.get(), session.warmDashboard.get()};
        const HANDLE panels[] = {session.showDashboard.get(), session.showAgent.get(), session.showAssistant.get(),
            session.showLauncher.get(), session.showCommand.get()};
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
                monitor = launchProcess(panel, L"--monitor " + initialization);
                monitorRetry = GetTickCount64() + 6000;
            }
            if (result == dashboardIndex)
            {
                dashboardProcess.close();
                ResetEvent(session.panelVisible.get());
                continue;
            }
            if (result == WAIT_OBJECT_0 + ARRAYSIZE(signals) - 1)
            {
                ensureDashboard();
                continue;
            }
            if (result == WAIT_OBJECT_0 + ARRAYSIZE(signals) - 2)
            {
                // Auto-hide is left as the user set it. Switching it off while the panel shows changes the work area, and
                // Windows then resizes every maximised window to sit above the taskbar.
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

        const auto shellExited = WaitForSingleObject(explorer.get(), 0) == WAIT_OBJECT_0;
        const auto restart = !trayMode && shellExited && WaitForSingleObject(session.stop.get(), 0) == WAIT_TIMEOUT;
        const auto reopen = WaitForSingleObject(session.panelVisible.get(), 0) == WAIT_OBJECT_0;
        SetEvent(session.stop.get());
        if (appearance && !appearance->Restore(explorer.get()))
            return 2;
        if (restart)
        {
            ReleaseMutex(owner.get());
            owner.close();
            return launchProcess(executable, reopen ? L"--open" : L"") ? 0 : 1;
        }
        return 0;
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
