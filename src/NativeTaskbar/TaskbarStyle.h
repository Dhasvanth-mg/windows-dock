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
    void StyleIndicator(xaml::FrameworkElement const& indicator);
    void Restore() noexcept;

    static xaml::FrameworkElement Find(xaml::DependencyObject const& root, std::wstring_view name);

private:
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
    void AddDashboardButton();
    void AddAgentGrid();
    void RefreshAgentGrid();
    void StyleTray(xaml::FrameworkElement const& tray);

    controls::Grid root;
    HANDLE dashboard;
    winrt::handle agentEvent, agentMapping;
    std::unique_ptr<AgentGridState, UnmapAgentGrid> agentState;
    AgentGridTile visibleTiles[4]{};
    LONG agentRevision = -1;
    winrt::weak_ref<xaml::FrameworkElement> appFrame;
    winrt::weak_ref<xaml::FrameworkElement> trayFrame;
    controls::Border chrome{nullptr};
    controls::Border divider{nullptr};
    controls::Button dashboardButton{nullptr};
    controls::Grid dockControls{nullptr};
    controls::Button agentButtons[4]{nullptr, nullptr, nullptr, nullptr};
    controls::TextBlock agentFaces[4]{nullptr, nullptr, nullptr, nullptr};
    xaml::Media::Animation::Storyboard agentAnimations[4]{nullptr, nullptr, nullptr, nullptr};
    xaml::DispatcherTimer agentTimer{nullptr};
    winrt::event_token agentTick{};
    winrt::event_token rootSize{};
    winrt::event_token traySize{};
    std::vector<controls::ColumnDefinition> originalColumns;
    std::vector<Property> properties;
    bool columnsChanged{};
};
