#include "Connection.h"
#include "AgentFaces.h"
#include <robuffer.h>
#include <winrt/base.h>
#undef GetCurrentTime
#include "TaskbarStyle.h"

#include <winrt/Windows.UI.Text.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Media.Imaging.h>
#include <array>
#include <charconv>
#include <cstring>
#include <cwchar>
#include <iterator>
#include <sstream>

namespace
{
    namespace motion = xaml::Media::Animation;
    enum AgentVisual : int32_t
    {
        Recent, Ready, Working, Approval, Finished, Failed, Interrupted, QuotaReached, Unassigned
    };

    xaml::Media::SolidColorBrush AgentBrush(uint8_t a, uint8_t r, uint8_t g, uint8_t b)
    {
        return xaml::Media::SolidColorBrush(winrt::Windows::UI::Color{a, r, g, b});
    }

    struct AgentMood
    {
        const wchar_t* label;
        const wchar_t* symbol;
    };

    constexpr AgentMood agentMoods[] =
    {
        {L"Recent", L"\u00B7\u00B7"}, {L"Ready", L"\u25CB"}, {L"Working", L"\u2022\u2022"},
        {L"Needs approval", L"!"}, {L"Reply ready", L"\u2713"}, {L"Failed", L"\u00D7"},
        {L"Interrupted", L"\u2016"}, {L"Limit reached", L"\u2212"}, {L"Unassigned", L"+"}
    };

    struct Sprite
    {
        uint32_t color{0xCAB4FF};
        std::array<std::string, 12> rows{};
    };

    auto LoadSprites()
    {
        constexpr std::array<std::string_view, 9> names{
            "Recent", "Ready", "Working", "Attention", "Finished", "Failed", "Interrupted", "QuotaReached", "Unassigned"
        };
        std::array<Sprite, names.size()> sprites;
        std::istringstream source{std::string(AgentFaceArt)};
        std::string name, color;
        for (size_t i = 0; i < names.size(); ++i)
        {
            auto& sprite = sprites[i];
            source >> name >> color;
            const auto parsed = std::from_chars(color.data(), color.data() + color.size(), sprite.color, 16);
            if (name != names[i] || parsed.ec != std::errc{} || parsed.ptr != color.data() + color.size())
                winrt::throw_hresult(E_INVALIDARG);
            for (auto& row : sprite.rows)
                if (!(source >> row) || row.size() != 16)
                    winrt::throw_hresult(E_INVALIDARG);
        }
        return sprites;
    }

    xaml::Media::ImageSource MakeAgentSprite(Sprite const& sprite, int scale)
    {
        const int width = 16 * scale;
        xaml::Media::Imaging::WriteableBitmap bitmap(width, 12 * scale);
        byte* buffer{};
        winrt::check_hresult(bitmap.PixelBuffer().as<::Windows::Storage::Streams::IBufferByteAccess>()->Buffer(&buffer));
        const auto tint = 0xFF000000u | sprite.color;
        auto pixels = reinterpret_cast<uint32_t*>(buffer);
        for (int y = 0; y < 12 * scale; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                const char pixel = sprite.rows[y / scale][x / scale];
                pixels[y * width + x] = pixel == '#' ? tint : pixel == 'o' ? 0xFF201C2E :
                    pixel == '+' ? 0xFFFFF1D9 : 0;
            }
        }
        bitmap.Invalidate();
        return bitmap;
    }

    void MoveAgentPart(motion::Storyboard const& storyboard, xaml::DependencyObject const& part,
        wchar_t const* property, double start, double end, int duration, bool repeat)
    {
        motion::DoubleAnimation animation;
        animation.From(start);
        animation.To(end);
        animation.Duration({std::chrono::milliseconds(duration), xaml::DurationType::TimeSpan});
        animation.AutoReverse(repeat);
        if (repeat)
            animation.RepeatBehavior({0, {}, motion::RepeatBehaviorType::Forever});
        else
        {
            motion::QuadraticEase ease;
            ease.EasingMode(motion::EasingMode::EaseOut);
            animation.EasingFunction(ease);
        }
        motion::Storyboard::SetTarget(animation, part);
        motion::Storyboard::SetTargetProperty(animation, property);
        storyboard.Children().Append(animation);
    }
}

extern "C" IMAGE_DOS_HEADER __ImageBase;

void TaskbarStyle::SetDashboardIcon()
{
    constexpr int size = 32;
    const auto module = reinterpret_cast<HINSTANCE>(&__ImageBase);
    const auto icon = static_cast<HICON>(LoadImageW(module, MAKEINTRESOURCEW(101), IMAGE_ICON, size, size, LR_SHARED));
    if (!icon)
    {
        controls::TextBlock fallback;
        fallback.Text(L"\u25a6");
        dashboardButton.Content(fallback);
        return;
    }

    BITMAPINFO info{};
    info.bmiHeader = {sizeof(BITMAPINFOHEADER), size, -size, 1, 32};
    void* pixels{};
    const auto dc = CreateCompatibleDC(nullptr);
    const auto dib = dc ? CreateDIBSection(dc, &info, DIB_RGB_COLORS, &pixels, nullptr, 0) : nullptr;
    if (!dib)
    {
        if (dc)
            DeleteDC(dc);
        return;
    }
    const auto previous = SelectObject(dc, dib);
    std::memset(pixels, 0, size * size * 4);
    DrawIconEx(dc, 0, 0, icon, size, size, 0, nullptr, DI_NORMAL);
    GdiFlush();
    std::array<uint32_t, size * size> buffer;
    std::memcpy(buffer.data(), pixels, sizeof(buffer));
    SelectObject(dc, previous);
    DeleteObject(dib);
    DeleteDC(dc);

    xaml::Media::Imaging::WriteableBitmap bitmap(size, size);
    byte* target{};
    winrt::check_hresult(bitmap.PixelBuffer().as<::Windows::Storage::Streams::IBufferByteAccess>()->Buffer(&target));
    std::memcpy(target, buffer.data(), sizeof(buffer));
    bitmap.Invalidate();
    controls::Image image;
    image.Source(bitmap);
    image.Width(size);
    image.Height(size);
    image.IsHitTestVisible(false);
    dashboardButton.Content(image);
}

void TaskbarStyle::AddAgentGrid()
{
    if (!agentEvent || !agentState)
        return;

    controls::Grid grid;
    grid.Margin({3, 0, 3, 0});
    grid.VerticalAlignment(xaml::VerticalAlignment::Center);
    grid.HorizontalAlignment(xaml::HorizontalAlignment::Center);
    grid.Name(L"WindowsDockCodingAgents");
    xaml::Automation::AutomationProperties::SetName(grid, L"Coding chat status grid");
    controls::RowDefinition row;
    row.Height({34, xaml::GridUnitType::Pixel});
    grid.RowDefinitions().Append(row);
    for (int index = 0; index < 4; ++index)
    {
        controls::ColumnDefinition column;
        column.Width({AgentSlotWidth, xaml::GridUnitType::Pixel});
        grid.ColumnDefinitions().Append(column);
    }

    for (int index = 0; index < 4; ++index)
    {
        controls::Button button;
        button.Name(L"WindowsDockAgentSlot" + std::to_wstring(index + 1));
        button.Width(AgentSlotWidth - 2);
        button.Height(32);
        button.MinWidth(0);
        button.MinHeight(0);
        button.Padding({0, 0, 0, 0});
        button.BorderThickness({0, 0, 0, 0});
        button.CornerRadius({6, 6, 6, 6});
        button.HorizontalAlignment(xaml::HorizontalAlignment::Center);
        button.VerticalAlignment(xaml::VerticalAlignment::Center);
        button.HorizontalContentAlignment(xaml::HorizontalAlignment::Center);
        button.VerticalContentAlignment(xaml::VerticalAlignment::Center);
        button.RequestedTheme(xaml::ElementTheme::Dark);
        controls::Grid character;
        character.IsHitTestVisible(false);
        character.RenderTransform(xaml::Media::TranslateTransform());
        controls::Image face;
        face.Width(20);
        face.Height(15);
        face.Stretch(xaml::Media::Stretch::Fill);
        character.Children().Append(face);
        controls::TextBlock symbol;
        symbol.FontSize(11);
        symbol.HorizontalAlignment(xaml::HorizontalAlignment::Center);
        symbol.VerticalAlignment(xaml::VerticalAlignment::Center);
        character.Children().Append(symbol);
        button.Content(character);
        controls::Grid::SetColumn(button, index);
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
        agentCharacters[index] = character;
        agentFaces[index] = face;
        agentSymbols[index] = symbol;
    }
    agentTray = controls::Border();
    agentTray.BorderThickness({1, 1, 1, 1});
    agentTray.CornerRadius({8, 8, 8, 8});
    agentTray.Margin({1, 0, 7, 1});
    agentTray.Child(grid);
    controls::Grid::SetColumn(agentTray, 1);
    dockControls.Children().Append(agentTray);
    RefreshAgentGrid();

    agentTimer = xaml::DispatcherTimer();
    agentTimer.Interval(std::chrono::milliseconds(750));
    agentTick = agentTimer.Tick([this](auto&&, auto&&)
    {
        try
        {
            RefreshAgentGrid();
            RefreshBattery();
            if (completionEvent && WaitForSingleObject(completionEvent.get(), 0) == WAIT_OBJECT_0)
                PulseCompletion();
        }
        catch (...) {}
    });
    agentTimer.Start();
}

void TaskbarStyle::RefreshAgentGrid()
{
    static const auto sprites = LoadSprites();
    const auto now = GetTickCount64();
    if (!agentAppearanceChecked || now - agentAppearanceChecked >= 2500)
    {
        agentAppearanceChecked = now;
        HIGHCONTRASTW contrast{sizeof(contrast), 0, nullptr};
        BOOL animations = TRUE;
        const auto highContrast = SystemParametersInfoW(SPI_GETHIGHCONTRAST, sizeof(contrast), &contrast, 0) &&
            (contrast.dwFlags & HCF_HIGHCONTRASTON);
        SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0, &animations, 0);
        const auto motion = !highContrast && animations;
        agentAppearanceDirty |= agentHighContrast != highContrast || agentMotion != motion;
        agentHighContrast = highContrast;
        agentMotion = motion;
    }
    const auto revision = InterlockedCompareExchange(&agentState->revision, 0, 0);
    if (revision & 1)
        return;
    if (revision == agentRevision && !agentAppearanceDirty)
        return;
    AgentGridTile next[4];
    std::memcpy(next, agentState->tiles, sizeof(next));
    if (revision != InterlockedCompareExchange(&agentState->revision, 0, 0))
        return;
    if (agentAppearanceDirty)
    {
        agentTray.BorderBrush(agentHighContrast ? nullptr : AgentBrush(31, 235, 232, 226));
        agentTray.Background(agentHighContrast ? nullptr : AgentBrush(65, 8, 8, 8));
    }
    const auto normalText = GetSysColor(COLOR_WINDOWTEXT);
    for (int index = 0; index < 4; ++index)
    {
        auto& tile = next[index];
        tile.id[std::size(tile.id) - 1] = L'\0';
        tile.title[std::size(tile.title) - 1] = L'\0';
        tile.step[std::size(tile.step) - 1] = L'\0';
        auto& previous = visibleTiles[index];
        if (!agentAppearanceDirty && agentRevision >= 0 && std::memcmp(&tile, &previous, sizeof(tile)) == 0)
            continue;
        const auto state = !tile.provider ? Unassigned :
            tile.state >= Recent && tile.state <= QuotaReached ? static_cast<AgentVisual>(tile.state) : Recent;
        const auto& mood = agentMoods[state];
        const auto& sprite = sprites[state];
        const auto r = static_cast<uint8_t>(sprite.color >> 16);
        const auto g = static_cast<uint8_t>(sprite.color >> 8);
        const auto b = static_cast<uint8_t>(sprite.color);
        const auto button = agentButtons[index];
        const auto face = agentFaces[index];
        const auto character = agentCharacters[index];
        const auto symbol = agentSymbols[index];
        if (!agentHighContrast && !agentSprites[state])
            agentSprites[state] = MakeAgentSprite(sprite, 2);
        const bool newFace = agentAppearanceDirty || agentRevision < 0 || tile.provider != previous.provider ||
            tile.state != previous.state || std::wcscmp(tile.id, previous.id) != 0;
        std::wstring name = L"Agent slot " + std::to_wstring(index + 1) + L": ";
        name += tile.provider ? std::wstring(mood.label) + L", " + tile.title :
            *tile.title ? tile.title : L"Unassigned";
        xaml::Automation::AutomationProperties::SetName(button, name);
        xaml::Automation::AutomationProperties::SetHelpText(button, tile.step);
        // Hovering a slot says exactly three things: which app, which chat, and how it is doing.
        std::wstring tip;
        if (tile.provider)
        {
            tip = tile.provider == 1 ? L"Claude Code" : L"Codex";
            tip += L"\n";
            tip += *tile.title ? tile.title : L"Untitled chat";
            tip += L"\n";
            tip += mood.label;
        }
        else
            tip = *tile.title ? std::wstring(tile.title) + L"\nWaiting for a chat" : std::wstring(L"Empty slot");
        controls::ToolTipService::SetToolTip(button, winrt::box_value(winrt::hstring(tip)));
        previous = tile;
        if (!newFace)
            continue;
        if (agentHighContrast)
        {
            button.ClearValue(controls::Control::BackgroundProperty());
            symbol.Foreground(AgentBrush(255, GetRValue(normalText), GetGValue(normalText), GetBValue(normalText)));
        }
        else
        {
            const bool prominent = state == Working || state == Approval || state == Finished ||
                state == Failed || state == QuotaReached;
            button.Background(AgentBrush(prominent ? 26 : 0, r, g, b));
            face.Source(agentSprites[state]);
        }
        symbol.Text(mood.symbol);
        symbol.Visibility(agentHighContrast ? xaml::Visibility::Visible : xaml::Visibility::Collapsed);
        face.Visibility(agentHighContrast ? xaml::Visibility::Collapsed : xaml::Visibility::Visible);
        if (agentAnimations[index])
        {
            agentAnimations[index].Stop();
            agentAnimations[index] = nullptr;
        }
        if (agentMotion)
        {
            motion::Storyboard storyboard;
            switch (state)
            {
            case Working:
                MoveAgentPart(storyboard, character, L"(UIElement.RenderTransform).(TranslateTransform.Y)", 0, -2.2, 620, true);
                break;
            case Approval:
                MoveAgentPart(storyboard, character, L"(UIElement.RenderTransform).(TranslateTransform.Y)", 0, -1.5, 440, true);
                break;
            case Finished:
                MoveAgentPart(storyboard, character, L"(UIElement.RenderTransform).(TranslateTransform.Y)", -4, 0, 260, false);
                break;
            case QuotaReached: MoveAgentPart(storyboard, character, L"Opacity", 0.62, 1, 900, true); break;
            default: MoveAgentPart(storyboard, character, L"Opacity", 0.45, 1, 180, false); break;
            }
            if (storyboard.Children().Size() != 0)
            {
                try
                {
                    storyboard.Begin();
                    agentAnimations[index] = storyboard;
                }
                catch (winrt::hresult_error const&) {}
            }
        }
    }
    agentRevision = revision;
    agentAppearanceDirty = false;
}
