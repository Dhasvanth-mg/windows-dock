using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WindowsDock;

internal sealed record CalculationRecord(DockCalculation Calculation, bool Favorite);

internal static class CalculationHistory
{
    private const string FileName = "calculations.json";
    internal static CalculationRecord[] Read() => DockData.Read<CalculationRecord[]>(FileName) ?? [];

    internal static void Save(DockCalculation calculation, bool? favorite = null) =>
        DockData.Update<CalculationRecord[]>(FileName, saved =>
        {
            var previous = saved?.FirstOrDefault(item => item.Calculation.Input == calculation.Input);
            var remaining = (saved ?? []).Where(item => item.Calculation.Input != calculation.Input).ToArray();
            var keep = favorite ?? previous?.Favorite ?? false;
            if (keep && remaining.Count(item => item.Favorite) >= 40)
                throw new InvalidOperationException("Remove a favorite before saving more than 40.");
            var updated = remaining.Prepend(new CalculationRecord(calculation, keep));
            return updated.Where(item => item.Favorite).Concat(updated.Where(item => !item.Favorite).Take(60)).ToArray();
        });

    internal static void ClearRecent() => DockData.Update<CalculationRecord[]>(FileName,
        saved => saved?.Where(item => item.Favorite).ToArray() ?? []);
}

public partial class DashboardWindow
{
    private static bool CopyCalculation(DockCalculation calculation, TextBlock status)
    {
        try { Clipboard.SetText(calculation.Result); }
        catch (ExternalException)
        {
            status.Text = "The clipboard is busy.";
            return false;
        }
        try { CalculationHistory.Save(calculation); status.Text = "Result copied."; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { status.Text = "Result copied. Windows Dock could not save it to history."; }
        return true;
    }

    private static bool SaveCalculation(DockCalculation calculation, bool favorite, TextBlock status)
    {
        try
        {
            CalculationHistory.Save(calculation, favorite);
            status.Text = favorite ? "Saved to favorites." : "Removed from favorites.";
            return true;
        }
        catch (InvalidOperationException error) { status.Text = error.Message; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { status.Text = "Windows Dock could not save this calculation."; }
        return false;
    }

    private void ShowCalculator()
    {
        var (window, content, _) = CreateDockPanel("Calculator", "", 472, 0xE8EF, accent, widget: true);
        BuildCalculator(content);
        window.Show();
    }

    private void BuildCalculator(StackPanel content)
    {
        var input = Field("Calculation", 405);
        input.Tag = "Try 20% of 150 or 5 km to miles";
        var output = Text("", 17, AssistantInk, true);
        output.TextWrapping = TextWrapping.Wrap;
        output.Margin = new Thickness(0, 7, 0, 3);
        var status = Text("", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        DockCalculation? current = null;
        var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        var copy = ActionButton("Copy result");
        copy.Margin = new Thickness(0, 0, 5, 0);
        var save = ActionButton("Save favorite");
        buttons.Children.Add(copy);
        buttons.Children.Add(save);
        var history = new StackPanel();
        var clear = ActionButton("Clear recent");
        copy.Click += (_, _) => Copy();
        save.Click += (_, _) =>
        {
            if (current != null && SaveCalculation(current, true, status))
                RefreshHistory();
        };
        clear.Click += (_, _) =>
        {
            try
            {
                CalculationHistory.ClearRecent();
                RefreshHistory();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { status.Text = "Windows Dock could not clear history."; }
        };
        clear.Margin = new Thickness(0, 8, 0, 0);
        clear.HorizontalAlignment = HorizontalAlignment.Left;
        content.Children.Add(input);
        content.Children.Add(output);
        content.Children.Add(status);
        content.Children.Add(buttons);
        content.Children.Add(Text("History and favorites", 12, AssistantSecondary, true));
        content.Children.Add(history);
        content.Children.Add(clear);
        input.TextChanged += (_, _) => Evaluate();
        input.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && current != null)
            {
                args.Handled = true;
                Copy();
            }
        };
        Evaluate();
        RefreshHistory();

        void Evaluate()
        {
            DockCalculator.TryEvaluate("=" + input.Text, out current, out var error);
            output.Text = current?.Result ?? "";
            status.Text = current?.Detail ?? (input.Text.Length > 0 ? error ?? "" : "");
            copy.IsEnabled = save.IsEnabled = current != null;
        }

        void Copy()
        {
            if (current != null && CopyCalculation(current, status))
                RefreshHistory();
        }

        void RefreshHistory()
        {
            history.Children.Clear();
            var records = CalculationHistory.Read();
            clear.IsEnabled = records.Any(item => !item.Favorite);
            foreach (var record in records.Take(15))
            {
                var bar = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
                var star = IconButton(record.Favorite ? 0xE735 : 0xE734,
                    record.Favorite ? "Remove calculation favorite" : "Favorite calculation", () =>
                    {
                        if (SaveCalculation(record.Calculation, !record.Favorite, status))
                            RefreshHistory();
                    });
                DockPanel.SetDock(star, Dock.Right);
                bar.Children.Add(star);
                var result = ActionButton($"{record.Calculation.Input} = {record.Calculation.Result}", () =>
                {
                    input.Text = record.Calculation.Input;
                    Copy();
                });
                result.HorizontalAlignment = HorizontalAlignment.Stretch;
                bar.Children.Add(result);
                history.Children.Add(bar);
            }
            if (records.Length == 0)
                history.Children.Add(Text("Copied results appear here.", 11, AssistantSecondary));
        }
    }
}
