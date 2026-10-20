using GitUi.Application.Services;
using GitUi.Domain.ValueObjects;
using Xunit;

namespace GitUi.Application.Tests;

public class RebaseTodoBuilderTests
{
    [Fact]
    public void Build_WritesOneLinePerStep_AndRewordAsPickPlusAmend()
    {
        var steps = new[]
        {
            new RebaseStep("aaa", RebaseAction.Pick),
            new RebaseStep("bbb", RebaseAction.Reword, "New message"),
            new RebaseStep("ccc", RebaseAction.Squash),
            new RebaseStep("ddd", RebaseAction.Fixup),
            new RebaseStep("eee", RebaseAction.Drop),
        };

        var todo = RebaseTodoBuilder.Build(steps, _ => @"C:\Temp\it's\msg.txt");

        Assert.Equal(
            "pick aaa\n" +
            "pick bbb\n" +
            "exec git commit --amend --allow-empty --no-verify -F 'C:/Temp/it'\\''s/msg.txt'\n" +
            "squash ccc\n" +
            "fixup ddd\n" +
            "drop eee\n",
            todo);
    }

    [Theory]
    [InlineData(RebaseAction.Squash)]
    [InlineData(RebaseAction.Fixup)]
    public void Validate_FirstKeptCommitSquashed_Refuses(RebaseAction action)
    {
        var steps = new[] { new RebaseStep("aaa", RebaseAction.Drop), new RebaseStep("bbb", action) };

        Assert.Throws<InvalidOperationException>(() => RebaseTodoBuilder.Validate(steps));
    }

    [Fact]
    public void Validate_AllDropped_Refuses()
    {
        Assert.Throws<InvalidOperationException>(() => RebaseTodoBuilder.Validate(new[] { new RebaseStep("aaa", RebaseAction.Drop) }));
    }

    [Fact]
    public void Validate_RewordWithoutMessage_Refuses()
    {
        Assert.Throws<InvalidOperationException>(() => RebaseTodoBuilder.Validate(new[] { new RebaseStep("aaa", RebaseAction.Reword, " ") }));
    }
}
