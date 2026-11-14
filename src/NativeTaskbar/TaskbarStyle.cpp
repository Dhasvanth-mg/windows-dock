#include "Connection.h"
#undef GetCurrentTime
#include "TaskbarStyle.h"

#include <winrt/Windows.UI.Text.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Controls.Primitives.h>
#include <winrt/Windows.UI.Xaml.Data.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>
#include <winrt/Windows.UI.ViewManagement.h>
#include <algorithm>
#include <cmath>

namespace media = xaml::Media;
namespace shapes = xaml::Shapes;
using winrt::Windows::UI::Color;
using xaml::FrameworkElement;
using controls::Grid;

namespace
{
    media::SolidColorBrush Solid(uint8_t alpha, uint8_t red, uint8_t green, uint8_t blue)
    {
        return media::SolidColorBrush(Color{alpha, red, green, blue});
    }

    media::LinearGradientBrush Rim()
    {
        media::LinearGradientBrush brush;
        brush.StartPoint({0, 0});
        brush.EndPoint({0, 1});
        for (const auto [position, color] : std::initializer_list<std::pair<double, Color>>{
            {0.0, {105, 207, 188, 252}}, {0.46, {33, 163, 147, 208}}, {1.0, {64, 174, 200, 240}}})
        {
            media::GradientStop stop;
            stop.Offset(position);
            stop.Color(color);
            brush.GradientStops().Append(stop);
        }
        return brush;
    }
}

TaskbarStyle::TaskbarStyle(Grid const& root, winrt::handle const& dashboard, DWORD controllerPid)
    : root(root), dashboard(dashboard.get()),
      agentEvent(OpenEventW(EVENT_MODIFY_STATE, FALSE, SessionName(controllerPid, L"Agent").c_str())),
      agentMapping(OpenFileMappingW(FILE_MAP_WRITE, FALSE, SessionName(controllerPid, L"AgentGrid").c_str())),
      agentState(agentMapping ? static_cast<AgentGridState*>(MapViewOfFile(agentMapping.get(), FILE_MAP_WRITE,
          0, 0, sizeof(AgentGridState))) : nullptr)
{
}

TaskbarStyle::~TaskbarStyle()
{
    Restore();
}

FrameworkElement TaskbarStyle::Find(xaml::DependencyObject const& root, std::wstring_view name)
{
    if (auto element = root.try_as<FrameworkElement>(); element && element.Name() == name)
        return element;
    const auto count = media::VisualTreeHelper::GetChildrenCount(root);
    for (int index = 0; index < count; ++index)
        if (auto match = Find(media::VisualTreeHelper::GetChild(root, index), name))
            return match;
    return nullptr;
}

void TaskbarStyle::Apply(FrameworkElement const& apps, FrameworkElement const& tray)
{
    const auto background = Find(apps, L"BackgroundFill");
    const auto stroke = Find(apps, L"BackgroundStroke");
    const auto trayContents = Find(tray, L"SystemTrayFrameGrid");
    if (!background || !stroke || !trayContents || root.ActualWidth() < 400 || root.ActualHeight() > 88)
        throw winrt::hresult_error(E_NOTIMPL);

    const auto columns = root.ColumnDefinitions();
    for (const auto& column : columns)
        originalColumns.push_back(column);

    appFrame = winrt::make_weak(apps);
    trayFrame = winrt::make_weak(tray);
    columnsChanged = true;
    columns.Clear();
    for (const auto unit : {xaml::GridUnitType::Star, xaml::GridUnitType::Auto,
        xaml::GridUnitType::Auto, xaml::GridUnitType::Auto, xaml::GridUnitType::Star})
    {
        controls::ColumnDefinition column;
        column.Width({1, unit});
        columns.Append(column);
    }

    Set(apps, Grid::ColumnProperty(), 1);
    Set(apps, Grid::ColumnSpanProperty(), 1);
    Set(apps, FrameworkElement::WidthProperty(), std::numeric_limits<double>::quiet_NaN());
    Set(apps, FrameworkElement::HorizontalAlignmentProperty(), xaml::HorizontalAlignment::Left);
    Set(apps, FrameworkElement::MarginProperty(), xaml::Thickness{8, 0, 4, 0});
    Set(apps, FrameworkElement::RequestedThemeProperty(), xaml::ElementTheme::Dark);
    Set(tray, Grid::ColumnProperty(), 3);
    Set(tray, Grid::ColumnSpanProperty(), 1);
    Set(tray, FrameworkElement::HorizontalAlignmentProperty(), xaml::HorizontalAlignment::Left);
    Set(tray, FrameworkElement::MarginProperty(), xaml::Thickness{0, 0, 8, 0});
    Set(tray, FrameworkElement::RequestedThemeProperty(), xaml::ElementTheme::Dark);

    Set(background, xaml::UIElement::OpacityProperty(), 0.0);
    Set(stroke, xaml::UIElement::OpacityProperty(), 0.0);
    if (auto edge = Find(apps, L"ScreenEdgeStroke"))
        Set(edge, xaml::UIElement::OpacityProperty(), 0.0);

    AddChrome();
    AddDashboardButton();
    AddAgentGrid();
    StyleTray(tray);

    rootSize = root.SizeChanged([this](auto&&, auto&&) { try { FitApps(); } catch (...) {} });
    traySize = tray.SizeChanged([this](auto&&, auto&&) { try { FitApps(); } catch (...) {} });
    FitApps();

    const auto repeater = Find(apps, L"TaskbarFrameRepeater");
    if (repeater)
    {
        const auto count = media::VisualTreeHelper::GetChildrenCount(repeater);
        for (int index = 0; index < count; ++index)
            if (auto indicator = Find(media::VisualTreeHelper::GetChild(repeater, index), L"RunningIndicator"))
                StyleIndicator(indicator);
    }
}

void TaskbarStyle::AddChrome()
{
    chrome = controls::Border();
    chrome.Name(L"WindowsDockGlass");
    chrome.IsHitTestVisible(false);
    chrome.CornerRadius({18, 18, 18, 18});
    chrome.Margin({0, 2, 0, 4});
    chrome.BorderThickness({1, 1, 1, 1});
    chrome.BorderBrush(Rim());
    media::AcrylicBrush glass;
    glass.BackgroundSource(media::AcrylicBackgroundSource::Backdrop);
    glass.TintColor(Color{255, 18, 15, 27});
    glass.TintOpacity(0.83);
    glass.TintLuminosityOpacity(0.10);
    glass.FallbackColor(Color{246, 22, 19, 32});
    chrome.Background(glass);

    Grid::SetColumn(chrome, 1);
    Grid::SetColumnSpan(chrome, 3);
    controls::Canvas::SetZIndex(chrome, -1);
    root.Children().InsertAt(0, chrome);

    divider = controls::Border();
    divider.Width(1);
    divider.Height(20);
    divider.HorizontalAlignment(xaml::HorizontalAlignment::Left);
    divider.VerticalAlignment(xaml::VerticalAlignment::Center);
    divider.Margin({1, 0, 0, 0});
    divider.Background(Solid(57, 191, 169, 245));
    divider.IsHitTestVisible(false);
    Grid::SetColumn(divider, 2);
    root.Children().Append(divider);
}

void TaskbarStyle::AddDashboardButton()
{
    dashboardButton = controls::Button();
    dashboardButton.Name(L"WindowsDockDashboard");
    dashboardButton.Width(34);
    dashboardButton.Height(30);
    dashboardButton.Margin({7, 0, 4, 1});
    dashboardButton.Padding({0, 0, 0, 0});
    dashboardButton.Background(Solid(33, 196, 174, 255));
    dashboardButton.BorderBrush(Solid(52, 196, 174, 255));
    dashboardButton.BorderThickness({1, 1, 1, 1});
    dashboardButton.CornerRadius({9, 9, 9, 9});
    dashboardButton.HorizontalAlignment(xaml::HorizontalAlignment::Center);
    dashboardButton.VerticalAlignment(xaml::VerticalAlignment::Center);
    dashboardButton.RequestedTheme(xaml::ElementTheme::Dark);
    xaml::Automation::AutomationProperties::SetName(dashboardButton, L"Dock dashboard");
    controls::ToolTipService::SetToolTip(dashboardButton, winrt::box_value(L"Dock dashboard"));

    controls::TextBlock icon;
    icon.Text(L"\u2726");
    icon.FontSize(16);
    icon.FontFamily(media::FontFamily(L"Segoe UI Symbol"));
    icon.Foreground(Solid(255, 220, 204, 255));
    dashboardButton.Content(icon);
    dashboardButton.Click([event = dashboard](auto&&, auto&&) { SetEvent(event); });
    dockControls = controls::Grid();
    dockControls.VerticalAlignment(xaml::VerticalAlignment::Center);
    for (int index = 0; index < 2; ++index)
    {
        controls::ColumnDefinition column;
        column.Width({1, xaml::GridUnitType::Auto});
        dockControls.ColumnDefinitions().Append(column);
    }
    dockControls.Children().Append(dashboardButton);
    Grid::SetColumn(dockControls, 2);
    root.Children().Append(dockControls);
}

void TaskbarStyle::StyleTray(FrameworkElement const& tray)
{
    const auto time = Find(tray, L"TimeInnerTextBlock").try_as<controls::TextBlock>();
    const auto date = Find(tray, L"DateInnerTextBlock").try_as<controls::TextBlock>();
    if (time)
    {
        Set(time, controls::TextBlock::FontWeightProperty(), winrt::Windows::UI::Text::FontWeight{600});
        Set(time, controls::TextBlock::FontSizeProperty(), 12.0);
        Set(time, controls::TextBlock::ForegroundProperty(), Solid(255, 246, 248, 252));
    }
    if (date)
    {
        Set(date, controls::TextBlock::FontSizeProperty(), 10.0);
        Set(date, controls::TextBlock::ForegroundProperty(), Solid(255, 185, 191, 205));
    }
    for (const auto name : {L"ControlCenterButton", L"NotificationCenterButton", L"ChevronIconView"})
    {
        if (const auto button = Find(tray, name).try_as<controls::Control>())
            Set(button, controls::Control::CornerRadiusProperty(), xaml::CornerRadius{8, 8, 8, 8});
    }
    const auto controlCenter = Find(tray, L"ControlCenterButton");
    if (!controlCenter)
        return;
    const auto findBattery = [&](auto&& self, xaml::DependencyObject const& item) -> FrameworkElement
    {
        if (winrt::get_class_name(item) == L"SystemTray.BatteryIconContent")
            return item.try_as<FrameworkElement>();
        const auto count = media::VisualTreeHelper::GetChildrenCount(item);
        for (int index = 0; index < count; ++index)
            if (auto result = self(self, media::VisualTreeHelper::GetChild(item, index)))
                return result;
        return nullptr;
    };
    const auto battery = findBattery(findBattery, controlCenter);
    SYSTEM_POWER_STATUS power{};
    if (!battery || !GetSystemPowerStatus(&power) || power.BatteryLifePercent > 100)
        return;
    const auto parent = media::VisualTreeHelper::GetParent(battery).try_as<controls::Grid>();
    if (!parent)
        return;
    batteryBadge = controls::Grid();
    batteryBadge.Name(L"WindowsDockBattery");
    batteryBadge.Width(17);
    batteryBadge.Height(9);
    batteryBadge.IsHitTestVisible(false);
    batteryBadge.HorizontalAlignment(xaml::HorizontalAlignment::Center);
    batteryBadge.VerticalAlignment(xaml::VerticalAlignment::Center);
    controls::Border outline;
    outline.BorderBrush(Solid(230, 235, 241, 250));
    outline.BorderThickness({1, 1, 1, 1});
    outline.CornerRadius({2.5, 2.5, 2.5, 2.5});
    outline.Margin({0, 0, 2, 0});
    batteryBadge.Children().Append(outline);
    controls::Border cap;
    cap.Width(1.5);
    cap.Height(4);
    cap.HorizontalAlignment(xaml::HorizontalAlignment::Right);
    cap.VerticalAlignment(xaml::VerticalAlignment::Center);
    cap.Background(Solid(225, 235, 241, 250));
    batteryBadge.Children().Append(cap);
    batteryFill = controls::Border();
    batteryFill.Margin({2, 2, 0, 2});
    batteryFill.HorizontalAlignment(xaml::HorizontalAlignment::Left);
    batteryFill.CornerRadius({1, 1, 1, 1});
    batteryBadge.Children().Append(batteryFill);
    batteryBolt = controls::TextBlock();
    batteryBolt.Text(L"\u26A1");
    batteryBolt.FontSize(7);
    batteryBolt.HorizontalAlignment(xaml::HorizontalAlignment::Center);
    batteryBolt.VerticalAlignment(xaml::VerticalAlignment::Center);
    batteryBadge.Children().Append(batteryBolt);
    controls::Grid::SetRow(batteryBadge, controls::Grid::GetRow(battery));
    controls::Grid::SetColumn(batteryBadge, controls::Grid::GetColumn(battery));
    parent.Children().Append(batteryBadge);
    Set(battery, xaml::UIElement::OpacityProperty(), 0.0);
    RefreshBattery();
}

void TaskbarStyle::RefreshBattery()
{
    if (!batteryBadge)
        return;
    const auto tick = GetTickCount();
    if (batteryRefreshed != 0 && tick - batteryRefreshed < 15000)
        return;
    batteryRefreshed = tick;
    SYSTEM_POWER_STATUS power{};
    if (!GetSystemPowerStatus(&power) || power.BatteryLifePercent > 100 ||
        (power.BatteryLifePercent == batteryLevel && power.BatteryFlag == batteryFlags))
        return;
    batteryLevel = power.BatteryLifePercent;
    batteryFlags = power.BatteryFlag;
    const auto charging = (batteryFlags & 8) != 0;
    batteryFill.Width(11.0 * batteryLevel / 100);
    batteryFill.Background(charging ? Solid(255, 115, 227, 174) : batteryLevel <= 20
        ? Solid(255, 251, 180, 101) : Solid(240, 221, 232, 245));
    batteryBolt.Visibility(charging ? xaml::Visibility::Visible : xaml::Visibility::Collapsed);
}

void TaskbarStyle::FitApps()
{
    if (const auto apps = appFrame.get())
    {
        const auto tray = trayFrame.get();
        const auto trayWidth = tray ? tray.ActualWidth() : 0;
        const auto controlsWidth = std::max(dockControls.ActualWidth(), agentTray ? 95.0 : 45.0);
        const auto maxWidth = std::max(120.0, root.ActualWidth() - trayWidth - controlsWidth - 38.0);
        if (apps.MaxWidth() != maxWidth)
            Set(apps, FrameworkElement::MaxWidthProperty(), maxWidth);
    }
}

void TaskbarStyle::StyleIndicator(FrameworkElement const& element)
{
    properties.erase(std::remove_if(properties.begin(), properties.end(),
        [](const Property& saved) { return !saved.object.get(); }), properties.end());
    const auto indicator = element.try_as<shapes::Rectangle>();
    if (!indicator)
        return;
    Set(indicator, FrameworkElement::WidthProperty(), 4.0);
    Set(indicator, FrameworkElement::HeightProperty(), 4.0);
    Set(indicator, shapes::Rectangle::RadiusXProperty(), 2.0);
    Set(indicator, shapes::Rectangle::RadiusYProperty(), 2.0);
}

void TaskbarStyle::Remember(xaml::DependencyObject const& object, xaml::DependencyProperty const& property)
{
    if (std::ranges::any_of(properties, [&](const Property& saved)
        { return saved.object.get() == object && saved.property == property; }))
        return;

    xaml::Data::Binding binding{nullptr};
    if (const auto element = object.try_as<FrameworkElement>())
        if (auto expression = element.GetBindingExpression(property))
            binding = expression.ParentBinding();
    properties.push_back({winrt::make_weak(object), property, object.ReadLocalValue(property), binding});
}

void TaskbarStyle::Restore() noexcept
{
    try
    {
        if (agentTimer)
        {
            agentTimer.Stop();
            agentTimer.Tick(agentTick);
            agentTimer = nullptr;
        }
        for (auto& animation : agentAnimations)
            if (animation)
                animation.Stop();
        if (rootSize.value)
            root.SizeChanged(rootSize);
        if (traySize.value)
            if (auto tray = trayFrame.get())
                tray.SizeChanged(traySize);
        rootSize = {};
        traySize = {};
        const auto children = root.Children();
        for (const auto& item : {chrome.as<xaml::UIElement>(), divider.as<xaml::UIElement>(),
            dockControls.as<xaml::UIElement>()})
        {
            uint32_t position = 0;
            if (item && children.IndexOf(item, position))
                children.RemoveAt(position);
        }
        if (batteryBadge)
        {
            if (const auto parent = media::VisualTreeHelper::GetParent(batteryBadge).try_as<Grid>())
            {
                uint32_t position = 0;
                if (parent.Children().IndexOf(batteryBadge, position))
                    parent.Children().RemoveAt(position);
            }
        }
    }
    catch (...) {}

    for (auto it = properties.rbegin(); it != properties.rend(); ++it)
    {
        try
        {
            if (auto object = it->object.get())
            {
                if (it->binding)
                    xaml::Data::BindingOperations::SetBinding(object, it->property, it->binding);
                else if (it->value == xaml::DependencyProperty::UnsetValue())
                    object.ClearValue(it->property);
                else
                    object.SetValue(it->property, it->value);
            }
        }
        catch (...) {}
    }
    properties.clear();

    if (columnsChanged)
    {
        try
        {
            const auto columns = root.ColumnDefinitions();
            columns.Clear();
            for (const auto& column : originalColumns)
                columns.Append(column);
        }
        catch (...) {}
        columnsChanged = false;
    }
}
