using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsDock;

internal static class WidgetCommand
{
    internal static async Task<string?> ReadAsync(string executable, string[] args, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        start.Environment["GH_PROMPT_DISABLED"] = "1";
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start);
            if (process == null)
                return null;
            var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            try
            {
                var output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                return process.ExitCode == 0 && output.Length <= 2_000_000 ? output : null;
            }
            finally
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
                try { await errors.ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or OperationCanceledException) { }
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return null; }
        catch (Exception error) when (error is IOException or Win32Exception or InvalidOperationException) { return null; }
    }
}
