using Cellestra.Core.Model;

namespace Cellestra.Core.Editing;

/// <summary>
/// An edit command built from apply/undo delegates that capture their own before-state in a
/// closure. Used for small, targeted changes (resize, hide, merge) where a full snapshot would
/// be wasteful. <see cref="Apply"/> re-captures each time, so redo works correctly.
/// </summary>
public sealed class ActionCommand : IEditCommand
{
    private readonly Func<Workbook, EditResult> _apply;
    private readonly Func<Workbook, EditResult> _undo;

    public ActionCommand(string label, Func<Workbook, EditResult> apply, Func<Workbook, EditResult> undo)
    {
        Label = label;
        _apply = apply;
        _undo = undo;
    }

    public string Label { get; }

    public EditResult Apply(Workbook workbook) => _apply(workbook);

    public EditResult Undo(Workbook workbook) => _undo(workbook);
}
