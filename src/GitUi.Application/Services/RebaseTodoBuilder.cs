using System.Text;
using GitUi.Domain.ValueObjects;

namespace GitUi.Application.Services;

/// <summary>
/// Turns the steps chosen in the interactive rebase editor into git's todo list. A reword becomes
/// "pick" followed by "exec git commit --amend -F &lt;file&gt;", so no editor ever opens.
/// </summary>
public static class RebaseTodoBuilder
{
    /// <param name="steps">Oldest first, the order git applies them.</param>
    /// <param name="messageFileFor">Returns the path of a file holding the new message of a reword step.</param>
    public static string Build(IReadOnlyList<RebaseStep> steps, Func<RebaseStep, string> messageFileFor)
    {
        Validate(steps);

        var todo = new StringBuilder();
        foreach (var step in steps)
        {
            switch (step.Action)
            {
                case RebaseAction.Pick:
                    todo.Append("pick ").Append(step.Sha).Append('\n');
                    break;
                case RebaseAction.Reword:
                    todo.Append("pick ").Append(step.Sha).Append('\n');
                    // exec runs in git's shell: forward slashes and single quotes keep a Windows path intact.
                    todo.Append("exec git commit --amend --allow-empty --no-verify -F '")
                        .Append(messageFileFor(step).Replace('\\', '/').Replace("'", "'\\''"))
                        .Append("'\n");
                    break;
                case RebaseAction.Squash:
                    todo.Append("squash ").Append(step.Sha).Append('\n');
                    break;
                case RebaseAction.Fixup:
                    todo.Append("fixup ").Append(step.Sha).Append('\n');
                    break;
                case RebaseAction.Drop:
                    todo.Append("drop ").Append(step.Sha).Append('\n');
                    break;
            }
        }

        return todo.ToString();
    }

    /// <summary>Refuses step lists git would reject or that would surprise the user.</summary>
    public static void Validate(IReadOnlyList<RebaseStep> steps)
    {
        var firstKept = steps.FirstOrDefault(s => s.Action != RebaseAction.Drop);
        if (firstKept is null)
        {
            throw new InvalidOperationException("Every commit would be dropped: keep at least one, or reset the branch instead.");
        }

        if (firstKept.Action is RebaseAction.Squash or RebaseAction.Fixup)
        {
            throw new InvalidOperationException("The first kept commit cannot be squashed: there is no commit before it to squash into.");
        }

        if (steps.FirstOrDefault(s => s.Action == RebaseAction.Reword && string.IsNullOrWhiteSpace(s.NewMessage)) is { } empty)
        {
            throw new InvalidOperationException($"The reworded commit {empty.Sha[..Math.Min(7, empty.Sha.Length)]} needs a message.");
        }
    }
}
