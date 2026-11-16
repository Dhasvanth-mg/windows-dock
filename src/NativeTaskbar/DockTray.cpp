#include "DockTray.h"

#include <algorithm>
#include <cstdio>
#include <winrt/base.h>

namespace
{
    constexpr UINT TrayMessage = WM_APP + 1;
    constexpr UINT DockHotkey = 1;
    constexpr UINT AssistantHotkey = 2;
    constexpr UINT CommandHotkey = 3;
    constexpr UINT_PTR StatusTimer = 1;
    constexpr UINT_PTR BatteryTimer = 2;
    constexpr UINT DashboardCommand = 1, ChatsCommand = 2, ExitCommand = 3, AssistantCommand = 4, SearchCommand = 5;

    HICON MakeIcon(std::array<int32_t, 4> const& states)
    {
        constexpr int size = 16;
        constexpr uint32_t colors[] = {
            0xFFADA2C0, 0xFFCAB4FF, 0xFF9CC9FF, 0xFFFFD187,
            0xFFA1EDC5, 0xFFFF899E, 0xFFFBB19C, 0xFFE0B3FF
        };
        BITMAPINFO info{};
        info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = size;
        info.bmiHeader.biHeight = -size;
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        void* bits{};
        const auto bitmap = CreateDIBSection(nullptr, &info, DIB_RGB_COLORS, &bits, nullptr, 0);
        if (!bitmap)
            return nullptr;
        auto pixels = static_cast<uint32_t*>(bits);
        std::fill_n(pixels, size * size, 0);
        for (int y = 1; y < size - 1; ++y)
            for (int x = 1; x < size - 1; ++x)
                if ((x > 1 && x < size - 2) || (y > 1 && y < size - 2))
                    pixels[y * size + x] = 0xFF241B33;
        for (int slot = 0; slot < 4; ++slot)
        {
            const auto state = states[slot];
            const auto color = state < 0 ? 0xFF675675 : colors[state];
            const int left = 3 + (slot % 2) * 6;
            const int top = 3 + (slot / 2) * 6;
            for (int y = 0; y < 4; ++y)
                for (int x = 0; x < 4; ++x)
                    if (state >= 0 || x == 0 || x == 3 || y == 0 || y == 3)
                        pixels[(top + y) * size + left + x] = color;
            if (state == 3)
            {
                pixels[(top + 1) * size + left + 1] = 0xFF241B33;
                pixels[(top + 3) * size + left + 1] = 0xFF241B33;
            }
        }
        const BYTE clearMask[size * 2]{};
        const auto mask = CreateBitmap(size, size, 1, 1, clearMask);
        ICONINFO icon{TRUE, 0, 0, mask, bitmap};
        const auto result = mask ? CreateIconIndirect(&icon) : nullptr;
        if (mask)
            DeleteObject(mask);
        DeleteObject(bitmap);
        return result;
    }
}

DockTray::DockTray(bool trayMode, HANDLE stop, HANDLE openDashboard, HANDLE chats, HANDLE assistant, HANDLE command)
    : trayMode(trayMode), stop(stop), openDashboard(openDashboard), chats(chats), assistant(assistant), command(command)
{
    const auto instance = GetModuleHandleW(nullptr);
    taskbarCreated = RegisterWindowMessageW(L"TaskbarCreated");
    const WNDCLASSW windowClass{0, WindowProc, 0, 0, instance, nullptr, nullptr, nullptr, nullptr, L"WindowsDockNativeController"};
    if (!RegisterClassW(&windowClass))
        winrt::throw_last_error();
    window = CreateWindowExW(0, windowClass.lpszClassName, L"Windows Dock", WS_OVERLAPPED,
        0, 0, 0, 0, nullptr, nullptr, instance, this);
    if (!window)
        winrt::throw_last_error();
    hasHotkey = RegisterHotKey(window, DockHotkey, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'D') != FALSE;
    hasAssistantHotkey = RegisterHotKey(window, AssistantHotkey, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'A') != FALSE;
    hasCommandHotkey = RegisterHotKey(window, CommandHotkey, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'K') != FALSE;
    notification.cbSize = sizeof(notification);
    notification.hWnd = window;
    notification.uID = 1;
    notification.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
    notification.uCallbackMessage = TrayMessage;
    customIcon = MakeIcon({1, 2, 3, 4});
    notification.hIcon = customIcon ? customIcon : LoadIconW(nullptr, IDI_APPLICATION);
    wcscpy_s(notification.szTip, L"Windows Dock");
    AddIcon();
    SetTimer(window, BatteryTimer, 15000, nullptr);
    CheckBattery();
}

DockTray::~DockTray()
{
    if (hasIcon)
        Shell_NotifyIconW(NIM_DELETE, &notification);
    if (hasHotkey)
        UnregisterHotKey(window, DockHotkey);
    if (hasAssistantHotkey)
        UnregisterHotKey(window, AssistantHotkey);
    if (hasCommandHotkey)
        UnregisterHotKey(window, CommandHotkey);
    DestroyWindow(window);
    if (customIcon)
        DestroyIcon(customIcon);
}

void DockTray::Explain() const
{
    if (hasHotkey)
        std::fputws(L"Press Ctrl+Alt+D to open coding chats.\n", stdout);
    if (!trayMode)
        std::fputws(L"Click the Windows Dock button on the taskbar to open Dashboard.\n", stdout);
    if (hasAssistantHotkey)
        std::fputws(L"Press Ctrl+Alt+A to open the assistant.\n", stdout);
    if (hasCommandHotkey)
        std::fputws(L"Press Ctrl+Alt+K to search the dock.\n", stdout);
}

void DockTray::AddIcon()
{
    hasIcon = Shell_NotifyIconW(NIM_ADD, &notification) != FALSE;
    notification.uVersion = NOTIFYICON_VERSION_4;
    version4 = hasIcon && Shell_NotifyIconW(NIM_SETVERSION, &notification);
}

void DockTray::Watch(AgentGridState* state)
{
    grid = state;
    if (trayMode && grid)
    {
        UpdateStatus();
        SetTimer(window, StatusTimer, 1000, nullptr);
    }
}

void DockTray::UpdateStatus()
{
    const auto nextRevision = InterlockedCompareExchange(&grid->revision, 0, 0);
    if (nextRevision == revision || (nextRevision & 1))
        return;
    std::array<int32_t, 4> next{};
    for (size_t i = 0; i < next.size(); ++i)
        next[i] = grid->tiles[i].provider ? std::clamp(grid->tiles[i].state, 0, 7) : -1;
    if (InterlockedCompareExchange(&grid->revision, 0, 0) != nextRevision)
        return;
    revision = nextRevision;
    if (next == states)
        return;
    states = next;
    wchar_t tip[128]{};
    swprintf_s(tip, L"Windows Dock\n%zu working, %zu need approval, %zu replies ready",
        static_cast<size_t>(std::count(states.begin(), states.end(), 2)),
        static_cast<size_t>(std::count(states.begin(), states.end(), 3)),
        static_cast<size_t>(std::count(states.begin(), states.end(), 4)));
    wcscpy_s(notification.szTip, tip);
    if (const auto replacement = MakeIcon(states))
    {
        notification.hIcon = replacement;
        if (customIcon)
            DestroyIcon(customIcon);
        customIcon = replacement;
    }
    if (hasIcon && !Shell_NotifyIconW(NIM_MODIFY, &notification))
        AddIcon();
}

void DockTray::CheckBattery()
{
    SYSTEM_POWER_STATUS power{};
    if (!hasIcon || !GetSystemPowerStatus(&power))
        return;
    const auto crossed = battery.Observe(power.ACLineStatus, power.BatteryFlag, power.BatteryLifePercent);
    if (!crossed)
        return;
    auto alert = notification;
    alert.uFlags = NIF_INFO;
    alert.dwInfoFlags = NIIF_WARNING | NIIF_RESPECT_QUIET_TIME;
    swprintf_s(alert.szInfoTitle, L"Battery at %u%%", static_cast<unsigned>(power.BatteryLifePercent));
    wcscpy_s(alert.szInfo, crossed <= 10 ? L"Plug in now to keep working." : L"Connect a charger when you can.");
    Shell_NotifyIconW(NIM_MODIFY, &alert);
}

void DockTray::ShowMenu()
{
    const auto menu = CreatePopupMenu();
    if (!menu)
        return;
    AppendMenuW(menu, MF_STRING, ChatsCommand, L"Coding chats");
    AppendMenuW(menu, MF_STRING, AssistantCommand, L"Open assistant");
    AppendMenuW(menu, MF_STRING, DashboardCommand, L"Dashboard");
    AppendMenuW(menu, MF_STRING, SearchCommand, L"Search dock");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, ExitCommand, trayMode ? L"Exit Windows Dock" : L"Restore Windows taskbar");
    POINT anchor{};
    NOTIFYICONIDENTIFIER id{};
    id.cbSize = sizeof(id);
    id.hWnd = window;
    id.uID = notification.uID;
    RECT rect{};
    if (SUCCEEDED(Shell_NotifyIconGetRect(&id, &rect)))
        anchor = POINT{(rect.left + rect.right) / 2, rect.top};
    else
        GetCursorPos(&anchor);
    SetForegroundWindow(window);
    const auto choice = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN,
        anchor.x, anchor.y, 0, window, nullptr);
    PostMessageW(window, WM_NULL, 0, 0);
    switch (choice)
    {
    case ChatsCommand: SetEvent(chats); break;
    case DashboardCommand: SetEvent(openDashboard); break;
    case AssistantCommand: SetEvent(assistant); break;
    case SearchCommand: SetEvent(command); break;
    case ExitCommand: SetEvent(stop); break;
    default: Shell_NotifyIconW(NIM_SETFOCUS, &notification); break;
    }
    DestroyMenu(menu);
}

LRESULT CALLBACK DockTray::WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    if (message == WM_NCCREATE)
    {
        auto self = static_cast<DockTray*>(reinterpret_cast<CREATESTRUCTW*>(lParam)->lpCreateParams);
        self->window = window;
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
    }
    if (const auto self = reinterpret_cast<DockTray*>(GetWindowLongPtrW(window, GWLP_USERDATA)))
        return self->HandleMessage(message, wParam, lParam);
    return DefWindowProcW(window, message, wParam, lParam);
}

LRESULT DockTray::HandleMessage(UINT message, WPARAM wParam, LPARAM lParam)
{
    if (message == WM_HOTKEY && wParam == DockHotkey)
        SetEvent(chats);
    else if (message == WM_HOTKEY && wParam == AssistantHotkey)
        SetEvent(assistant);
    else if (message == WM_HOTKEY && wParam == CommandHotkey)
        SetEvent(command);
    else if (message == WM_TIMER && wParam == StatusTimer && grid)
        UpdateStatus();
    else if ((message == WM_TIMER && wParam == BatteryTimer) || message == WM_POWERBROADCAST)
        CheckBattery();
    else if (taskbarCreated && message == taskbarCreated)
        AddIcon();
    else if (message == TrayMessage)
    {
        const auto event = version4 ? LOWORD(lParam) : lParam;
        if (event == NIN_SELECT || event == NIN_KEYSELECT || (!version4 && event == WM_LBUTTONUP))
            SetEvent(openDashboard);
        else if (event == WM_CONTEXTMENU || (!version4 && event == WM_RBUTTONUP))
            ShowMenu();
    }
    return DefWindowProcW(window, message, wParam, lParam);
}
