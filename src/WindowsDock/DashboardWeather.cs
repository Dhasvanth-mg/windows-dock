using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Windows.Devices.Geolocation;

namespace WindowsDock;

public partial class DashboardWindow
{
    private static readonly HttpClient WeatherClient = new() { Timeout = TimeSpan.FromSeconds(12) };
    private Forecast? weather;
    private City? city;
    private bool loadingWeather;
    private string? weatherError;
    private City[] cityMatches = [];
    private string cityQuery = "";
    private bool weatherLocationDenied;
    private DateTimeOffset weatherUpdated;

    private UIElement WeatherPage()
    {
        var layout = new StackPanel();
        var toolbar = new DockPanel();
        var detect = AccentButton("Use my location", 0xE707, accent, () => _ = LocateWeatherAsync());
        detect.Margin = new Thickness(6, 0, 0, 0);
        detect.IsEnabled = !loadingWeather;
        detect.ToolTip = "Uses Windows location and sends rounded coordinates to Open-Meteo.";
        DockPanel.SetDock(detect, Dock.Right);
        toolbar.Children.Add(detect);
        var search = Field("City name", 100);
        search.Text = cityQuery;
        search.ToolTip = "Enter a city name";
        search.Tag = "Search for a city";
        var status = Text(weatherError ?? (loadingWeather ? "Loading weather…" : ""), 11, subdued);
        var submit = AccentButton("Find city", 0xE721, purple, async () =>
        {
            if (loadingWeather || string.IsNullOrWhiteSpace(search.Text))
                return;
            cityQuery = search.Text.Trim();
            loadingWeather = true;
            weatherError = null;
            status.Text = "Finding cities…";
            try
            {
                using var data = await WeatherClient.GetFromJsonAsync<JsonDocument>(
                    "https://geocoding-api.open-meteo.com/v1/search?count=5&language=en&name=" + Uri.EscapeDataString(cityQuery),
                    lifetime.Token);
                cityMatches = data?.RootElement.TryGetProperty("results", out var results) == true
                    ? results.EnumerateArray().Select(CityFromJson).ToArray() : [];
                if (cityMatches.Length == 0)
                    weatherError = "No matching cities.";
                else if (cityMatches.Length == 1)
                    await SetCityAsync(cityMatches[0]);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException or KeyNotFoundException)
            {
                weatherError = "City search is unavailable.";
            }
            finally
            {
                loadingWeather = false;
                if (!closed && currentPage == DashboardPage.Weather)
                    ShowPage(DashboardPage.Weather);
            }
        });
        DockPanel.SetDock(submit, Dock.Right);
        search.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        submit.Margin = new Thickness(6, 0, 0, 0);
        toolbar.Children.Add(submit);
        toolbar.Children.Add(search);
        var searchCard = Card(toolbar);
        searchCard.Padding = new Thickness(8);
        searchCard.Margin = new Thickness(0, 0, 0, 7);
        layout.Children.Add(searchCard);
        if (loadingWeather || weatherError != null)
        {
            status.Margin = new Thickness(0, 0, 0, 6);
            status.TextWrapping = TextWrapping.Wrap;
            layout.Children.Add(status);
        }
        if (weatherLocationDenied)
        {
            var settings = ActionButton("Windows location settings", () => OpenTarget("ms-settings:privacy-location"));
            settings.HorizontalAlignment = HorizontalAlignment.Left;
            settings.Margin = new Thickness(0, 0, 0, 7);
            layout.Children.Add(settings);
        }
        if (cityMatches.Length > 1)
        {
            var matches = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
            foreach (var match in cityMatches)
            {
                var button = ActionButton(match.Label, async () =>
                {
                    if (loadingWeather)
                        return;
                    cityMatches = [];
                    loadingWeather = true;
                    ShowPage(DashboardPage.Weather);
                    await SetCityAsync(match);
                    loadingWeather = false;
                    if (!closed && currentPage == DashboardPage.Weather)
                        ShowPage(DashboardPage.Weather);
                });
                button.Margin = new Thickness(0, 0, 4, 3);
                matches.Children.Add(button);
            }
            layout.Children.Add(matches);
        }

        if (weather is not { } forecast)
        {
            layout.Children.Add(Card(CardHeading("Set a forecast location", 0xE753, accent,
                detail: "Use Windows location for automatic updates, or choose a city."), accent));
            return layout;
        }

        var row = WideDashboard ? Columns(1, 2.7) : Columns(1);
        var current = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var (icon, tint) = WeatherStyle(forecast.Code);
        current.Children.Add(CardHeading(forecast.City, icon, tint, detail: WeatherLabel(forecast.Code)));
        current.Children.Add(Text($"{forecast.Temperature:0}°F", 33, strong: true));
        var credit = ActionButton("Open-Meteo", () => OpenTarget("https://open-meteo.com/"));
        credit.Padding = new Thickness(7, 4, 7, 4);
        credit.HorizontalAlignment = HorizontalAlignment.Left;
        credit.Margin = new Thickness(0, 5, 0, 0);
        current.Children.Add(credit);
        Put(row, Card(current, tint), 0);
        var upcoming = new System.Windows.Controls.Primitives.UniformGrid { Columns = Math.Max(1, Math.Min(5, forecast.Days.Length)) };
        foreach (var day in forecast.Days)
        {
            var (dailyIcon, dailyTint) = WeatherStyle(day.Code);
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 3, 2, 3) };
            var badge = GlyphBadge(dailyIcon, dailyTint, 31);
            badge.HorizontalAlignment = HorizontalAlignment.Center;
            badge.Margin = new Thickness(0, 4, 0, 5);
            stack.Children.Add(Text(day.Date.ToString("ddd"), 11, subdued, true));
            stack.Children.Add(badge);
            stack.Children.Add(Text($"{day.High:0}° / {day.Low:0}°", 14, strong: true));
            stack.Children.Add(Text(WeatherLabel(day.Code), 10, muted));
            foreach (var label in stack.Children.OfType<TextBlock>())
                label.TextAlignment = TextAlignment.Center;
            upcoming.Children.Add(stack);
        }
        Put(row, Card(upcoming, accent), WideDashboard ? 0 : 1, WideDashboard ? 1 : 0);
        layout.Children.Add(row);
        return layout;
    }

    private (int Icon, SolidColorBrush Tint) WeatherStyle(int code) => code switch
    {
        0 or 1 => (0xE706, amber),
        2 or 3 or 45 or 48 => (0xE753, subdued),
        >= 71 and <= 77 or 85 or 86 => (0xE9CA, accent),
        >= 95 => (0xE945, amber),
        _ => (0xE753, accent)
    };

    private async Task SetCityAsync(City selected)
    {
        weatherError = null;
        try
        {
            var url = FormattableString.Invariant(
                $"https://api.open-meteo.com/v1/forecast?latitude={selected.Latitude}&longitude={selected.Longitude}&current=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&temperature_unit=fahrenheit&forecast_days=5");
            using var data = await WeatherClient.GetFromJsonAsync<JsonDocument>(url, lifetime.Token);
            var current = data!.RootElement.GetProperty("current");
            var daily = data.RootElement.GetProperty("daily");
            var dates = daily.GetProperty("time");
            var days = new WeatherDay[dates.GetArrayLength()];
            for (var i = 0; i < days.Length; i++)
            {
                days[i] = new WeatherDay(DateTime.ParseExact(dates[i].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    daily.GetProperty("temperature_2m_max")[i].GetDouble(),
                    daily.GetProperty("temperature_2m_min")[i].GetDouble(),
                    daily.GetProperty("weather_code")[i].GetInt32());
            }
            weather = new Forecast(selected.Name, current.GetProperty("temperature_2m").GetDouble(),
                current.GetProperty("weather_code").GetInt32(), days);
            weatherUpdated = DateTimeOffset.UtcNow;
            if (city != selected)
            {
                city = selected;
                WriteJson("weather-city.json", selected.Automatic ? selected with { Latitude = 0, Longitude = 0 } : selected);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException
            or KeyNotFoundException or FormatException)
        {
            weatherError = "Forecast unavailable. Try again.";
        }
    }

    private void LoadSavedWeather() => city = ReadJson<City>("weather-city.json");

    private async Task RefreshSavedWeatherAsync()
    {
        if (city == null || loadingWeather || DateTimeOffset.UtcNow - weatherUpdated < TimeSpan.FromMinutes(20))
            return;
        loadingWeather = true;
        weatherUpdated = DateTimeOffset.UtcNow;
        if (city.Automatic)
        {
            if (await DetectWeatherCityAsync() is { } current)
                await SetCityAsync(current);
            else if (weather == null)
                weatherError = "Current location is unavailable. Search for a city or try location again.";
        }
        else
            await SetCityAsync(city);
        loadingWeather = false;
        if (!closed && IsVisible && (currentPage is DashboardPage.Weather or DashboardPage.Home))
            ShowPage(currentPage);
    }

    private async Task LocateWeatherAsync()
    {
        if (loadingWeather)
            return;
        loadingWeather = true;
        weatherError = null;
        weatherLocationDenied = false;
        cityMatches = [];
        var wasModal = modalOpen;
        ShowPage(DashboardPage.Weather);
        try
        {
            modalOpen = true;
            if (await DetectWeatherCityAsync() is { } current)
                await SetCityAsync(current);
            else
                weatherError = "Windows could not locate this device. Check location access or search for a city.";
        }
        finally
        {
            modalOpen = wasModal;
            loadingWeather = false;
            if (!closed && currentPage == DashboardPage.Weather && IsVisible)
                ShowPage(DashboardPage.Weather);
        }
    }

    private async Task<City?> DetectWeatherCityAsync()
    {
        try
        {
            var access = await Geolocator.RequestAccessAsync().AsTask(lifetime.Token);
            weatherLocationDenied = access == GeolocationAccessStatus.Denied;
            if (access != GeolocationAccessStatus.Allowed)
                return null;
            var locator = new Geolocator { DesiredAccuracyInMeters = 5000 };
            var position = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(10)).AsTask(lifetime.Token);
            var coordinates = position.Coordinate.Point.Position;
            return new City("Current location", null, null, Math.Round(coordinates.Latitude, 2),
                Math.Round(coordinates.Longitude, 2), Automatic: true);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or
            TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            weatherLocationDenied = error is UnauthorizedAccessException;
            return null;
        }
    }

    private static City CityFromJson(JsonElement result) => new(result.GetProperty("name").GetString()!,
        result.TryGetProperty("admin1", out var region) ? region.GetString() : null,
        result.TryGetProperty("country", out var country) ? country.GetString() : null,
        result.GetProperty("latitude").GetDouble(), result.GetProperty("longitude").GetDouble());

    private static string WeatherLabel(int code) => code switch
    {
        0 => "Clear", 1 => "Mostly clear", 2 => "Partly cloudy", 3 => "Cloudy",
        45 or 48 => "Fog", >= 51 and <= 57 => "Drizzle",
        >= 71 and <= 77 or 85 or 86 => "Snow", >= 95 => "Storm", _ => "Rain"
    };

    private sealed record City(string Name, string? Region, string? Country, double Latitude, double Longitude, bool Automatic = false)
    {
        public string Label => string.Join(", ", new[] { Name, Region, Country }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private sealed record WeatherDay(DateTime Date, double High, double Low, int Code);
    private sealed record Forecast(string City, double Temperature, int Code, WeatherDay[] Days);
}
