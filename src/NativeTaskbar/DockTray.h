#pragma once

#include "AgentGridState.h"
#include "BatteryAlerts.h"

#include <array>
#include <shellapi.h>

class DockTray
{
public:
    DockTray(bool trayMode, HANDLE stop, HANDLE openDashboard, HANDLE chats, HANDLE assistant);
    ~DockTray();
    DockTray(DockTray const&) = delete;
    DockTray& operator=(DockTray const&) = delete;

    bool HasEntryPoint() const { return hasIcon || hasHotkey; }
    void Watch(AgentGridState* grid);
    void Explain() const;

private:
    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam);
    LRESULT HandleMessage(UINT message, WPARAM wParam, LPARAM lParam);
    void AddIcon();
    void UpdateStatus();
    void CheckBattery();
    void ShowMenu();

    const bool trayMode;
    HANDLE stop, openDashboard, chats, assistant;
    HWND window{};
    UINT taskbarCreated{};
    NOTIFYICONDATAW notification{};
    HICON customIcon{};
    bool hasIcon{}, hasHotkey{}, hasAssistantHotkey{}, version4{};
    AgentGridState* grid{};
    LONG revision{-1};
    std::array<int32_t, 4> states{-2, -2, -2, -2};
    BatteryAlerts battery;
};
