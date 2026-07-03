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
    private readonly MarkdownEditor _editor = new();

    private readonly MarkdownCommandBar _commandBar;

    public MarkdownEditorView()
    {
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _editor,
        };

        // An optional formatting toolbar over the editor's own command catalog. Hidden by default so the
        // standalone app (which supplies its own toolbar) is unaffected; a host like Puffinote turns it on.
        _commandBar = new MarkdownCommandBar(EditingCommands) { IsVisible = false };
        DockPanel.SetDock(_commandBar, Dock.Top);
        Content = new DockPanel { Children = { _commandBar, scroll } };

        _editor.TextChanged += (_, e) => TextChanged?.Invoke(this, e);
        _editor.SelectionChanged += (_, e) => SelectionChanged?.Invoke(this, e);
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
    public MarkdownEditor Editor => _editor;

    public string Text
    {
        get => _editor.Text;
        set => _editor.Text = value;
    }

    public bool IsDirty => _editor.IsDirty;

    public DocumentStats Stats => _editor.Stats;

    public LinePosition CaretPosition => _editor.CaretPosition;

    /// <summary>Number of grapheme clusters currently selected (0 when empty).</summary>
    public int SelectedCharacterCount => _editor.SelectedCharacterCount;

    public bool CanUndo => _editor.CanUndo;

    public bool CanRedo => _editor.CanRedo;

    public MarkdownTheme EditorTheme
    {
        get => _editor.EditorTheme;
        set => _editor.EditorTheme = value;
    }

    public IEmbedResolver EmbedResolver
    {
        get => _editor.EmbedResolver;
        set => _editor.EmbedResolver = value;
    }

    /// <summary>Load text and mark it as the saved baseline (open / revert).</summary>
    public void LoadText(string text)
    {
        _editor.Text = text;
        _editor.MarkSaved();
    }

    public void MarkSaved() => _editor.MarkSaved();

    /// <summary>Load recovered text, leaving it marked dirty (unsaved) for crash recovery (PRD §9.4).</summary>
    public void LoadRecovered(string text) => _editor.LoadDirty(text);

    public void Undo() => _editor.Undo();

    public void Redo() => _editor.Redo();

    /// <summary>
    /// This editor's own command catalog (editing/formatting operations) for a host's Command Palette, menu,
    /// or toolbar — mirroring how <c>Cellestra.Editor</c> publishes its catalog. Shell commands (file, find,
    /// dialogs, view mode) stay with the host.
    /// </summary>
    public IReadOnlyList<AppCommand> EditingCommands => MarkdownEditingCommands.Build(this);
}
