using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

/// <summary>Loads a conflicted file for the merge tool and applies the user's resolution.</summary>
public sealed class ConflictResolutionService
{
    private readonly IGitRepositoryReader _reader;
    private readonly IGitRepositoryWriter _writer;

    public ConflictResolutionService(IGitRepositoryReader reader, IGitRepositoryWriter writer)
    {
        _reader = reader;
        _writer = writer;
    }

    public async Task<(ConflictVersions Versions, ConflictDocument? Document)> LoadAsync(string path, string filePath, CancellationToken cancellationToken = default)
    {
        var versions = await _reader.GetConflictVersionsAsync(path, filePath, cancellationToken);

        // A modify/delete conflict has no markers to pick lines from: keep or delete the whole file.
        if (versions.IsDeleteConflict)
        {
            return (versions, null);
        }

        var text = await _reader.ReadWorkingFileAsync(path, filePath, cancellationToken);
        return (versions, ConflictDocument.Parse(text));
    }

    /// <summary>Writes the merged output and, unless <paramref name="markResolved"/> is false, marks the file resolved.</summary>
    public async Task SaveResolutionAsync(string path, string filePath, string content, bool markResolved = true, CancellationToken cancellationToken = default)
    {
        await _writer.WriteWorkingFileAsync(path, filePath, content, cancellationToken);
        if (markResolved)
        {
            await _writer.MarkResolvedAsync(path, filePath, cancellationToken);
        }
    }

    /// <summary>Resolves the whole file with one side; a side where the file was deleted resolves by deleting it.</summary>
    public async Task TakeSideAsync(string path, string filePath, ConflictSide side, CancellationToken cancellationToken = default)
    {
        var versions = await _reader.GetConflictVersionsAsync(path, filePath, cancellationToken);
        var sideExists = side == ConflictSide.Ours ? versions.HasOurs : versions.HasTheirs;
        if (sideExists)
        {
            await _writer.TakeConflictSideAsync(path, filePath, side, cancellationToken);
        }
        else
        {
            await _writer.RemoveFileAsync(path, filePath, cancellationToken);
        }
    }

    private static readonly System.Text.UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// GitKraken's automatic resolution: merges the file's three index stages again with diff3 and,
    /// when no true conflict remains, writes the result and marks the file resolved. Binary files,
    /// non UTF-8 text and modify/delete conflicts are left alone.
    /// </summary>
    /// <returns>True when the file was resolved.</returns>
    public async Task<bool> TryAutoResolveAsync(string path, string filePath, CancellationToken cancellationToken = default)
    {
        var stages = await _reader.GetConflictStageContentsAsync(path, filePath, cancellationToken);
        if (stages.Ours is null || stages.Theirs is null)
        {
            return false;
        }

        var ours = Decode(stages.Ours);
        var theirs = Decode(stages.Theirs);
        var ancestor = stages.Base is null ? new Decoded(string.Empty, false) : Decode(stages.Base);
        if (ours is null || theirs is null || ancestor is null)
        {
            return false;
        }

        var merged = Diff3Merge.TryMerge(Lines(ancestor.Text), Lines(ours.Text), Lines(theirs.Text));
        if (merged is null)
        {
            return false;
        }

        // Keep the line endings the file has on disk (the stages are usually normalized to LF).
        var newline = "\n";
        try
        {
            if ((await _reader.ReadWorkingFileAsync(path, filePath, cancellationToken)).Contains("\r\n", StringComparison.Ordinal))
            {
                newline = "\r\n";
            }
        }
        catch (IOException)
        {
            // No working file to look at: keep LF.
        }

        var text = string.Join(newline, merged);
        var bytes = StrictUtf8.GetBytes(text);
        if (ours.HasBom)
        {
            bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bytes).ToArray();
        }

        await _writer.WriteWorkingFileBytesAsync(path, filePath, bytes, cancellationToken);
        await _writer.MarkResolvedAsync(path, filePath, cancellationToken);
        return true;
    }

    private sealed record Decoded(string Text, bool HasBom);

    /// <summary>UTF-8 text (BOM noted and removed), or null for binary content or another encoding.</summary>
    private static Decoded? Decode(byte[] bytes)
    {
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var body = hasBom ? bytes.AsSpan(3) : bytes.AsSpan();
        if (body.IndexOf((byte)0) >= 0)
        {
            return null;
        }

        try
        {
            return new Decoded(StrictUtf8.GetString(body), hasBom);
        }
        catch (System.Text.DecoderFallbackException)
        {
            return null;
        }
    }

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").Split('\n');

    public Task MarkResolvedAsync(string path, string filePath, CancellationToken cancellationToken = default)
        => _writer.MarkResolvedAsync(path, filePath, cancellationToken);

    public async Task MarkAllResolvedAsync(string path, IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        foreach (var filePath in filePaths)
        {
            await _writer.MarkResolvedAsync(path, filePath, cancellationToken);
        }
    }

    public Task MarkConflictedAsync(string path, string filePath, CancellationToken cancellationToken = default)
        => _writer.MarkConflictedAsync(path, filePath, cancellationToken);
}
