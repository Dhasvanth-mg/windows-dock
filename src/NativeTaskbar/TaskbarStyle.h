#pragma once

#include "AgentGridState.h"

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Data.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Media.Animation.h>
#include <limits>
#include <memory>
#include <vector>

namespace xaml = winrt::Windows::UI::Xaml;
namespace controls = xaml::Controls;

class TaskbarStyle
{
public:
    TaskbarStyle(controls::Grid const& root, winrt::handle const& dashboard, DWORD controllerPid);
    ~TaskbarStyle();

    void Apply(xaml::FrameworkElement const& apps, xaml::FrameworkElement const& tray);
    void ApplyEmbeddedControls();
    void ShowEmbeddedAgents(bool visible);
    void StyleIndicator(xaml::FrameworkElement const& indicator);
    void Restore() noexcept;

    static xaml::FrameworkElement Find(xaml::DependencyObject const& root, std::wstring_view name);

private:
    static constexpr double AgentSlotWidth = 26;

    struct Property
    {
        winrt::weak_ref<xaml::DependencyObject> object;
        xaml::DependencyProperty property;
        winrt::Windows::Foundation::IInspectable value;
        xaml::Data::Binding binding{nullptr};
    };

    template <typename T>
    void Set(xaml::DependencyObject const& object, xaml::DependencyProperty const& property, T const& value)
    {
        Remember(object, property);
        object.SetValue(property, winrt::box_value(value));
    }

    void Remember(xaml::DependencyObject const& object, xaml::DependencyProperty const& property);
    void FitApps();
    void AddChrome();
    void AddGlass(int column, int span);
    void AddDashboardButton();
    void SetDashboardIcon();
    void AddAgentGrid();
    void RefreshAgentGrid();
    void StyleTray(xaml::FrameworkElement const& tray);
    void RefreshBattery();
    void PulseCompletion();

    controls::Grid root;
    HANDLE dashboard;
    winrt::handle launcherEvent, warmEvent, hideEvent, panelVisibleEvent;
    winrt::handle agentEvent, completionEvent, agentMapping;
    std::unique_ptr<AgentGridState, UnmapAgentGrid> agentState;
    AgentGridTile visibleTiles[4]{};
    LONG agentRevision = -1;
    ULONGLONG agentAppearanceChecked{};
    bool agentAppearanceDirty = true;
    bool agentHighContrast{};
    bool agentMotion = true;
    winrt::weak_ref<xaml::FrameworkElement> appFrame;
    winrt::weak_ref<xaml::FrameworkElement> trayFrame;
    controls::Border chrome{nullptr};
    xaml::Media::SolidColorBrush chromeRim{nullptr};
    xaml::Media::Animation::Storyboard completionPulse{nullptr};
    controls::Border divider{nullptr}, trayDivider{nullptr}, clockDivider{nullptr};
    controls::Button dashboardButton{nullptr};
    controls::Grid dockControls{nullptr};
    controls::Border agentTray{nullptr};
    controls::Border batteryFill{nullptr};
    controls::TextBlock batteryBolt{nullptr};
    controls::Grid batteryBadge{nullptr};
    DWORD batteryRefreshed{};
    BYTE batteryLevel{255};
    BYTE batteryFlags{255};
    controls::Button agentButtons[4]{nullptr, nullptr, nullptr, nullptr};
    controls::Grid agentCharacters[4]{nullptr, nullptr, nullptr, nullptr};
    controls::Image agentFaces[4]{nullptr, nullptr, nullptr, nullptr};
    controls::TextBlock agentSymbols[4]{nullptr, nullptr, nullptr, nullptr};
    xaml::Media::ImageSource agentSprites[9]{nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr};
    xaml::Media::Animation::Storyboard agentAnimations[4]{nullptr, nullptr, nullptr, nullptr};
    xaml::DispatcherTimer agentTimer{nullptr};
    winrt::event_token agentTick{};
    winrt::event_token rootSize{};
    winrt::event_token traySize{};
    std::vector<controls::ColumnDefinition> originalColumns;
    std::vector<Property> properties;
    bool columnsChanged{};
};
