#include "Connection.h"
#include <winrt/base.h>
#undef GetCurrentTime
#include "TaskbarStyle.h"

#include <winrt/Windows.UI.ViewManagement.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <cstring>
#include <cwchar>
#include <iterator>

namespace
{
    namespace motion = xaml::Media::Animation;
    enum AgentVisual : int32_t
    {
        Recent, Ready, Working, Approval, Finished, Failed, Interrupted, QuotaReached
    };

    xaml::Media::SolidColorBrush AgentBrush(uint8_t a, uint8_t r, uint8_t g, uint8_t b)
    {
        return xaml::Media::SolidColorBrush(winrt::Windows::UI::Color{a, r, g, b});
    }

    struct AgentLook
    {
        const wchar_t* label;
        const wchar_t* face;
        uint8_t r, g, b;
    };

    AgentLook TileLook(AgentGridTile const& tile)
    {
        if (!tile.provider)
            return {L"Unassigned", L"\u00B7", 116, 123, 139};
        switch (tile.state)
        {
        case Ready: return {L"Ready", L"\u2022\u2022", 190, 179, 249};
        case Working: return {L"Working", L"\u2022\u2022", 119, 190, 255};
        case Approval: return {L"Needs approval", L"!", 255, 195, 111};
        case Finished: return {L"Reply ready", L"\u2713", 121, 226, 182};
        case Failed: return {L"Failed", L"\u00D7", 255, 132, 149};
        case Interrupted: return {L"Interrupted", L"\u2212", 167, 175, 195};
        case QuotaReached: return {L"Limit reached", L"\u2212", 255, 126, 155};
        default: return {L"Recent", L"\u2022\u2022", 172, 183, 206};
        }
    }
}

void TaskbarStyle::AddAgentGrid()
{
    if (!agentEvent || !agentState)
        return;

    controls::Grid grid;
    grid.Margin({0, 0, 6, 1});
    grid.VerticalAlignment(xaml::VerticalAlignment::Center);
    grid.HorizontalAlignment(xaml::HorizontalAlignment::Center);
    grid.Name(L"WindowsDockCodingAgents");
    xaml::Automation::AutomationProperties::SetName(grid, L"Coding chat status grid");
    for (int index = 0; index < 2; ++index)
    {
        controls::RowDefinition row;
        row.Height({16, xaml::GridUnitType::Pixel});
        grid.RowDefinitions().Append(row);
        controls::ColumnDefinition column;
        column.Width({19, xaml::GridUnitType::Pixel});
        grid.ColumnDefinitions().Append(column);
    }

    for (int index = 0; index < 4; ++index)
    {
        controls::Button button;
        button.Name(L"WindowsDockAgentSlot" + std::to_wstring(index + 1));
        button.Width(17);
        button.Height(15);
        button.MinWidth(0);
        button.MinHeight(0);
        button.Padding({0, 0, 0, 0});
        button.BorderThickness({0, 0, 0, 0});
        button.CornerRadius({4, 4, 4, 4});
        button.HorizontalAlignment(xaml::HorizontalAlignment::Center);
        button.VerticalAlignment(xaml::VerticalAlignment::Center);
        button.HorizontalContentAlignment(xaml::HorizontalAlignment::Center);
        button.VerticalContentAlignment(xaml::VerticalAlignment::Center);
        button.RequestedTheme(xaml::ElementTheme::Dark);
        controls::TextBlock face;
        face.FontFamily(xaml::Media::FontFamily(L"Segoe UI Variable Text"));
        face.FontSize(10);
        face.IsHitTestVisible(false);
        face.VerticalAlignment(xaml::VerticalAlignment::Center);
        button.Content(face);
        controls::Grid::SetRow(button, index / 2);
        controls::Grid::SetColumn(button, index % 2);
        button.Click([this, index](auto&&, auto&&)
        {
            const auto& tile = visibleTiles[index];
            auto& selection = agentState->selection;
            InterlockedIncrement(&selection.revision);
            selection.provider = tile.provider;
            wcscpy_s(selection.id, tile.id);
            selection.slot = index;
            InterlockedIncrement(&selection.revision);
            SetEvent(agentEvent.get());
        });
        grid.Children().Append(button);
        agentButtons[index] = button;
        agentFaces[index] = face;
    }
    controls::Grid::SetColumn(grid, 1);
    dockControls.Children().Append(grid);
    RefreshAgentGrid();

    agentTimer = xaml::DispatcherTimer();
    agentTimer.Interval(std::chrono::milliseconds(750));
    agentTick = agentTimer.Tick([this](auto&&, auto&&)
    {
        try { RefreshAgentGrid(); RefreshBattery(); } catch (...) {}
    });
    agentTimer.Start();
}

void TaskbarStyle::RefreshAgentGrid()
{
    const auto revision = InterlockedCompareExchange(&agentState->revision, 0, 0);
    if ((revision & 1) || revision == agentRevision)
        return;
    AgentGridTile next[4];
    std::memcpy(next, agentState->tiles, sizeof(next));
    if (revision != InterlockedCompareExchange(&agentState->revision, 0, 0))
        return;
    const auto animate = winrt::Windows::UI::ViewManagement::UISettings().AnimationsEnabled();
    for (int index = 0; index < 4; ++index)
    {
        auto& tile = next[index];
        tile.id[std::size(tile.id) - 1] = L'\0';
        tile.title[std::size(tile.title) - 1] = L'\0';
        tile.step[std::size(tile.step) - 1] = L'\0';
        if (agentRevision >= 0 && std::memcmp(&tile, &visibleTiles[index], sizeof(tile)) == 0)
            continue;
        visibleTiles[index] = tile;
        const auto look = TileLook(tile);
        const auto button = agentButtons[index];
        const auto face = agentFaces[index];
        const auto provider = tile.provider == 1 ? L"Claude Code" : L"Codex";
        std::wstring name = L"Agent slot " + std::to_wstring(index + 1) + L": ";
        std::wstring tooltip;
        if (tile.provider)
        {
            name += std::wstring(look.label) + L", " + tile.title;
            tooltip = std::wstring(provider) + L"  \u00B7  " + look.label + L"\n" + tile.title;
            if (*tile.step)
                tooltip += L"\n" + std::wstring(tile.step);
        }
        else
        {
            name += *tile.title ? tile.title : L"Unassigned";
            tooltip = *tile.title ? std::wstring(tile.title) + L"\nWaiting for a matching chat" : L"Coding chats\nClick to assign this slot";
        }
        xaml::Automation::AutomationProperties::SetName(button, name);
        controls::ToolTipService::SetToolTip(button, winrt::box_value(tooltip));
        const auto alpha = static_cast<uint8_t>(tile.provider ? 52 : 23);
        button.Background(AgentBrush(alpha, look.r, look.g, look.b));
        face.Foreground(AgentBrush(255, look.r, look.g, look.b));
        face.Text(look.face);
        if (agentAnimations[index])
        {
            agentAnimations[index].Stop();
            agentAnimations[index] = nullptr;
        }
        if (animate && (tile.state == Working || tile.state == Approval || tile.state == QuotaReached || tile.state == Finished))
        {
            motion::Storyboard storyboard;
            motion::DoubleAnimation pulse;
            pulse.From(tile.state == Finished ? 0.42 : tile.state == QuotaReached ? 0.62 : 0.48);
            pulse.To(1.0);
            const auto duration = tile.state == Finished ? 300 : tile.state == Approval ? 470 :
                tile.state == QuotaReached ? 1050 : 760;
            pulse.Duration(xaml::Duration{std::chrono::milliseconds(duration), xaml::DurationType::TimeSpan});
            pulse.AutoReverse(tile.state != Finished);
            if (tile.state != Finished)
                pulse.RepeatBehavior({0, {}, motion::RepeatBehaviorType::Forever});
            motion::Storyboard::SetTarget(pulse, face);
            motion::Storyboard::SetTargetProperty(pulse, L"Opacity");
            storyboard.Children().Append(pulse);
            storyboard.Begin();
            agentAnimations[index] = storyboard;
        }
    }
    agentRevision = revision;
}
