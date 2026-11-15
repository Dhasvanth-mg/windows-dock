using System.IO;
using System.Text.Json;
using WindowsDock;
using Xunit;

namespace WindowsDock.Tests;

/// <summary>Runs against a throwaway data root so nothing touches the real %LOCALAPPDATA%\WindowsDock.</summary>
[Collection("DockData")]
public sealed class DockDataTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "windows-dock-tests-" + Guid.NewGuid().ToString("N"));

    public DockDataTests() => DockData.RootOverride = root;

    public void Dispose()
    {
        DockData.RootOverride = null;
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    [Fact]
    public void RoundTripsAndReportsMissingFilesAsDefault()
    {
        Assert.Null(DockData.Read<string[]>("missing.json"));
        DockData.Save("items.json", new[] { "a", "b" });
        Assert.Equal(new[] { "a", "b" }, DockData.Read<string[]>("items.json"));
    }

    [Fact]
    public void CorruptFileIsMovedAsideInsteadOfOverwritten()
    {
        var path = DockData.FilePath("reminders.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        Assert.Null(DockData.Read<string[]>("reminders.json"));

        Assert.False(File.Exists(path));
        var kept = Directory.GetFiles(Path.GetDirectoryName(path)!, "reminders.json.corrupt-*");
        Assert.Single(kept);
        Assert.Equal("{ not json", File.ReadAllText(kept[0]));
    }

    [Fact]
    public void UpdateKeepsTheCorruptOriginalAndStartsFromDefault()
    {
        var path = DockData.FilePath("focus.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "garbage");

        var next = DockData.Update<string[]>("focus.json", current => [.. current ?? [], "new"]);

        Assert.Equal(new[] { "new" }, next);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "focus.json.corrupt-*"));
    }

    [Fact]
    public void UpdateDoesNotOverwriteWhenTheFileCannotBeRead()
    {
        DockData.Save("locked.json", new[] { "keep" });
        var path = DockData.FilePath("locked.json");
        using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Throws<IOException>(() => DockData.Update<string[]>("locked.json", _ => ["replaced"]));

        hold.Dispose();
        Assert.Equal(new[] { "keep" }, DockData.Read<string[]>("locked.json"));
    }

    [Fact]
    public void KeepNewestDeletesOlderMatches()
    {
        Directory.CreateDirectory(root);
        for (var i = 0; i < 5; i++)
        {
            var file = Path.Combine(root, $"settings.json.windows-dock-{i}.bak");
            File.WriteAllText(file, "x");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-10 + i));
        }
        File.WriteAllText(Path.Combine(root, "unrelated.txt"), "x");

        DockData.KeepNewest(root, "settings.json.windows-dock-*.bak", 3);

        var left = Directory.GetFiles(root, "settings.json.windows-dock-*.bak").Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(new[] { "settings.json.windows-dock-2.bak", "settings.json.windows-dock-3.bak", "settings.json.windows-dock-4.bak" }, left);
        Assert.True(File.Exists(Path.Combine(root, "unrelated.txt")));
    }

    [Fact]
    public void LogWritesOneLineAndHidesMessagesUnlessAskedFor()
    {
        var failure = new InvalidOperationException("secret prompt text");
        DockLog.Write("test", "plain", failure);
        DockLog.Write("test", "detailed", failure, detail: true);

        var lines = File.ReadAllLines(DockLog.FilePath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("InvalidOperationException", lines[0]);
        Assert.DoesNotContain("secret prompt text", lines[0]);
        Assert.Contains("secret prompt text", lines[1]);
    }

    [Fact]
    public void LogRotatesWhenLarge()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DockLog.FilePath)!);
        File.WriteAllBytes(DockLog.FilePath, new byte[600 * 1024]);

        DockLog.Write("test", "after rotation");

        Assert.True(File.Exists(DockLog.FilePath + ".1"));
        Assert.Single(File.ReadAllLines(DockLog.FilePath));
    }
}

[CollectionDefinition("DockData", DisableParallelization = true)]
public sealed class DockDataCollection { }
