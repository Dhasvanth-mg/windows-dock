using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowAudioPresets()
    {
        var (window, content, _) = CreateDockPanel("Audio presets", "", 492, 0xE767, purple, widget: true);
        BuildPinnedAudioPresets(content);
        window.Show();
    }

    private void BuildPinnedAudioPresets(StackPanel content)
    {
        var list = (ReadJson<List<AudioPreset>>("audio-presets.json") ?? [])
            .Where(preset => preset is { Name.Length: > 0, DeviceId.Length: > 0, Apps: not null }).Take(12).ToArray();
        var choices = new ComboBox { DisplayMemberPath = nameof(AudioPreset.Label), Margin = new Thickness(0, 0, 0, 7) };
        AutomationProperties.SetName(choices, "Saved audio presets");
        content.Children.Add(choices);
        var name = Field("Preset name", 40);
        name.Tag = "Preset name";
        name.Margin = new Thickness(0, 9, 0, 5);
        var status = Text("", 11, subdued);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 6, 0, 0);
        var actions = new WrapPanel();
        var apply = AccentButton("Apply preset", 0xE73E, purple, () =>
        {
            if (choices.SelectedItem is not AudioPreset preset)
                return;
            try
            {
                status.Text = AudioPresets.Apply(preset);
                if (currentPage == DashboardPage.Media)
                    ShowPage(DashboardPage.Media);
            }
            catch (Exception error) when (error is COMException or InvalidCastException or UnauthorizedAccessException)
            {
                status.Text = "Windows could not finish applying this preset. Check the output and volumes in the switchboard.";
            }
        });
        apply.Margin = new Thickness(0, 0, 5, 0);
        actions.Children.Add(apply);
        var remove = ActionButton("Remove");
        remove.Click += (_, _) =>
        {
            if (choices.SelectedItem is AudioPreset preset)
                Save(list.Where(item => item != preset).ToArray(), null, $"Removed {preset.Name}.");
        };
        actions.Children.Add(remove);
        content.Children.Add(actions);
        var caption = Text("Save the current output, master volume, and available app levels.", 11, subdued);
        caption.Margin = new Thickness(0, 10, 0, 0);
        caption.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(caption);
        content.Children.Add(name);
        var save = AccentButton("Save current mix", 0xE74E, purple, () =>
        {
            var label = name.Text.Trim();
            if (label.Length == 0)
            {
                status.Text = "Enter a name for this preset.";
                name.Focus();
                return;
            }
            var others = list.Where(item => !item.Name.Equals(label, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (others.Length >= 12)
            {
                status.Text = "Remove a preset before adding another.";
                return;
            }
            try
            {
                var preset = AudioPresets.Capture(label);
                Save([preset, .. others], preset, $"Saved {preset.Name}.");
            }
            catch (Exception error) when (error is COMException or InvalidOperationException)
            {
                status.Text = "No active output was available to save.";
            }
        });
        save.HorizontalAlignment = HorizontalAlignment.Left;
        content.Children.Add(save);
        content.Children.Add(status);
        choices.SelectionChanged += (_, _) => { apply.IsEnabled = remove.IsEnabled = choices.SelectedItem != null; };
        Update(null);

        void Update(AudioPreset? selected)
        {
            choices.ItemsSource = list;
            choices.SelectedItem = selected ?? list.FirstOrDefault();
            choices.IsEnabled = apply.IsEnabled = remove.IsEnabled = list.Length > 0;
            status.Text = list.Length == 0 ? "No presets yet." : "";
        }

        void Save(AudioPreset[] next, AudioPreset? selected, string message)
        {
            try
            {
                DockData.Save("audio-presets.json", next);
                list = next;
                Update(selected);
                status.Text = message;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                status.Text = "Could not save audio presets.";
            }
        }
    }
}
