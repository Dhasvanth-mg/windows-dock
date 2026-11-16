using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    private bool weatherCelsius;
    private DateTimeOffset weatherUpdated;

    // Weather has no page of its own: the forecast sits on Today and the location settings sit in Controls.
    // Each lives in a host that is refilled in place, so a refresh never rebuilds the page around it.
    private ContentControl? weatherGlanceHost;
    private ContentControl? weatherSettingsHost;

    private void RefreshWeatherViews()
    {
        if (closed)
            return;
        if (weatherGlanceHost != null)
            weatherGlanceHost.Content = WeatherGlanceContent();
        if (weatherSettingsHost != null)
            weatherSettingsHost.Content = WeatherSettingsContent();
    }

    private FrameworkElement WeatherGlance()
    {
        weatherGlanceHost = new ContentControl { Content = WeatherGlanceContent(), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Focusable = false };
        return weatherGlanceHost;
    }

    private FrameworkElement WeatherGlanceContent()
    {
        var panel = new StackPanel();
        if (weather is not { } forecast)
        {
            var waiting = new DockPanel();
            var set = QuietButton(city == null ? "Set a location" : "Location", () => ShowPage(DashboardPage.Controls));
            DockPanel.SetDock(set, Dock.Right);
            waiting.Children.Add(set);
            var note = Text(loadingWeather ? "Looking outside…" : weatherError != null ? "No forecast right now" : "Weather", 12, AssistantSecondary);
            note.VerticalAlignment = VerticalAlignment.Center;
            waiting.Children.Add(note);
            panel.Children.Add(waiting);
            return panel;
        }

        var now = new DockPanel();
        var place = Text(forecast.City, 11, AssistantFaint);
        place.VerticalAlignment = VerticalAlignment.Center;
        place.MaxWidth = 110;
        DockPanel.SetDock(place, Dock.Right);
        now.Children.Add(place);
        var glyph = DockGlyph(WeatherStyle(forecast.Code).Icon, 17, forecast.Code is 0 or 1 ? amber : AssistantSecondary);
        glyph.Margin = new Thickness(0, 0, 9, 0);
        now.Children.Add(glyph);
        var reading = Reading(DisplayTemperature(forecast.Temperature), WeatherLabel(forecast.Code), 15);
        reading.VerticalAlignment = VerticalAlignment.Center;
        now.Children.Add(reading);
        var current = PlainButton(now, $"{DisplayTemperature(forecast.Temperature)}, {WeatherLabel(forecast.Code)}, {forecast.City}. Change location",
            () => ShowPage(DashboardPage.Controls));
        current.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        current.ToolTip = "Change the location or units";
        panel.Children.Add(current);

        var days = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var day in forecast.Days.Take(5))
        {
            var column = new StackPanel();
            var name = Text(day.Date.Date == DateTime.Today ? "Today" : day.Date.ToString("ddd"), 11, AssistantSecondary);
            var icon = DockGlyph(WeatherStyle(day.Code).Icon, 13, day.Code is 0 or 1 ? amber : AssistantSecondary);
            icon.Margin = new Thickness(0, 4, 0, 4);
            var range = Reading(DisplayDegrees(day.High), "", 12);
            var low = Text(DisplayDegrees(day.Low), 11, AssistantFaint);
            foreach (var part in new[] { name, icon, range, low })
            {
                part.HorizontalAlignment = HorizontalAlignment.Center;
                column.Children.Add(part);
            }
            column.ToolTip = $"{day.Date:dddd}: {WeatherLabel(day.Code)}, {DisplayTemperature(day.High)} / {DisplayTemperature(day.Low)}";
            System.Windows.Automation.AutomationProperties.SetName(column, (string)column.ToolTip);
            days.Children.Add(column);
        }
        panel.Children.Add(days);
        return panel;
    }

    private FrameworkElement WeatherSettings()
    {
        weatherSettingsHost = new ContentControl { Content = WeatherSettingsContent(), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Focusable = false };
        return weatherSettingsHost;
    }

    private FrameworkElement WeatherSettingsContent()
    {
        var layout = new StackPanel();
        var units = Segmented("Temperature unit", ["°F", "°C"], weatherCelsius ? 1 : 0, index =>
        {
            if (index == 1 == weatherCelsius)
                return;
            try
            {
                DockData.Save("weather-celsius.json", !weatherCelsius);
                weatherCelsius = !weatherCelsius;
            }
            catch (Exception error) when (DockActions.IsActionError(error)) { ShowOwnedMessage(error.Message); }
            RefreshWeatherViews();
        });
        var where = weather != null ? $"{weather.City}, {DisplayTemperature(weather.Temperature)} and {WeatherLabel(weather.Code).ToLowerInvariant()}"
            : city?.Label ?? "No location yet";
        layout.Children.Add(SettingRow("Weather", where, units, last: true));

        var finder = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var detect = QuietButton("Use my location", () => _ = LocateWeatherAsync());
        detect.IsEnabled = !loadingWeather;
        detect.ToolTip = "Uses Windows location and sends rounded coordinates to Open-Meteo.";
        ToolTipService.SetIsEnabled(detect, true);
        detect.VerticalAlignment = VerticalAlignment.Center;
        detect.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(detect, Dock.Right);
        finder.Children.Add(detect);
        var search = Field("City name", 100);
        search.Text = cityQuery;
        search.Tag = "Search for a city";
        var submit = ActionButton("Find", async () =>
        {
            if (loadingWeather || string.IsNullOrWhiteSpace(search.Text))
                return;
            cityQuery = search.Text.Trim();
            loadingWeather = true;
            weatherError = null;
            RefreshWeatherViews();
            try
            {
                using var data = await WeatherClient.GetFromJsonAsync<JsonDocument>(
                    "https://geocoding-api.open-meteo.com/v1/search?count=5&language=en&name=" + Uri.EscapeDataString(cityQuery),
                    lifetime.Token);
                cityMatches = data?.RootElement.TryGetProperty("results", out var results) == true
                    ? results.EnumerateArray().Select(CityFromJson).ToArray() : [];
                if (cityMatches.Length == 0)
                    weatherError = "No city by that name. Try a different spelling.";
                else if (cityMatches.Length == 1)
                    await SetCityAsync(cityMatches[0]);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException or KeyNotFoundException)
            {
                weatherError = "City search is not responding. Try again in a moment.";
            }
            finally
            {
                loadingWeather = false;
                RefreshWeatherViews();
            }
        });
        System.Windows.Automation.AutomationProperties.SetName(submit, "Find city");
        submit.IsEnabled = !loadingWeather;
        submit.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(submit, Dock.Right);
        finder.Children.Add(submit);
        search.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        finder.Children.Add(search);
        layout.Children.Add(finder);

        if (loadingWeather || weatherError != null)
        {
            var status = Text(weatherError ?? "Looking that up…", 12, subdued);
            status.Margin = new Thickness(0, 0, 0, 6);
            status.TextWrapping = TextWrapping.Wrap;
            layout.Children.Add(status);
        }
        if (weatherLocationDenied)
        {
            var settings = ActionButton("Open location settings", () => OpenTarget("ms-settings:privacy-location"));
            settings.HorizontalAlignment = HorizontalAlignment.Left;
            settings.Margin = new Thickness(0, 0, 0, 6);
            layout.Children.Add(settings);
        }
        if (cityMatches.Length > 1)
        {
            var matches = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            foreach (var match in cityMatches)
            {
                var button = ActionButton(match.Label, async () =>
                {
                    if (loadingWeather)
                        return;
                    cityMatches = [];
                    loadingWeather = true;
                    RefreshWeatherViews();
                    await SetCityAsync(match);
                    loadingWeather = false;
                    RefreshWeatherViews();
                });
                button.Margin = new Thickness(0, 0, 6, 6);
                matches.Children.Add(button);
            }
            layout.Children.Add(matches);
        }
        var credit = QuietButton("Forecast from Open-Meteo", () => OpenTarget("https://open-meteo.com/"));
        credit.HorizontalAlignment = HorizontalAlignment.Left;
        layout.Children.Add(credit);
        layout.Children.Add(Rule(6));
        return layout;
    }

    private string DisplayDegrees(double fahrenheit) => weatherCelsius ? $"{(fahrenheit - 32) * 5 / 9:0}°" : $"{fahrenheit:0}°";

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
            if (city != selected)
            {
                city = selected;
                weather = null;
                WriteJson("weather-city.json", selected);
            }
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
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException
            or KeyNotFoundException or FormatException)
        {
            weatherError = "Forecast unavailable. Try again.";
        }
    }

    private void LoadSavedWeather()
    {
        city = ReadJson<City>("weather-city.json");
        weatherCelsius = ReadJson<bool>("weather-celsius.json");
    }

    private string DisplayTemperature(double fahrenheit) =>
        weatherCelsius ? $"{(fahrenheit - 32) * 5 / 9:0}°C" : $"{fahrenheit:0}°F";

    private async Task RefreshSavedWeatherAsync()
    {
        if (loadingWeather || DateTimeOffset.UtcNow - weatherUpdated < TimeSpan.FromMinutes(20))
            return;
        loadingWeather = true;
        weatherUpdated = DateTimeOffset.UtcNow;
        if (city == null || city is { Automatic: true, Latitude: 0, Longitude: 0 })
        {
            if (await DetectWeatherCityAsync() is { } current)
                await SetCityAsync(current);
            else
                weatherError = "Windows location is unavailable. Choose a city or allow location in Windows.";
        }
        else
            await SetCityAsync(city);
        loadingWeather = false;
        RefreshWeatherViews();
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
        RefreshWeatherViews();
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
            RefreshWeatherViews();
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
