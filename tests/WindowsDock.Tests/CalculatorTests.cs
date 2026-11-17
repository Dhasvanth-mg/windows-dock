using Xunit;

namespace WindowsDock.Tests;

public sealed class CalculatorTests
{
    [Theory]
    [InlineData("25 * 18", "450")]
    [InlineData("20% of 150", "30")]
    [InlineData("150 + 12%", "168")]
    [InlineData("Increase 80 by 15%", "92")]
    [InlineData("What percentage is 45 of 180?", "25%")]
    [InlineData("3:5 of 240", "90 : 150")]
    [InlineData("5 km to miles", "3.106855961187 mi")]
    [InlineData("72 F to C", "22.222222222222 °C")]
    [InlineData("250 ml in cups", "1.056688209433 US cups")]
    [InlineData("2.5 hours in minutes", "150 min")]
    [InlineData("sqrt(144)", "12")]
    [InlineData("sin(30 degrees)", "0.5")]
    [InlineData("log10(1000)", "3")]
    [InlineData("15 choose 4", "1365")]
    [InlineData("Solve 2x + 5 = 17", "x = 6")]
    [InlineData("=3(x + 2) = 2x - 1", "x = -7")]
    [InlineData("1/3 + 1/6", "0.5")]
    [InlineData("0b100 + 0xF", "19")]
    [InlineData("=median(10, 1, 3, 12)", "6.5")]
    [InlineData("2^3^2", "512")]
    [InlineData("-2^2", "-4")]
    [InlineData("calc today + 2 weeks", "2026-11-30")]
    [InlineData("days between 2026-11-11 and today", "5 days")]
    public void EvaluatesWithoutAProvider(string input, string expected)
    {
        Assert.True(DockCalculator.TryEvaluate(input, out var calculation, out var error, new DateOnly(2026, 11, 16)), error);
        Assert.Equal(expected, calculation!.Result);
    }

    [Theory]
    [InlineData("56 kb to mb", "0.056 Mb")]
    [InlineData("56 KB to MB", "0.056 MB")]
    [InlineData("56 kb to MB", "0.007 MB")]
    [InlineData("32 MiB → KiB", "32768 KiB")]
    [InlineData("1 TiB to TB", "1.099511627776 TB")]
    [InlineData("100 Mbps to MB/s", "12.5 MB/s")]
    [InlineData("1 gigabyte per second to megabits per second", "8000 megabits per second")]
    [InlineData("convert 60 mph into m/s", "26.8224 m/s")]
    [InlineData("12 in in cm", "30.48 cm")]
    [InlineData("5'11\" to cm", "180.34 cm")]
    [InlineData("1 h 30 m to min", "90 min")]
    [InlineData("-1 ft 6 in to in", "-18 in")]
    [InlineData("1,500 m to km", "1.5 km")]
    [InlineData("(2 + 3) GB to MB", "5000 MB")]
    [InlineData("3/4 cup to tablespoons", "12 US tbsp")]
    [InlineData("1 g/cm³ to kg/m³", "1000 kg/m³")]
    [InlineData("1 N to kg·m/s²", "1 kg·m/s²")]
    [InlineData("1 Newton to kg m s⁻²", "1 kg m s⁻²")]
    [InlineData("10 square feet to square inches", "1440 square inches")]
    [InlineData("1 m ^ 2 to cm2", "10000 cm2")]
    [InlineData("1 MPa to bar", "10 bar")]
    [InlineData("1 MW to mW", "1000000000 mW")]
    [InlineData("300 K to C", "26.85 °C")]
    [InlineData("18 Δ°F to Δ°C", "10 Δ°C")]
    [InlineData("60 rpm to Hz", "1 Hz")]
    [InlineData("30 mpg to L / 100 km", "7.840486111111 L/100 km")]
    [InlineData("1 ppm to percent", "0.0001 %")]
    public void ConvertsDataAndCompoundUnits(string input, string expected)
    {
        Assert.True(DockCalculator.TryEvaluate(input, out var calculation, out var error), error);
        Assert.Equal(expected, calculation!.Result);
    }

    [Theory]
    [InlineData("calc 12 km to seconds")]
    [InlineData("calc 20 °C/s to Δ°F/s")]
    [InlineData("calc 1 m^200 to ft")]
    [InlineData("calc 10 min / to h")]
    [InlineData("calc 0 mpg to L/100 km")]
    [InlineData("=x*x = 5")]
    [InlineData("=3/0")]
    [InlineData("=sqrt(-1)")]
    [InlineData("=2*")]
    public void RejectsInvalidCalculations(string input)
    {
        Assert.False(DockCalculator.TryEvaluate(input, out var calculation, out var error));
        Assert.Null(calculation);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void OrdinarySearchKeepsItsOwnRoute()
    {
        Assert.False(DockCalculator.TryEvaluate("notes", out _, out var error));
        Assert.Null(error);
        Assert.False(DockCalculator.TryEvaluate("7 things to do", out _, out error));
        Assert.Null(error);
    }
}
