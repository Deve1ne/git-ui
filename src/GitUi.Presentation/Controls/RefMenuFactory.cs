using Avalonia.Controls;
using GitUi.Application.Services;

namespace GitUi.Presentation.Controls;

/// <summary>Turns planned ref actions into an Avalonia context menu: one separator between groups, submenus for children.</summary>
public static class RefMenuFactory
{
    public static ContextMenu Build(IReadOnlyList<IReadOnlyList<RefAction>> groups, Func<RefAction, Task> execute)
    {
        var menu = new ContextMenu();
        foreach (var item in BuildItems(groups, execute))
        {
            menu.Items.Add(item);
        }

        return menu;
    }

    public static IEnumerable<Control> BuildItems(IReadOnlyList<IReadOnlyList<RefAction>> groups, Func<RefAction, Task> execute)
    {
        for (var i = 0; i < groups.Count; i++)
        {
            if (i > 0)
            {
                yield return new Separator();
            }

            foreach (var action in groups[i])
            {
                yield return BuildItem(action, execute);
            }
        }
    }

    private static MenuItem BuildItem(RefAction action, Func<RefAction, Task> execute)
    {
        var item = new MenuItem { Header = action.InputPrompt is null ? action.Label : action.Label + "…" };
        if (action.Children.Count > 0)
        {
            foreach (var child in action.Children)
            {
                item.Items.Add(BuildItem(child, execute));
            }
        }
        else
        {
            item.Click += (_, _) => _ = execute(action);
        }

        return item;
    }
}
