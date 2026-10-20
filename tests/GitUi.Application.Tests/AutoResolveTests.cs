using System.Text;
using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using NSubstitute;
using Xunit;

namespace GitUi.Application.Tests;

public class AutoResolveTests
{
    private readonly IGitRepositoryReader _reader = Substitute.For<IGitRepositoryReader>();
    private readonly IGitRepositoryWriter _writer = Substitute.For<IGitRepositoryWriter>();
    private readonly ConflictResolutionService _service;

    public AutoResolveTests() => _service = new ConflictResolutionService(_reader, _writer);

    private void Stages(byte[]? ancestor, byte[]? ours, byte[]? theirs, string workingFile)
    {
        _reader.GetConflictStageContentsAsync("/repo", "f.txt", Arg.Any<CancellationToken>()).Returns(new ConflictStageContents(ancestor, ours, theirs));
        _reader.ReadWorkingFileAsync("/repo", "f.txt", Arg.Any<CancellationToken>()).Returns(workingFile);
    }

    private static byte[] B(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public async Task ResolvableChanges_AreWrittenWithTheFilesLineEndingsAndMarkedResolved()
    {
        Stages(B("a\nb\nc\n"), B("A\nb\nc\n"), B("a\nb\nC\n"), "<<<<<<< HEAD\r\nA\r\n=======\r\n...");

        Assert.True(await _service.TryAutoResolveAsync("/repo", "f.txt"));

        await _writer.Received(1).WriteWorkingFileBytesAsync("/repo", "f.txt",
            Arg.Is<byte[]>(b => Encoding.UTF8.GetString(b) == "A\r\nb\r\nC\r\n"), Arg.Any<CancellationToken>());
        await _writer.Received(1).MarkResolvedAsync("/repo", "f.txt", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bom_IsKept()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        Stages(bom.Concat(B("a\nm\nb\n")).ToArray(), bom.Concat(B("A\nm\nb\n")).ToArray(), bom.Concat(B("a\nm\nB\n")).ToArray(), "x\n");

        Assert.True(await _service.TryAutoResolveAsync("/repo", "f.txt"));

        await _writer.Received(1).WriteWorkingFileBytesAsync("/repo", "f.txt",
            Arg.Is<byte[]>(b => b.Take(3).SequenceEqual(bom) && Encoding.UTF8.GetString(b.Skip(3).ToArray()) == "A\nm\nB\n"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrueConflict_LeavesTheFileAlone()
    {
        Stages(B("a\nb\n"), B("X\nb\n"), B("Y\nb\n"), "x\n");

        Assert.False(await _service.TryAutoResolveAsync("/repo", "f.txt"));

        await _writer.DidNotReceiveWithAnyArgs().WriteWorkingFileBytesAsync(default!, default!, default!, default);
        await _writer.DidNotReceiveWithAnyArgs().MarkResolvedAsync(default!, default!, default);
    }

    [Theory]
    [InlineData(true)]  // binary content
    [InlineData(false)] // not UTF-8 (Windows-1252 "é")
    public async Task BinaryOrNonUtf8_IsNotTouched(bool binary)
    {
        var odd = binary ? new byte[] { 1, 0, 2 } : new byte[] { (byte)'a', 0xE9, (byte)'\n' };
        Stages(B("a\n"), odd, B("b\n"), "x\n");

        Assert.False(await _service.TryAutoResolveAsync("/repo", "f.txt"));
    }

    [Fact]
    public async Task DeletedOnOneSide_IsNotTouched()
    {
        Stages(B("a\n"), null, B("b\n"), "x\n");

        Assert.False(await _service.TryAutoResolveAsync("/repo", "f.txt"));
    }
}
