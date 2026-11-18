#pragma once

#include <windows.h>
#include <winrt/base.h>

// Keeps an auto-hiding taskbar on screen while the dashboard is showing, without switching auto-hide off. Turning
// auto-hide off would change the screen's work area, and Windows would then resize every maximised window. Instead,
// from inside Explorer, the taskbar window's slide down off the screen is held back while the dashboard shows, and
// finished once it closes. Explorer still considers the taskbar auto-hidden throughout, so the work area never moves.
//
// The hook can only be set from the thread that owns the taskbar window. When it cannot be set, the controller is
// told (the "TaskbarHeld" event stays clear) and falls back to switching auto-hide off.
class TaskbarHold
{
public:
    explicit TaskbarHold(DWORD controllerPid);
    ~TaskbarHold();
    TaskbarHold(TaskbarHold const&) = delete;
    TaskbarHold& operator=(TaskbarHold const&) = delete;

private:
    static constexpr UINT_PTR TimerId = 0x57444B48;
    static LRESULT CALLBACK Proc(HWND window, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR id, DWORD_PTR data);
    bool DashboardShowing() const noexcept;
    void FinishHide() noexcept;

    HWND tray{};
    winrt::handle visible, held;
    bool installed{};
    bool pending{};
    WINDOWPOS blocked{};
    static inline TaskbarHold* active{};
};
