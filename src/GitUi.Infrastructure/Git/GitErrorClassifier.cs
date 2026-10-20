using GitUi.Domain.Exceptions;
using GitUi.Infrastructure.Processes;

namespace GitUi.Infrastructure.Git;

public static class GitErrorClassifier
{
    public static GitOperationException Classify(string command, ProcessResult result)
    {
        var stderr = result.StandardError;
        var message = $"Command '{command}' failed with exit code {result.ExitCode}.";

        if (Contains(stderr, "Authentication failed") || Contains(stderr, "could not read Username"))
        {
            return new GitAuthenticationException(message, stderr);
        }

        // merge / pull / cherry-pick / stash pop report conflicts on stdout ("CONFLICT (content): ...",
        // "Automatic merge failed") with an empty stderr, so both streams are searched.
        var output = string.Join('\n', new[] { result.StandardOutput, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (Contains(output, "CONFLICT") || Contains(output, "Automatic merge failed"))
        {
            return new GitMergeConflictException(message, output);
        }

        if (Contains(stderr, "Could not resolve host") || Contains(stderr, "Connection timed out"))
        {
            return new GitNetworkException(message, stderr);
        }

        if (Contains(stderr, "not a git repository"))
        {
            return new GitRepositoryCorruptException(message, stderr);
        }

        return new GitOperationException(message, ErrorCode.Unknown, stderr);
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
