using System.IO;
using System.Text.Json;
using Xunit;

namespace WindowsDock.Tests;

[Collection("DockData")]
public sealed class DockChatsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "windows-dock-chat-test-" + Guid.NewGuid().ToString("N"));

    public DockChatsTests() => DockData.RootOverride = Path.Combine(root, "data");

    public void Dispose()
    {
        DockData.RootOverride = null;
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Fact]
    public void ConversationAttachmentAndOutputsMoveTogether()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "reference.png");
        File.WriteAllBytes(path, [137, 80, 78, 71, 13, 10, 26, 10, 1, 2]);
        var picked = ChatAttachments.Read(path);
        var id = Guid.NewGuid();
        var turns = Enumerable.Range(0, 140).Select(i => new SavedTurn(i % 2 == 0, $"Message {i}")).ToArray();
        turns[0] = turns[0] with { Files = [picked.File] };
        DockChats.Save(new(id, "research", DockChats.Title(turns), DateTimeOffset.UtcNow, turns, AssistantProvider.Gemini, "model"), [picked]);
        var original = DockChats.SaveFile(id, "report.md", "first version");
        var second = DockChats.SaveFile(id, "report.md", "second version");
        Assert.Equal("report-2.md", Path.GetFileName(second));
        Assert.Throws<ArgumentException>(() => DockChats.SaveFile(id, "../outside.txt", "invalid"));
        Assert.Throws<ArgumentException>(() => DockChats.SaveFile(id, "CON.txt", "invalid"));

        DockChats.ChooseFolder(Path.Combine(root, "chosen folder"));
        var moved = Assert.Single(DockChats.Load());
        Assert.Equal(turns.Length, moved.Turns.Length);
        Assert.Equal(turns[0].Text, moved.Title);
        Assert.Equal("model", moved.Model);
        Assert.Equal(picked.Bytes, DockChats.ReadMedia(id, Assert.Single(moved.Turns[0].Files!)).Bytes);
        var movedOutput = Path.Combine(DockChats.Root, id.ToString("N"), "files");
        Assert.Equal("first version", File.ReadAllText(Path.Combine(movedOutput, "report.md")));
        Assert.Equal("second version", File.ReadAllText(Path.Combine(movedOutput, "report-2.md")));
        Assert.True(File.Exists(original));
    }

    [Fact]
    public void AFailedFolderChangeKeepsTheCurrentLibrary()
    {
        var id = Guid.NewGuid();
        DockChats.Save(new(id, "research", "Saved", DateTimeOffset.UtcNow, [new(true, "Saved")]));
        var current = DockChats.Root;
        var file = Path.Combine(root, "existing");
        File.WriteAllText(file, "existing");
        Assert.ThrowsAny<IOException>(() => DockChats.ChooseFolder(file));
        Assert.Equal(current, DockChats.Root);
        Assert.Equal(id, Assert.Single(DockChats.Load()).Id);
    }

    [Fact]
    public void OrganizingAndBranchingKeepTheOriginalConversationAndAttachment()
    {
        var id = Guid.NewGuid();
        var original = new SavedChat(id, "research", "Before", DateTimeOffset.UtcNow,
            [new(true, "first"), new(false, "answer"), new(true, "follow up")], AssistantProvider.Ollama, "model");
        var attachment = new PickedChatFile(new(Guid.NewGuid(), "note.txt", null, "data"), "data"u8.ToArray());
        original.Turns[0] = original.Turns[0] with { Files = [attachment.File] };
        DockChats.Save(original, [attachment]);
        var organized = DockChats.Organize(id, "Renamed", " Course  work ", true);
        Assert.Equal("Course work", organized.Folder);
        Assert.True(organized.Pinned);

        var branch = DockChats.Branch(id, 1);
        var library = DockChats.Load();
        Assert.Equal(id, library[0].Id);
        Assert.Equal("Renamed", library[0].Title);
        Assert.Equal(3, library[0].Turns.Length);
        Assert.Equal(branch.Id, library[1].Id);
        Assert.Equal("Course work", branch.Folder);
        Assert.Equal("model", branch.Model);
        Assert.False(branch.Pinned);
        Assert.Equal(attachment.Bytes, DockChats.ReadAttachment(branch.Id, Assert.Single(Assert.Single(branch.Turns).Files!)).Bytes);
        Assert.Empty(DockChats.Branch(id, 0).Turns);
        Assert.Equal(2, DockChats.Load().Length);
    }

    [Theory]
    [InlineData("ChatGptPlan", "input_image", "input_file")]
    [InlineData("ClaudeApi", "image", "document")]
    [InlineData("Gemini", "image/png", "application/pdf")]
    public void ImageAndPdfUseTheProviderContentFormat(string source, string imageMarker, string pdfMarker)
    {
        var provider = Enum.Parse<AssistantProvider>(source);
        var json = JsonSerializer.Serialize(ChatMediaRequest.Content(provider, "Look at these",
            [new("image.png", "image/png", [1]), new("source.pdf", "application/pdf", [2])]));
        Assert.Contains(imageMarker, json);
        Assert.Contains(pdfMarker, json);
        Assert.Contains("Look at these", json);
        Assert.Contains("AQ==", json);
        Assert.Contains("Ag==", json);
    }

    [Fact]
    public void MarkdownPreservesCodeAndConfinesSuggestedNames()
    {
        var fence = new string((char)96, 3);
        var blocks = ChatMarkdown.Blocks($"## Summary\n\n**Done**\n\n{fence}python filename=report.py\nprint(1)\n{fence}");
        var code = Assert.Single(blocks, block => block.Kind == MdKind.Code);
        Assert.Equal("print(1)", code.Text);
        Assert.Equal("report.py", ChatMarkdown.SuggestedName(code.Label, 2, 1));
        Assert.Equal("reply-2-file-1.py", ChatMarkdown.SuggestedName("python filename=../escape.py", 2, 1));
        Assert.Null(ChatMarkdown.WebLink("file:///C:/Windows"));
    }

    [Fact]
    public void ChatsCanBeRenamedHiddenAndDeleted()
    {
        var id = Guid.NewGuid();
        SavedTurn[] turns = [new(true, "Plan my week"), new(false, "Sure.")];
        DockChats.Save(new(id, "planner", DockChats.Title(turns), DateTimeOffset.UtcNow, turns, Pinned: true));

        var renamed = DockChats.Organize(id, "Week plan", "", true);
        Assert.Equal("Week plan", renamed.Title);

        var hidden = DockChats.SetHidden(id, true);
        Assert.True(hidden.Hidden);
        Assert.False(hidden.Pinned);
        Assert.Equal("Week plan", Assert.Single(DockChats.Load()).Title);
        Assert.True(DockChats.Organize(id, "Week plan v2", "", false).Hidden);
        Assert.False(DockChats.SetHidden(id, false).Hidden);

        DockChats.Delete(id, recycle: false);
        Assert.Empty(DockChats.Load());
        DockChats.Delete(id, recycle: false);
    }
}
