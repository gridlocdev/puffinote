using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Puffinote.Components.Commands;
using Typage.Control.Commands;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Engine.Document;

namespace Typage.Control.Views;

/// <summary>
/// The embeddable component the PRD calls for (§10.2): the <see cref="MarkdownEditor"/> surface
/// wrapped in a scroll viewer, exposing a small stable public API (text, dirty flag, stats,
/// caret, theme, embed-resolver injection, undo/redo, events) with no file or app assumptions.
/// The standalone shell and the future notes app both host this unchanged; file I/O lives in the
/// host. Built in code (no XAML) so it carries no resource dependencies.
/// </summary>
public class MarkdownEditorView : UserControl
{
    private readonly MarkdownCommandBar _commandBar;

    public MarkdownEditorView()
    {
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = Editor,
        };

        // An optional formatting toolbar over the editor's own command catalog. Hidden by default so the
        // standalone app (which supplies its own toolbar) is unaffected; a host like Puffinote turns it on.
        _commandBar = new MarkdownCommandBar(EditingCommands) { IsVisible = false };
        DockPanel.SetDock(_commandBar, Dock.Top);
        Content = new DockPanel { Children = { _commandBar, scroll } };

        Editor.TextChanged += (_, e) =>
        {
            _commandBar.RefreshState(); // an edit can change the format under the caret (e.g. typing past a **)
            TextChanged?.Invoke(this, e);
        };
        Editor.SelectionChanged += (_, e) =>
        {
            _commandBar.RefreshState(); // light the tools for the format at the new caret position
            SelectionChanged?.Invoke(this, e);
        };
    }

    /// <summary>
    /// Show the formatting command bar (toolbar chrome) over the editor. Off by default; a host that doesn't
    /// supply its own toolbar (Puffinote) turns it on so the common Markdown controls are one click away.
    /// </summary>
    public bool ShowCommandBar
    {
        get => _commandBar.IsVisible;
        set => _commandBar.IsVisible = value;
    }

    /// <summary>Raised after the document text changes.</summary>
    public event EventHandler? TextChanged;

    /// <summary>Raised after the caret or selection changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The underlying surface, for hosts that need direct access (e.g. the format toolbar).</summary>
    public MarkdownEditor Editor { get; } = new();

    public string Text
    {
        get => Editor.Text;
        set => Editor.Text = value;
    }

    public bool IsDirty => Editor.IsDirty;

    public DocumentStats Stats => Editor.Stats;

    public LinePosition CaretPosition => Editor.CaretPosition;

    /// <summary>Number of grapheme clusters currently selected (0 when empty).</summary>
    public int SelectedCharacterCount => Editor.SelectedCharacterCount;

    public bool CanUndo => Editor.CanUndo;

    public bool CanRedo => Editor.CanRedo;

    public MarkdownTheme EditorTheme
    {
        get => Editor.EditorTheme;
        set => Editor.EditorTheme = value;
    }

    public IEmbedResolver EmbedResolver
    {
        get => Editor.EmbedResolver;
        set => Editor.EmbedResolver = value;
    }

    /// <summary>Load text and mark it as the saved baseline (open / revert).</summary>
    public void LoadText(string text)
    {
        Editor.Text = text;
        Editor.MarkSaved();
    }

    public void MarkSaved() => Editor.MarkSaved();

    /// <summary>Load recovered text, leaving it marked dirty (unsaved) for crash recovery (PRD §9.4).</summary>
    public void LoadRecovered(string text) => Editor.LoadDirty(text);

    public void Undo() => Editor.Undo();

    public void Redo() => Editor.Redo();

    /// <summary>
    /// This editor's own command catalog (editing/formatting operations) for a host's Command Palette, menu,
    /// or toolbar — mirroring how <c>Cellestra.Editor</c> publishes its catalog. Shell commands (file, find,
    /// dialogs, view mode) stay with the host.
    /// </summary>
    public IReadOnlyList<AppCommand> EditingCommands => MarkdownEditingCommands.Build(this);
}
