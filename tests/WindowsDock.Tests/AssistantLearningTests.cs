using WindowsDock;
using Xunit;

namespace WindowsDock.Tests;

public sealed class AssistantLearningTests
{
    [Fact]
    public void ExtractRemovesTheNoteFromTheVisibleReply()
    {
        var (reply, notes) = AssistantLearning.Extract("Sure, metric it is.\n\nREMEMBER: Prefers metric units.");
        Assert.Equal("Sure, metric it is.", reply);
        Assert.Equal(["Prefers metric units."], notes);
    }

    [Fact]
    public void ExtractToleratesMarkdownAroundTheMarker()
    {
        var (reply, notes) = AssistantLearning.Extract("Noted.\n- **remember:** Works night shifts.\n");
        Assert.Equal("Noted.", reply);
        Assert.Equal(["Works night shifts."], notes);
    }

    [Theory]
    [InlineData("REMEMBER: My password is hunter2")]
    [InlineData("REMEMBER: API key sk-abcdefghijklmnopqrstuvwxyz123456")]
    [InlineData("REMEMBER: Card 4111 1111 1111 1111")]
    [InlineData("REMEMBER: ok")]
    public void ExtractDropsSecretsAndScrapsButStillHidesTheLine(string line)
    {
        var (reply, notes) = AssistantLearning.Extract("Done.\n" + line);
        Assert.Equal("Done.", reply);
        Assert.Empty(notes);
    }

    [Fact]
    public void ExtractKeepsAtMostTwoNotesAndLeavesOrdinaryTextAlone()
    {
        var text = "Remember to drink water.\nREMEMBER: Likes tea.\nREMEMBER: Lives in Leeds.\nREMEMBER: Has a cat.";
        var (reply, notes) = AssistantLearning.Extract(text);
        Assert.Equal("Remember to drink water.", reply);
        Assert.Equal(["Likes tea.", "Lives in Leeds."], notes);
    }

    [Fact]
    public void HidePartialHidesTheMarkerWhileItIsStillBeingTyped()
    {
        Assert.Equal("Sure.", AssistantLearning.HidePartial("Sure.\nREM"));
        Assert.Equal("Sure.", AssistantLearning.HidePartial("Sure.\nREMEMBER: Prefers"));
        Assert.Equal("Sure.\nRemembering", AssistantLearning.HidePartial("Sure.\nRemembering"));
        Assert.Equal("Sure.\nRe", AssistantLearning.HidePartial("Sure.\nRe"));
    }

    [Fact]
    public void MergeAppendsSkipsRepeatsAndReportsWhatWasAdded()
    {
        var (memory, added) = AssistantLearning.Merge("Call me Dee.\n- Prefers metric units.",
            ["prefers metric units.", "Uses Linux at work."]);
        Assert.Equal("Call me Dee.\n- Prefers metric units.\n- Uses Linux at work.", memory);
        Assert.Equal(["Uses Linux at work."], added);
    }

    [Fact]
    public void MergeDropsTheOldestLearnedLinesButNeverYourOwnWords()
    {
        var own = new string('a', 1600);
        var learned = string.Join("\n", Enumerable.Range(1, 10).Select(index => $"- Learned fact number {index:00} about the person."));
        var (memory, added) = AssistantLearning.Merge(own + "\n" + learned, ["Brand new fact."]);
        Assert.True(memory.Length <= AssistantLearning.MemoryLimit);
        Assert.StartsWith(own, memory);
        Assert.EndsWith("- Brand new fact.", memory);
        Assert.DoesNotContain("number 01", memory);
        Assert.Equal(["Brand new fact."], added);
    }

    [Fact]
    public void MergeLearnsNothingWhenYourOwnWordsFillTheMemory()
    {
        var own = new string('a', AssistantLearning.MemoryLimit);
        var (memory, added) = AssistantLearning.Merge(own, ["Something new."]);
        Assert.Equal(own, memory);
        Assert.Empty(added);
    }
}
