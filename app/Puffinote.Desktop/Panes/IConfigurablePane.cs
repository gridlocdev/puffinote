namespace Puffinote.Desktop.Panes;

/// <summary>
/// A pane whose editor takes its settings from the resolved editor config (PRD §7.5) — its own app's
/// settings file, with any Puffinote override layered on — and can be re-seeded after they change, so a
/// Preferences edit reaches the tabs that are already open, not only the next one. Each pane reads the one
/// mirror that is its editor's and ignores the rest.
/// <para>
/// Kept off <see cref="IEditorPane"/> deliberately: a pane type with nothing to configure shouldn't have to
/// implement a no-op to say so. <see cref="TabHost"/> re-applies to whichever open panes implement this.
/// </para>
/// </summary>
internal interface IConfigurablePane
{
    /// <summary>Re-seed the editor from <paramref name="config"/>.</summary>
    void ApplyConfig(EditorConfig config);
}
