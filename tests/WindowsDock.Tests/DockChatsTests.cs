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
}
