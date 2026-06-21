using System.Collections.Generic;
using System.Linq;
using Cellestra.Core.Dependencies;
using Cellestra.Core.Model;

namespace Cellestra.Core.Editing;

/// <summary>
/// Several edits applied (and undone) as one step, so a compound action — e.g. applying a number
/// format and widening the columns it overflowed — is a single entry on the undo stack.
/// </summary>
public sealed class CompositeEditCommand : IEditCommand
{
    private readonly IReadOnlyList<IEditCommand> _commands;

    public CompositeEditCommand(string label, IReadOnlyList<IEditCommand> commands)
    {
        Label = label;
        _commands = commands;
    }

    public string Label { get; }

    public EditResult Apply(Workbook workbook) => Combine(_commands.Select(c => c.Apply(workbook)));

    // Undo in reverse order so each step sees the workbook in the state it left behind.
    public EditResult Undo(Workbook workbook) => Combine(_commands.Reverse().Select(c => c.Undo(workbook)));

    private static EditResult Combine(IEnumerable<EditResult> results)
    {
        var changed = new List<CellRef>();
        bool structural = false;
        foreach (var result in results)
        {
            changed.AddRange(result.ChangedCells);
            structural |= result.Structural;
        }

        return new EditResult(changed, structural);
    }
}
