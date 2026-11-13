#include "Connection.h"

#include <cstdio>
#include <cwchar>
#include <filesystem>
#include <shellapi.h>
#include <thread>
#include <winrt/base.h>
#include <xamlom.h>

namespace
{
    constexpr auto StopCommand = 1;
    constexpr auto DashboardCommand = 2;
    constexpr auto TrayMessage = WM_APP + 1;
    constexpr UINT RestoreHotkey = 1;
    HANDLE activeStop = nullptr;
    HANDLE activeDashboard = nullptr;

    struct Session
    {
        explicit Session(DWORD pid)
            : ready(CreateEventW(nullptr, TRUE, FALSE, SessionName(pid, L"Ready").c_str())),
              restored(CreateEventW(nullptr, TRUE, FALSE, SessionName(pid, L"Restored").c_str())),
              stop(CreateEventW(nullptr, TRUE, FALSE, SessionName(pid, L"Stop").c_str())),
              dashboard(CreateEventW(nullptr, FALSE, FALSE, SessionName(pid, L"Dashboard").c_str()))
        {
            if (!ready || !restored || !stop || !dashboard)
                winrt::throw_last_error();
        }

        winrt::handle ready, restored, stop, dashboard;
    };

    LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
    {
        if (message == WM_HOTKEY && wParam == RestoreHotkey)
            SetEvent(activeStop);
        else if (message == TrayMessage && lParam == WM_LBUTTONUP)
            SetEvent(activeDashboard);
        else if (message == TrayMessage && (lParam == WM_CONTEXTMENU || lParam == WM_RBUTTONUP))
        {
            const auto menu = CreatePopupMenu();
            if (menu)
            {
                AppendMenuW(menu, MF_STRING, DashboardCommand, L"Open dock dashboard");
                AppendMenuW(menu, MF_STRING, StopCommand, L"Restore Windows taskbar");
                POINT cursor{};
                GetCursorPos(&cursor);
                SetForegroundWindow(window);
                const auto command = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, cursor.x, cursor.y, 0, window, nullptr);
                if (command == StopCommand)
                    SetEvent(activeStop);
                if (command == DashboardCommand)
                    SetEvent(activeDashboard);
                DestroyMenu(menu);
            }
        }
        return DefWindowProcW(window, message, wParam, lParam);
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
                std::fputws(L"No active native dock session with that process ID.\n", stderr);
                return 1;
            }
            return 0;
        }

        if (argc != 1)
        {
            std::fputws(L"Usage: NativeTaskbar.exe [--restore <controller PID>]\n", stderr);
            return 1;
        }

        OSVERSIONINFOW version{};
        version.dwOSVersionInfoSize = sizeof(version);
        const auto ntdll = GetModuleHandleW(L"ntdll.dll");
        const auto getVersion = reinterpret_cast<LONG (WINAPI*)(OSVERSIONINFOW*)>(
            GetProcAddress(ntdll, "RtlGetVersion"));
        if (!getVersion || getVersion(&version) != 0 || version.dwBuildNumber != TestedWindowsBuild)
        {
            std::fputws(L"This preview is limited to Windows 11 x64 build 26200 (25H2).\n", stderr);
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
        const auto module = directory / L"NativeTaskbar.Xaml.dll";
        if (!std::filesystem::is_regular_file(module))
        {
            std::fputws(L"NativeTaskbar.Xaml.dll must be beside this executable.\n", stderr);
            return 1;
        }

        const auto controllerPid = GetCurrentProcessId();
        const Session session(controllerPid);
        activeStop = session.stop.get();
        activeDashboard = session.dashboard.get();
        SetConsoleCtrlHandler([](DWORD event) -> BOOL
        {
            if (event == CTRL_C_EVENT || event == CTRL_BREAK_EVENT || event == CTRL_CLOSE_EVENT)
            {
                SetEvent(activeStop);
                return TRUE;
            }
            return FALSE;
        }, TRUE);

        const auto instance = GetModuleHandleW(nullptr);
        const WNDCLASSW windowClass{0, WindowProc, 0, 0, instance, nullptr, nullptr, nullptr, nullptr, L"WindowsDockNativeController"};
        RegisterClassW(&windowClass);
        const auto window = CreateWindowExW(0, windowClass.lpszClassName, L"Windows Dock", WS_OVERLAPPED,
            0, 0, 0, 0, nullptr, nullptr, instance, nullptr);
        if (!window)
            winrt::throw_last_error();

        const bool hotkey = RegisterHotKey(window, RestoreHotkey, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'D') != FALSE;
        NOTIFYICONDATAW icon{};
        icon.cbSize = sizeof(icon);
        icon.hWnd = window;
        icon.uID = 1;
        icon.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        icon.uCallbackMessage = TrayMessage;
        icon.hIcon = LoadIconW(nullptr, IDI_APPLICATION);
        wcscpy_s(icon.szTip, L"Windows Dock");
        const bool hasIcon = Shell_NotifyIconW(NIM_ADD, &icon) != FALSE;
        if (!hotkey && !hasIcon)
            return 1;
        struct Cleanup
        {
            HWND window;
            NOTIFYICONDATAW& icon;
            bool hasIcon, hotkey;
            ~Cleanup()
            {
                if (hasIcon)
                    Shell_NotifyIconW(NIM_DELETE, &icon);
                if (hotkey)
                    UnregisterHotKey(window, RestoreHotkey);
                DestroyWindow(window);
            }
        } cleanup{window, icon, hasIcon, hotkey};

        const auto xaml = LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!xaml)
            return 1;
        const auto attach = reinterpret_cast<decltype(&InitializeXamlDiagnosticsEx)>(
            GetProcAddress(xaml, "InitializeXamlDiagnosticsEx"));
        if (!attach)
            return 1;

        const auto initialization = std::to_wstring(controllerPid);
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
            std::fwprintf(stderr, L"Native dock could not style the current taskbar (0x%08lX). "
                L"Turn off other taskbar appearance tools and try again.\n", static_cast<unsigned long>(lastError));
            return 1;
        }

        std::wprintf(L"Native dock active in Explorer %lu. Controller PID: %lu.\n", explorerPid, controllerPid);
        if (hotkey)
            std::fputws(L"Press Ctrl+Alt+D or use the notification area icon to restore.\n", stdout);
        else
            std::fputws(L"Use the notification area icon to restore.\n", stdout);
        std::fflush(stdout);
        winrt::handle dashboardProcess;
        const auto panel = directory / L"WindowsDock.exe";
        const HANDLE signals[] = {session.stop.get(), explorer.get(), session.dashboard.get()};
        for (;;)
        {
            const auto result = MsgWaitForMultipleObjects(ARRAYSIZE(signals), signals, FALSE, INFINITE, QS_ALLINPUT);
            if (result == WAIT_OBJECT_0 || result == WAIT_OBJECT_0 + 1 || result == WAIT_FAILED)
                break;
            if (result == WAIT_OBJECT_0 + 2)
            {
                if ((!dashboardProcess || WaitForSingleObject(dashboardProcess.get(), 0) == WAIT_OBJECT_0) &&
                    std::filesystem::is_regular_file(panel))
                {
                    const auto args = L"--dashboard " + initialization;
                    SHELLEXECUTEINFOW launch{};
                    launch.cbSize = sizeof(launch);
                    launch.fMask = SEE_MASK_NOCLOSEPROCESS;
                    launch.lpFile = panel.c_str();
                    launch.lpParameters = args.c_str();
                    launch.nShow = SW_SHOWNORMAL;
                    if (ShellExecuteExW(&launch))
                        dashboardProcess.attach(launch.hProcess);
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
