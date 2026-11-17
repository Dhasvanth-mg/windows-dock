using System.IO;
using NAudio.Wave;
using Xunit;

namespace WindowsDock.Tests;

[Collection("DockData")]
public sealed class LocalProductivityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "windows-dock-local-" + Guid.NewGuid().ToString("N"));

    public LocalProductivityTests() => DockData.RootOverride = root;

    public void Dispose()
    {
        DockData.RootOverride = null;
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Fact]
    public void LocalEventsRoundTripAndDetectOverlap()
    {
        var id = Guid.NewGuid();
        var first = Assert.Single(DockEvents.Save(id, " Write report ", "2026-11-22", "14:00", "15:00"));
        Assert.Equal("Write report", first.Title);
        Assert.False(DockEvents.Overlaps(first, DockEvents.Load()));
        var second = Guid.NewGuid();
        var events = DockEvents.Save(second, "Meeting", "2026-11-22", "14:30", "14:45");
        Assert.True(DockEvents.Overlaps(first, events));
        Assert.Throws<ArgumentException>(() => DockEvents.Save(id, "Invalid", "2026-11-22", "16:00", "14:30"));

        var exported = Ical.Net.Calendar.Load(DockEvents.Export())!;
        Assert.Equal(2, exported.Events.Count);
        Assert.Equal(first.Start.UtcDateTime, exported.Events.Single(item => item.Summary == "Write report").DtStart!.AsUtc);
        DockEvents.Remove(second);
        Assert.Equal(id, Assert.Single(DockEvents.Load()).Id);
    }

    [Fact]
    public void ImportedVoiceMemoPersistsAndDeletesAudioWithItsMetadata()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Sample voice memo.wav");
        using (var writer = new WaveFileWriter(path, new WaveFormat(16000, 16, 1)))
            writer.Write(new byte[32000], 0, 32000);
        var memo = DockVoiceMemos.Import(path);
        Assert.Equal("Sample voice memo", memo.Title);
        Assert.Equal(TimeSpan.FromSeconds(1), memo.Duration);
        Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(DockVoiceMemos.PathFor(memo.Id)));
        DockVoiceMemos.Edit(memo.Id, "Renamed", "Words");
        Assert.Equal("Words", Assert.Single(DockVoiceMemos.Load()).Transcript);
        DockVoiceMemos.Remove(memo.Id);
        Assert.Empty(DockVoiceMemos.Load());
        Assert.False(File.Exists(DockVoiceMemos.PathFor(memo.Id)));
    }

    [Fact]
    public void IndexSearchIsOptInIncrementalAndRemovesFolderResults()
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "allowed")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(folder, "nested")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(root, "private")).FullName;
        var story = Path.Combine(nested, "story.md");
        File.WriteAllText(story, "A purple mystery unfolds.");
        File.WriteAllText(Path.Combine(outside, "excluded.txt"), "private mystery");
        File.WriteAllText(Path.Combine(folder, "expense-report.pdf"), "Name-only PDF");
        Assert.Empty(DockFileSearch.Search("mystery"));

        DockFileSearch.AddFolder(folder);
        Assert.Throws<ArgumentException>(() => DockFileSearch.AddFolder(nested));
        Assert.Equal(2, DockFileSearch.Update(folder, CancellationToken.None).Count);
        var found = Assert.Single(DockFileSearch.Search("purple myst"));
        Assert.Equal(story, found.Path);
        Assert.Contains("purple mystery", found.Snippet);
        Assert.Empty(DockFileSearch.Search("private"));
        Assert.Single(DockFileSearch.Search("expense report"));

        File.WriteAllText(story, "Completely different terms");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => DockFileSearch.Update(folder, canceled.Token));
        Assert.Single(DockFileSearch.Search("purple"));
        DockFileSearch.Update(folder, CancellationToken.None);
        Assert.Empty(DockFileSearch.Search("purple"));
        Assert.Single(DockFileSearch.Search("different"));
        DockFileSearch.RemoveFolder(folder);
        Assert.Empty(DockFileSearch.Search("different"));
        Assert.Empty(DockFileSearch.Folders());
    }

    [Fact]
    public void WidgetLayoutRoundTripPreservesFavoriteOrderAndRejectsInvalidLayouts()
    {
        DockWidgetLayout.MoveFavorite("notifications", -1);
        DockWidgetLayout.Pin("voice");
        var original = DockWidgetLayout.Load();
        var path = Path.Combine(root, "profile.json");
        File.WriteAllText(path, DockWidgetLayout.Export());
        DockWidgetLayout.SetFavorite("activity", false);

        var imported = DockWidgetLayout.Import(path);
        Assert.Equal("voice", imported.Pinned);
        Assert.Equal(new[] { "focus", "notifications", "assistant", "activity" }, imported.Favorites);
        Assert.Equal(original.Favorites, DockWidgetLayout.Load().Favorites);
        File.WriteAllText(path, """{"Version":1,"Layout":{"Pinned":"file-search","Favorites":["file-search"]}}""");
        Assert.Throws<ArgumentException>(() => DockWidgetLayout.Import(path));
        Assert.Equal("voice", DockWidgetLayout.Load().Pinned);
    }
}
