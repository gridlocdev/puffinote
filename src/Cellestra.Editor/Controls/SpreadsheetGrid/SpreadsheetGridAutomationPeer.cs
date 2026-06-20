using Avalonia.Automation;
using Avalonia.Automation.Peers;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Accessibility peer for the custom-drawn grid (AGENTS.md: new controls expose automation peers and
/// are keyboard-operable). The grid has no per-cell controls, so this is a single control-level peer
/// that reports a data-grid role and announces the active cell's address + value as the selection
/// moves, so a screen reader reads the current cell.
/// </summary>
internal sealed class SpreadsheetGridAutomationPeer : ControlAutomationPeer
{
    private readonly SpreadsheetGrid _grid;

    public SpreadsheetGridAutomationPeer(SpreadsheetGrid grid)
        : base(grid) => _grid = grid;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataGrid;

    protected override string GetClassNameCore() => "SpreadsheetGrid";

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    // No explicit host-set name → the peer's name is the live active-cell description, so focusing the
    // grid (and each subsequent move) reads the current cell.
    protected override string? GetNameCore()
    {
        string? explicitName = AutomationProperties.GetName(_grid);
        return string.IsNullOrEmpty(explicitName) ? _grid.ActiveCellDescription : explicitName;
    }

    /// <summary>Re-announce the active cell so an attached screen reader reads it after a move.</summary>
    internal void AnnounceActiveCell(string description) =>
        RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, null, description);
}
