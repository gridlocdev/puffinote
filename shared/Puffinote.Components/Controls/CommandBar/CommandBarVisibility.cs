namespace Puffinote.Components.Controls.CommandBar;

/// <summary>
/// How much of an <see cref="AdaptiveCommandBar"/> a window shows — the family's three states for every
/// command bar (the spreadsheet's and the note editor's alike), persisted by each host.
/// <para>
/// The three are a ladder, not a toggle: <see cref="Visible"/> trades room for reach,
/// <see cref="Collapsed"/> keeps the bar one click away in a slim handle, and <see cref="Hidden"/> gives the
/// row back entirely. Hidden leaves nothing on screen to click, so it is deliberately only reachable — and
/// escapable — from Settings, the menu bar, and the Command Palette, never from the bar itself.
/// </para>
/// </summary>
public enum CommandBarVisibility
{
    /// <summary>The full bar: tool groups, overflow drawer, and search pill. The default.</summary>
    Visible,

    /// <summary>Folded to its slim handle — a chevron that brings the full bar back.</summary>
    Collapsed,

    /// <summary>Not shown at all; the bar takes no room.</summary>
    Hidden,
}
