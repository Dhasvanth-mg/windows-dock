#include "EmbeddedTaskbar.h"

#include <algorithm>
#include <windows.ui.xaml.hosting.desktopwindowxamlsource.h>

namespace
{
    bool ChildBounds(HWND taskbar, const wchar_t* className, RECT& bounds)
    {
        const auto child = FindWindowExW(taskbar, nullptr, className, nullptr);
        return child && GetWindowRect(child, &bounds) && bounds.right > bounds.left;
    }

    struct EmbeddedBounds
    {
        RECT rect{};
        bool showAgents{};
    };

    EmbeddedBounds FreeTaskbarSpace(HWND taskbar)
    {
        RECT bar{}, apps{}, tray{};
        if (!GetWindowRect(taskbar, &bar) || !ChildBounds(taskbar, L"ReBarWindow32", apps) ||
            !ChildBounds(taskbar, L"TrayNotifyWnd", tray) || apps.left < bar.left || apps.right > tray.left ||
            tray.left > bar.right || bar.bottom <= bar.top)
            return {};

        // Explorer exposes the bounds of its app group and tray through these child windows.
        // Keep the island out of both areas, including when the centered group grows.
        RECT start{};
        if (ChildBounds(taskbar, L"Start", start))
        {
            apps.left = std::min(apps.left, start.left);
            apps.right = std::max(apps.right, start.right);
        }
        const auto dpi = GetDpiForWindow(taskbar);
        if (!dpi)
            return {};
        const auto padding = MulDiv(6, dpi, 96);
        const auto right = tray.left - apps.right - padding * 2;
        const auto left = apps.left - bar.left - padding * 2;
        for (const bool showAgents : {true, false})
        {
            const auto width = MulDiv(showAgents ? 170 : 53, dpi, 96);
            if (right >= width)
                return {{apps.right + padding, bar.top, apps.right + padding + width, bar.bottom}, showAgents};
            if (left >= width)
                return {{apps.left - padding - width, bar.top, apps.left - padding, bar.bottom}, showAgents};
        }
        return {};
    }
}

bool EmbeddedTaskbar::Attach(HWND window, winrt::handle const& dashboard, DWORD controllerPid)
{
    if (!window || GetWindowThreadProcessId(window, nullptr) != GetCurrentThreadId())
        return false;
    taskbar = window;
    host = CreateWindowExW(0, L"STATIC", L"Windows Dock taskbar controls",
        WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS, 0, 0, 1, 1, window, nullptr, nullptr, nullptr);
    if (!host)
        winrt::throw_last_error();

    source = xaml::Hosting::DesktopWindowXamlSource();
    auto native = source.as<IDesktopWindowXamlSourceNative>();
    winrt::check_hresult(native->AttachToWindow(host));
    winrt::check_hresult(native->get_WindowHandle(&island));
    if (!island)
        return false;
    winrt::Windows::UI::Xaml::Controls::Grid root;
    source.Content(root);
    controls = std::make_unique<TaskbarStyle>(root, dashboard, controllerPid);
    controls->ApplyEmbeddedControls();
    if (!Position())
        return false;
    placementTimer = xaml::DispatcherTimer();
    placementTimer.Interval(std::chrono::seconds(1));
    placementTick = placementTimer.Tick([this](auto&&, auto&&)
    {
        try { Position(); }
        catch (...) { ShowWindow(host, SW_HIDE); }
    });
    placementTimer.Start();
    return true;
}

bool EmbeddedTaskbar::Position()
{
    auto [bounds, showAgents] = FreeTaskbarSpace(taskbar);
    if (bounds.right <= bounds.left)
    {
        ShowWindow(host, SW_HIDE);
        lastBounds = {};
        return false;
    }
    POINT topLeft{bounds.left, bounds.top}, bottomRight{bounds.right, bounds.bottom};
    if (!ScreenToClient(taskbar, &topLeft) || !ScreenToClient(taskbar, &bottomRight))
        return false;
    bounds = {topLeft.x, topLeft.y, bottomRight.x, bottomRight.y};
    if (EqualRect(&bounds, &lastBounds) && IsWindowVisible(host))
        return true;
    const auto width = bounds.right - bounds.left;
    const auto height = bounds.bottom - bounds.top;
    controls->ShowEmbeddedAgents(showAgents);
    if (!SetWindowPos(island, nullptr, 0, 0, width, height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW) ||
        !SetWindowPos(host, HWND_TOP, bounds.left, bounds.top, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW))
        return false;
    lastBounds = bounds;
    return true;
}

EmbeddedTaskbar::~EmbeddedTaskbar() noexcept
{
    if (placementTimer)
        try
        {
            placementTimer.Stop();
            placementTimer.Tick(placementTick);
        }
        catch (...) {}
    controls.reset();
    if (source)
        try { source.Close(); }
        catch (...) {}
    if (host)
        DestroyWindow(host);
}
