using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowVoiceMemos()
    {
        var (window, content, cancellation) = CreateDockPanel("Voice memos", "", 540, 0xE720, accent, widget: true);
        BuildVoiceMemos(window, content, cancellation);
        window.Show();
    }

    private void BuildVoiceMemos(Window window, StackPanel content, CancellationToken cancellation)
    {
        var record = ActionButton("Record");
        var stop = ActionButton("Stop recording");
        stop.IsEnabled = false;
        var import = ActionButton("Import WAV");
        var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 6), Children = { record, stop, import } };
        var status = Text("Record a memo or import a WAV file.", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        var search = Field("Search voice memo titles and transcripts", 80);
        search.Tag = "Search voice memos";
        var list = new StackPanel();
        var details = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var player = new MediaPlayer();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        DateTimeOffset recordingStarted = default;
        WaveInEvent? capture = null;
        VoiceMemo[] memos = DockVoiceMemos.Load();
        foreach (var element in new UIElement[] { controls, status, search, details, list })
            content.Children.Add(element);
        search.TextChanged += (_, _) => Render();
        player.MediaFailed += (_, _) => status.Text = "Windows could not play this recording.";
        timer.Tick += (_, _) =>
        {
            var elapsed = DateTimeOffset.Now - recordingStarted;
            status.Text = "Recording · " + elapsed.ToString(@"mm\:ss");
            if (elapsed >= TimeSpan.FromMinutes(10))
                StopRecording();
        };
        record.Click += (_, _) => StartRecording();
        stop.Click += (_, _) => StopRecording();
        import.Click += async (_, _) =>
        {
            var picker = new OpenFileDialog { Filter = "WAV audio|*.wav", CheckFileExists = true };
            if (picker.ShowDialog(window) != true)
                return;
            controls.IsEnabled = false;
            try
            {
                var memo = await Task.Run(() => DockVoiceMemos.Import(picker.FileName), cancellation);
                if (!cancellation.IsCancellationRequested)
                {
                    Reload();
                    OpenMemo(memo);
                    status.Text = "Memo imported.";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidDataException)
            { status.Text = "Could not import this WAV recording."; }
            finally { controls.IsEnabled = true; }
        };
        cancellation.Register(() => Dispatcher.InvokeAsync(() =>
        {
            timer.Stop();
            StopRecording();
            player.Close();
        }));
        Render();

        void StartRecording()
        {
            if (capture != null || cancellation.IsCancellationRequested)
                return;
            player.Close();
            var id = Guid.NewGuid();
            var path = DockVoiceMemos.PathFor(id, pending: true);
            WaveInEvent? device = null;
            WaveFileWriter? writer = null;
            Exception? writeFailure = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                device = new WaveInEvent { DeviceNumber = -1, WaveFormat = new WaveFormat(16000, 16, 1) };
                writer = new WaveFileWriter(path, device.WaveFormat);
                device.DataAvailable += (sender, args) =>
                {
                    try { writer.Write(args.Buffer, 0, args.BytesRecorded); }
                    catch (IOException error) { writeFailure = error; device.StopRecording(); }
                };
                device.RecordingStopped += (_, args) =>
                {
                    try
                    {
                        writer.Dispose();
                        if (args.Exception != null || writeFailure != null)
                            throw new IOException("The microphone stopped unexpectedly.");
                        var memo = DockVoiceMemos.Commit(id, "Memo " + recordingStarted.ToLocalTime().ToString("g"));
                        if (!cancellation.IsCancellationRequested)
                        {
                            Reload();
                            OpenMemo(memo);
                            status.Text = "Recording saved.";
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidDataException)
                    {
                        status.Text = "Could not save this recording.";
                        try { File.Delete(path); }
                        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
                    }
                    finally
                    {
                        device.Dispose();
                        capture = null;
                        timer.Stop();
                        record.IsEnabled = import.IsEnabled = true;
                        stop.IsEnabled = false;
                    }
                };
                recordingStarted = DateTimeOffset.Now;
                device.StartRecording();
                capture = device;
                record.IsEnabled = import.IsEnabled = false;
                stop.IsEnabled = true;
                status.Text = "Recording · 00:00";
                timer.Start();
            }
            catch (Exception error) when (error is NAudio.MmException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                writer?.Dispose();
                device?.Dispose();
                try { File.Delete(path); }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
                status.Text = "Could not open the microphone. Check Windows microphone access.";
            }
        }

        void StopRecording()
        {
            if (capture == null || !stop.IsEnabled)
                return;
            stop.IsEnabled = false;
            status.Text = "Saving recording…";
            capture.StopRecording();
        }

        void Reload()
        {
            memos = DockVoiceMemos.Load();
            Render();
        }

        void Render()
        {
            list.Children.Clear();
            var query = search.Text.Trim();
            foreach (var memo in memos.Where(item => item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Transcript?.Contains(query, StringComparison.OrdinalIgnoreCase) == true).Take(50))
            {
                var label = memo.Title + " · " + memo.Duration.ToString(@"mm\:ss");
                var open = ActionButton(label, () => OpenMemo(memo));
                open.HorizontalAlignment = HorizontalAlignment.Left;
                open.Margin = new Thickness(0, 3, 0, 0);
                list.Children.Add(open);
            }
            if (list.Children.Count == 0)
                list.Children.Add(Text(query.Length == 0 ? "No saved memos." : "No matching memos.", 11, AssistantSecondary));
        }

        void OpenMemo(VoiceMemo memo)
        {
            var name = Field("Memo title", 120);
            name.Text = memo.Title;
            var transcript = Field("Memo transcript", 24_000);
            transcript.Text = memo.Transcript ?? "";
            transcript.AcceptsReturn = true;
            transcript.TextWrapping = TextWrapping.Wrap;
            transcript.MaxHeight = 90;
            transcript.Tag = "Transcript";
            var play = ActionButton("Play", () =>
            {
                if (capture != null)
                    return;
                player.Open(new Uri(DockVoiceMemos.PathFor(memo.Id)));
                player.Play();
            });
            var pause = ActionButton("Pause", player.Pause);
            var save = ActionButton("Save memo details", () => SaveMetadata());
            var transcribe = ActionButton("Transcribe with Groq");
            var use = ActionButton("Use transcript in Orbit", () =>
            {
                var draft = transcript.Text.Trim();
                if (draft.Length == 0)
                    return;
                if (!TryOpenOrbitWithDraft(draft))
                    status.Text = "Shorten the transcript or your existing Orbit draft before sharing.";
            });
            var export = ActionButton("Export WAV", () =>
            {
                var picker = new SaveFileDialog { Filter = "WAV audio|*.wav", FileName = "Voice memo.wav" };
                if (picker.ShowDialog(window) != true)
                    return;
                try { File.Copy(DockVoiceMemos.PathFor(memo.Id), picker.FileName, true); status.Text = "Memo exported."; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { status.Text = "Could not export this memo."; }
            });
            var remove = ActionButton("Delete memo", () =>
            {
                if (MessageBox.Show(DialogOwner(window), "Delete this recording and transcript?", "Voice memos",
                    MessageBoxButton.YesNo, MessageBoxImage.None, MessageBoxResult.No) != MessageBoxResult.Yes)
                    return;
                try
                {
                    player.Close();
                    DockVoiceMemos.Remove(memo.Id);
                    details.Children.Clear();
                    Reload();
                    status.Text = "Memo deleted.";
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { status.Text = "Could not delete this memo."; }
            });
            var actions = new WrapPanel { Margin = new Thickness(0, 5, 0, 0),
                Children = { play, pause, save, transcribe, use, export, remove } };
            var editor = new StackPanel { Children = { name, transcript, actions } };
            details.Children.Clear();
            details.Children.Add(Card(editor));
            transcribe.Click += async (_, _) =>
            {
                var index = Array.FindIndex(AssistantSources, source => source.Provider == AssistantProvider.Groq);
                if (AssistantKey(index) is not { Length: > 0 } key)
                {
                    status.Text = "Set a Groq API key in Assistants to transcribe this memo.";
                    return;
                }
                if (MessageBox.Show(DialogOwner(window), "Send this recording to Groq for transcription using your API account?",
                    "Transcribe memo", MessageBoxButton.YesNo, MessageBoxImage.None, MessageBoxResult.No) != MessageBoxResult.Yes)
                    return;
                editor.IsEnabled = false;
                list.IsEnabled = false;
                try
                {
                    status.Text = "Transcribing…";
                    var result = await DockVoiceMemos.TranscribeAsync(memo.Id, key, cancellation);
                    if (!cancellation.IsCancellationRequested)
                    {
                        transcript.Text = result;
                        SaveMetadata();
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch (Exception error) when (error is OperationCanceledException or HttpRequestException or IOException or
                    UnauthorizedAccessException or System.Text.Json.JsonException or FormatException or InvalidDataException)
                { status.Text = "Could not transcribe this memo."; }
                finally { editor.IsEnabled = list.IsEnabled = true; }
            };

            void SaveMetadata()
            {
                try
                {
                    var value = transcript.Text.Trim();
                    DockVoiceMemos.Edit(memo.Id, name.Text, value.Length == 0 ? null : value);
                    Reload();
                    status.Text = "Memo saved.";
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { status.Text = "Could not save this memo."; }
            }
        }
    }
}
