#include "Connection.h"
#undef GetCurrentTime
#include "TaskbarStyle.h"

#include <winrt/Windows.UI.Text.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Controls.Primitives.h>
#include <winrt/Windows.UI.Xaml.Data.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>
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
            {0.0, {105, 255, 255, 255}}, {0.42, {37, 255, 255, 255}}, {1.0, {68, 255, 255, 255}}})
        {
            media::GradientStop stop;
            stop.Offset(position);
            stop.Color(color);
            brush.GradientStops().Append(stop);
        }
        return brush;
    }
}

TaskbarStyle::TaskbarStyle(Grid const& root, winrt::handle const& dashboard)
    : root(root), dashboard(dashboard.get())
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
    chrome.CornerRadius({14, 14, 14, 14});
    chrome.Margin({0, 2, 0, 4});
    chrome.BorderThickness({1, 1, 1, 1});
    chrome.BorderBrush(Rim());
    media::AcrylicBrush glass;
    glass.BackgroundSource(media::AcrylicBackgroundSource::Backdrop);
    glass.TintColor(Color{255, 10, 11, 13});
    glass.TintOpacity(0.78);
    glass.TintLuminosityOpacity(0.10);
    glass.FallbackColor(Color{246, 14, 15, 17});
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
    divider.Background(Solid(50, 255, 255, 255));
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
    dashboardButton.Background(Solid(29, 255, 255, 255));
    dashboardButton.BorderThickness({0, 0, 0, 0});
    dashboardButton.CornerRadius({8, 8, 8, 8});
    dashboardButton.HorizontalAlignment(xaml::HorizontalAlignment::Center);
    dashboardButton.VerticalAlignment(xaml::VerticalAlignment::Center);
    dashboardButton.RequestedTheme(xaml::ElementTheme::Dark);
    xaml::Automation::AutomationProperties::SetName(dashboardButton, L"Dock dashboard");
    controls::ToolTipService::SetToolTip(dashboardButton, winrt::box_value(L"Dock dashboard"));

    controls::FontIcon icon;
    icon.FontFamily(media::FontFamily(L"Segoe Fluent Icons"));
    icon.Glyph(L"\uE80A");
    icon.FontSize(14);
    icon.Foreground(Solid(242, 255, 255, 255));
    dashboardButton.Content(icon);
    dashboardButton.Click([event = dashboard](auto&&, auto&&) { SetEvent(event); });
    Grid::SetColumn(dashboardButton, 2);
    root.Children().Append(dashboardButton);
}

void TaskbarStyle::FitApps()
{
    if (const auto apps = appFrame.get())
    {
        const auto tray = trayFrame.get();
        const auto trayWidth = tray ? tray.ActualWidth() : 0;
        const auto maxWidth = std::max(120.0, root.ActualWidth() - trayWidth - 88.0);
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
        if (rootSize.value)
            root.SizeChanged(rootSize);
        if (traySize.value)
            if (auto tray = trayFrame.get())
                tray.SizeChanged(traySize);
        rootSize = {};
        traySize = {};
        const auto children = root.Children();
        for (const auto& item : {chrome.as<xaml::UIElement>(), divider.as<xaml::UIElement>(),
            dashboardButton.as<xaml::UIElement>()})
        {
            uint32_t position = 0;
            if (item && children.IndexOf(item, position))
                children.RemoveAt(position);
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
