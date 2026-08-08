namespace Puffinote.Desktop.Panes;

/// <summary>
/// A pane whose editor takes its settings from the resolved note config (PRD §7.5) and can be re-seeded
/// after the overrides change — so a Preferences edit reaches the tabs that are already open, not only the
/// next one. Kept off <see cref="IEditorPane"/> deliberately: not every pane type has anything Puffinote
/// overrides (a board's settings live in the board file), and a pane shouldn't have to implement a no-op
/// to say so. <see cref="TabHost"/> re-applies to whichever open panes implement this.
/// </summary>
internal interface IConfigurablePane
{
    /// <summary>Re-seed the editor from <paramref name="config"/>.</summary>
    void ApplyConfig(NoteConfig config);
}
