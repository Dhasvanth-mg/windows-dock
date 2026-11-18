#include "Connection.h"
#include "Log.h"
#include "TaskbarHold.h"

#include <commctrl.h>

TaskbarHold::TaskbarHold(DWORD controllerPid)
    : visible(OpenEventW(SYNCHRONIZE, FALSE, SessionName(controllerPid, L"PanelVisible").c_str())),
      held(OpenEventW(EVENT_MODIFY_STATE, FALSE, SessionName(controllerPid, L"TaskbarHeld").c_str()))
{
    // One hold per Explorer is enough; a second copy of the dock's controls leaves it to the first.
    if (active || !visible || !held)
        return;
    tray = FindWindowW(L"Shell_TrayWnd", nullptr);
    if (!tray)
        return;
    if (GetWindowThreadProcessId(tray, nullptr) != GetCurrentThreadId())
    {
        DockLog(L"explorer", L"The taskbar runs on another thread, so the dashboard switches auto-hide off instead.");
        return;
    }
    if (!SetWindowSubclass(tray, Proc, TimerId, reinterpret_cast<DWORD_PTR>(this)))
    {
        DockLog(L"explorer", L"Could not watch the taskbar window; the dashboard switches auto-hide off instead.", GetLastError());
        return;
    }
    installed = true;
    active = this;
    SetEvent(held.get());
    DockLog(L"explorer", L"Holding the auto-hide taskbar up while the dashboard shows.");
}

TaskbarHold::~TaskbarHold()
{
    if (!installed)
        return;
    KillTimer(tray, TimerId);
    if (pending)
        FinishHide();
    RemoveWindowSubclass(tray, Proc, TimerId);
    ResetEvent(held.get());
    if (active == this)
        active = nullptr;
}

bool TaskbarHold::DashboardShowing() const noexcept
{
    return visible && WaitForSingleObject(visible.get(), 0) == WAIT_OBJECT_0;
}

// Lets the slide that was held back happen now: the taskbar goes where Explorer last asked it to go.
void TaskbarHold::FinishHide() noexcept
{
    pending = false;
    SetWindowPos(tray, nullptr, blocked.x, blocked.y, blocked.cx, blocked.cy,
        (blocked.flags & (SWP_NOSIZE | SWP_NOREDRAW | SWP_NOCOPYBITS | SWP_DEFERERASE)) | SWP_NOZORDER | SWP_NOACTIVATE);
}

LRESULT CALLBACK TaskbarHold::Proc(HWND window, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR id, DWORD_PTR data)
{
    const auto self = reinterpret_cast<TaskbarHold*>(data);
    switch (message)
    {
    case WM_WINDOWPOSCHANGING:
    {
        const auto position = reinterpret_cast<WINDOWPOS*>(lParam);
        if ((position->flags & SWP_NOMOVE) || !self->DashboardShowing())
            break;
        RECT current{};
        MONITORINFO monitor{sizeof(monitor)};
        if (!GetWindowRect(window, &current) ||
            !GetMonitorInfoW(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), &monitor))
            break;
        // A taskbar along the bottom that is fully on screen and being moved further down is sliding away.
        const auto screen = monitor.rcMonitor;
        const bool bottomEdge = current.top > (screen.top + screen.bottom) / 2;
        if (!bottomEdge || current.bottom > screen.bottom || position->y <= current.top)
            break;
        self->blocked = *position;
        self->pending = true;
        position->flags |= SWP_NOMOVE | SWP_NOSIZE;
        SetTimer(window, TimerId, 250, nullptr);
        break;
    }
    case WM_TIMER:
        if (wParam != TimerId)
            break;
        if (!self->DashboardShowing())
        {
            KillTimer(window, TimerId);
            if (self->pending)
                self->FinishHide();
        }
        return 0;
    case WM_NCDESTROY:
        RemoveWindowSubclass(window, Proc, id);
        self->installed = false;
        ResetEvent(self->held.get());
        if (active == self)
            active = nullptr;
        break;
    }
    return DefSubclassProc(window, message, wParam, lParam);
}
