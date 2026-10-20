using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GitUi.Application.Services;

namespace GitUi.Presentation.Controls;

/// <summary>
/// Drag-and-drop of branches/tags between the ref panel and the graph. The dragged
/// <see cref="RefTarget"/> travels in-process; a drop opens a menu of what can be done
/// (merge, rebase, fast-forward, push, reset...).
/// </summary>
public static class RefDrag
{
    private const double DragThreshold = 6;

    public static readonly DataFormat<RefTarget> Format = DataFormat.CreateInProcessFormat<RefTarget>("gitui-ref");

    /// <summary>
    /// Makes <paramref name="control"/> a drag source: a left press on something
    /// <paramref name="resolve"/> maps to a ref starts a drag once the pointer moves a few pixels.
    /// Handled events are observed too, so list selection keeps working.
    /// </summary>
    public static void AttachSource(Control control, Func<PointerPressedEventArgs, RefTarget?> resolve)
    {
        PointerPressedEventArgs? pressed = null;
        RefTarget? candidate = null;
        Point start = default;

        control.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            {
                return;
            }

            candidate = resolve(e);
            pressed = candidate is null ? null : e;
            start = e.GetPosition(control);
        }, handledEventsToo: true);

        control.AddHandler(InputElement.PointerMovedEvent, async (_, e) =>
        {
            if (pressed is null || candidate is null)
            {
                return;
            }

            var delta = e.GetPosition(control) - start;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
            {
                return;
            }

            var startedFrom = pressed;
            var target = candidate;
            pressed = null;
            candidate = null;

            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(Format, target));
            await DragDrop.DoDragDropAsync(startedFrom, transfer, DragDropEffects.Move | DragDropEffects.Link);
        }, handledEventsToo: true);

        control.AddHandler(InputElement.PointerReleasedEvent, (_, _) =>
        {
            pressed = null;
            candidate = null;
        }, handledEventsToo: true);
    }

    /// <summary>
    /// Makes <paramref name="control"/> a drop target for refs. <paramref name="resolveTarget"/> maps
    /// the drop position to the ref (or commit) under it; <paramref name="onDrop"/> gets both refs.
    /// </summary>
    public static void AttachTarget(Control control, Func<DragEventArgs, RefTarget?> resolveTarget, Func<RefTarget, RefTarget, Task> onDrop)
    {
        DragDrop.SetAllowDrop(control, true);

        control.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(Format) && resolveTarget(e) is not null ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        });

        control.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            if (e.DataTransfer.TryGetValue(Format) is { } dragged && resolveTarget(e) is { } target)
            {
                e.Handled = true;
                await onDrop(dragged, target);
            }
        });
    }
}
