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
    [InlineData("calc 12 km to seconds")]
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
    }
}
