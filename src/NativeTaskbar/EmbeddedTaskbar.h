#pragma once

#include "TaskbarStyle.h"
#include <winrt/Windows.UI.Xaml.Hosting.h>

// Owns a small XAML island inside Explorer's taskbar when the live tree is unavailable.
class EmbeddedTaskbar
{
public:
    ~EmbeddedTaskbar() noexcept;
    bool Attach(HWND taskbar, winrt::handle const& dashboard, DWORD controllerPid);

private:
    bool Position();

    HWND taskbar{}, host{}, island{};
    xaml::Hosting::DesktopWindowXamlSource source{nullptr};
    std::unique_ptr<TaskbarStyle> controls;
    xaml::DispatcherTimer placementTimer{nullptr};
    winrt::event_token placementTick{};
    RECT lastBounds{};
};
