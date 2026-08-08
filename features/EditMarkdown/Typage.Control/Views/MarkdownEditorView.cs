using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Puffinote.Components.Commands;
using Puffinote.Components.Input;
using Puffinote.Components.Overlays;
using Puffinote.Components.Theming;
using Puffinote.Text.Document;
using Typage.Control.Commands;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Engine.Html;

namespace Typage.Control.Views;

/// <summary>
/// The embeddable component the PRD calls for (§10.2): the <see cref="MarkdownEditor"/> surface
/// wrapped in a scroll viewer, exposing a small stable public API (text, dirty flag, stats,
/// caret, theme, embed-resolver injection, undo/redo, events) with no file or app assumptions.
/// The standalone shell and the Puffinote shell both host this unchanged. Built in code (no XAML) so it
/// carries no resource dependencies.
/// <para>
/// It is <em>self-sufficient</em>: the formatting command bar, the find/replace bar, and the link, image and
/// reference dialogs all ship here, in the surface, over the surface's own overlay layer. A host therefore
/// gets Typage's whole editing experience by embedding one control — it does not reimplement any of it, and
/// the two shells cannot drift apart. What stays with a host is what genuinely needs one: the window, the
/// menu bar, recent files, settings, and deciding <em>which</em> file is open (see
/// <see cref="MarkdownDocumentSession"/>).
/// </para>
/// </summary>
public class MarkdownEditorView : UserControl
{
    public MarkdownEditorView()
    {
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = Editor,
        };

        // Built once, here, rather than per read: the toolbar, the host's palette, and the host's key bindings
        // must all point at the *same* command objects (a rebuild-per-read hands each a private copy).
        EditingCommands = MarkdownEditingCommands.Build(this);

        // The formatting toolbar over the editor's own command catalog. Hidden by default: it's the host's
        // call whether the surface carries chrome (Puffinote and the standalone app both turn it on).
        CommandBar = new MarkdownCommandBar(EditingCommands) { IsVisible = false };
        FindBar = new FindBar(this);
        DockPanel.SetDock(CommandBar, Dock.Top);
        DockPanel.SetDock(FindBar, Dock.Top);

        // The overlay layer floats over the editor, so a link prompt or the reference sheet dims and covers
        // the surface it belongs to — correct both full-window (standalone) and inside a pane (Puffinote).
        Content = new Panel
        {
            Children = { new DockPanel { Children = { CommandBar, FindBar, scroll } }, Overlays },
        };
        Overlays.Closed += (_, _) => Editor.Focus(); // never strand focus in a torn-down overlay

        // The trackpad half of zoom, over the same seam the ⌘= / ⌘− catalog uses (see PinchZoom).
        PinchZoom.Attach(this, () => Zoom, zoom => Zoom = zoom);

        Editor.TextChanged += (_, e) =>
        {
            CommandBar.RefreshState(); // an edit can change the format under the caret (e.g. typing past a **)
            TextChanged?.Invoke(this, e);
        };
        Editor.SelectionChanged += (_, e) =>
        {
            CommandBar.RefreshState(); // light the tools for the format at the new caret position
            SelectionChanged?.Invoke(this, e);
        };

        // The surface owns the right-click menu but not the dialogs (they need the overlay layer), so its link
        // items ask for the prompt and it opens here — one dialog for the menu, the toolbar and ⌘⌥K alike.
        Editor.InsertLinkRequested += (_, _) => _ = ShowLinkDialogAsync();
        Editor.EditLinkRequested += (_, _) => _ = ShowEditLinkDialogAsync();
    }

    /// <summary>The formatting toolbar docked over the editor (shown via <see cref="ShowCommandBar"/>).</summary>
    internal MarkdownCommandBar CommandBar { get; }

    /// <summary>The find/replace bar docked under the toolbar (opened via <see cref="ShowFind"/>).</summary>
    internal FindBar FindBar { get; }

    /// <summary>The surface's own overlay layer, hosting the link prompt, the reference sheet, and errors.</summary>
    internal OverlayHost Overlays { get; } = new() { Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) };

    /// <summary>
    /// Show the formatting command bar (toolbar chrome) over the editor. Off by default; both shells turn it
    /// on, and each offers a "Toggle Editor Toolbar" command that flips it.
    /// </summary>
    public bool ShowCommandBar
    {
        get => CommandBar.IsVisible;
        set
        {
            CommandBar.IsVisible = value;
            CommandBar.RefreshState(); // lit state was skipped while hidden — bring it current on show
        }
    }

    /// <summary>
    /// The absolute path of the file being edited, or null for an unsaved document. The host sets it (it owns
    /// the file); the surface reads it to anchor a relative image path and to name an HTML export. An unset
    /// path is not an error — it's what puts an inserted image through <see cref="ImageStaging"/>.
    /// </summary>
    public string? DocumentPath { get; set; }

    /// <summary>How the Markdown is rendered: live preview, markers revealed, or plain source (PRD §7.4).</summary>
    public MarkdownEditor.ViewMode ViewMode
    {
        get => Editor.View;
        set
        {
            if (Editor.View == value)
            {
                return;
            }

            Editor.View = value;
            CommandBar.RefreshState(); // the view-mode commands are toggles; re-light the active one
            ViewModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>True while the surface is showing one of its own modals (link, image error, reference), so a
    /// host can stand its shortcuts down rather than firing a command behind the dialog.</summary>
    public bool IsOverlayOpen => Overlays.IsOpen;

    /// <summary>True while the find/replace bar is open over the editor.</summary>
    public bool IsFindOpen => FindBar.IsVisible;

    /// <summary>Raised after the document text changes.</summary>
    public event EventHandler? TextChanged;

    /// <summary>Raised after the caret or selection changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised after <see cref="ViewMode"/> changes, so a host can persist it as the new default.</summary>
    public event EventHandler? ViewModeChanged;

    /// <summary>Raised after <see cref="Zoom"/> changes, so a host can persist it as the new default.</summary>
    public event EventHandler? ZoomChanged;

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

    /// <summary>
    /// The palette in force — the theme as the <em>host</em> set it, at 100%. What the editor actually
    /// renders with is this scaled by <see cref="Zoom"/>; keeping the two apart is what lets a reader
    /// switch to the light theme at 150% and stay at 150%, and lets a host still compare what it set
    /// against <see cref="MarkdownTheme.Light"/> by reference.
    /// </summary>
    public MarkdownTheme EditorTheme
    {
        get;
        set
        {
            field = value;
            ApplyTheme();
        }
    } = MarkdownTheme.Default;

    /// <summary>
    /// How much larger than authored the text renders, on the family's shared ladder
    /// (<see cref="ZoomScale"/>). Scales the type and the vertical rhythm with it — never the window
    /// chrome or the find bar.
    /// </summary>
    public double Zoom
    {
        get;
        set
        {
            double clamped = ZoomScale.Clamp(value);
            if (clamped == field)
            {
                return;
            }

            field = clamped;
            ApplyTheme();
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    } = ZoomScale.Default;

    private void ApplyTheme() => Editor.EditorTheme = EditorTheme.Scaled(Zoom);

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
    /// This editor's own command catalog — every editing, formatting, find and insert operation the surface
    /// offers — for a host's Command Palette, menu, toolbar, and key bindings, mirroring how
    /// <c>Cellestra.Editor</c> publishes its catalog. Built once, so the toolbar, the palette and the host's
    /// key bindings all point at the <em>same</em> command objects.
    /// </summary>
    public IReadOnlyList<AppCommand> EditingCommands { get; }

    // ----- the surface's own commands (each reached from the catalog above) -----

    /// <summary>Open the find bar, in find or find-and-replace mode (⌘F / ⌘⌥F).</summary>
    public void ShowFind(bool replace) => FindBar.Show(replace);

    /// <summary>Open the reference sheet: Markdown syntax plus every shortcut this catalog binds.</summary>
    public void ShowReference()
    {
        if (!Overlays.IsOpen)
        {
            _ = Overlays.ShowModalAsync(new MarkdownReferenceDialog(EditingCommands));
        }
    }

    /// <summary>
    /// The link prompt (⌘⌥K): a "Display text" field prefilled from the selection / word under the caret, and
    /// a "Location". On Add the link is inserted over the current target.
    /// </summary>
    public async Task ShowLinkDialogAsync()
    {
        if (Overlays.IsOpen)
        {
            return;
        }

        if (await Overlays.ShowModalAsync(new LinkDialog(Editor.SuggestedLinkText())) is { } values)
        {
            Editor.InsertLinkWithText(values.Text, values.Url);
        }
    }

    /// <summary>
    /// The same prompt over the link the caret is <em>already in</em> (Edit Link…, and the link's own
    /// right-click menu), prefilled with its text and destination; Save rewrites that link. It does nothing
    /// when the caret isn't in a link — the command is offered only where it applies (<see cref="CanEditLink"/>),
    /// so it never opens an "edit" dialog over nothing.
    /// </summary>
    public async Task ShowEditLinkDialogAsync()
    {
        if (Overlays.IsOpen || Editor.LinkAtCaret() is not { } link)
        {
            return;
        }

        if (await Overlays.ShowModalAsync(new LinkDialog(link.Text, link.Url, editing: true)) is { } values)
        {
            // Re-read the link: an await can outlive the document it was read from (an external change, a
            // recovery load), and rewriting a stale span would cut the edit out of the middle of the new text.
            if (Editor.LinkAtCaret() is { } current)
            {
                Editor.ReplaceLink(current, values.Text, values.Url);
            }
        }
    }

    /// <summary>Whether the caret is in a link, so the host can enable/disable its Edit Link… command.</summary>
    public bool CanEditLink => Editor.IsCaretInLink;

    /// <summary>
    /// Pick an image file and insert a reference to it. A <b>saved</b> document (one with a
    /// <see cref="DocumentPath"/>) references the picked image by a relative path — it has a folder to anchor
    /// against. An <b>unsaved</b> document has no such anchor, so the image is copied into a scratch directory
    /// and referenced by that absolute staged path; the host's next save offers to move it into the document's
    /// permanent home (see <see cref="ImageStaging.Reconcile"/>).
    /// </summary>
    public async Task InsertImageAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Insert Image",
            AllowMultiple = false,
            FileTypeFilter = [ImageType, FilePickerFileTypes.All],
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } source)
        {
            return;
        }

        // A storage-picker await can resume off the UI thread, and what follows touches the editor.
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var alt = Path.GetFileNameWithoutExtension(source);
            try
            {
                var url = DocumentPath is { } docPath
                    ? ImageStaging.RelativeUrl(docPath, source)          // saved → relative path in place
                    : ImageStaging.Stage(source, Guid.NewGuid().ToString("N")); // unsaved → stage a copy
                Editor.InsertImageReference(ImageStaging.EncodeUrl(url), alt); // encode so spaces don't break the link
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowError("Couldn't insert the image.", $"{ex.Message}\n\nThe image could not be staged for the unsaved document.");
            }
        });
    }

    /// <summary>Render the document to a self-contained HTML file of the user's choosing.</summary>
    public async Task ExportHtmlAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export HTML",
            DefaultExtension = "html",
            SuggestedFileName = (Path.GetFileNameWithoutExtension(DocumentPath) ?? "Untitled") + ".html",
            FileTypeChoices = [new FilePickerFileType("HTML") { Patterns = ["*.html", "*.htm"] }],
        });

        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        // Read the editor text and report errors on the UI thread (the picker await may resume off it).
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var html = HtmlRenderer.RenderDocument(TextDocument.FromString(Text), Path.GetFileNameWithoutExtension(path));
            try
            {
                MarkdownDocumentSession.AtomicWrite(path, new UTF8Encoding(false).GetBytes(html)); // HTML export is always UTF-8
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowError("Couldn't export HTML.", $"{ex.Message}\n\nCheck that the destination folder is writable.");
            }
        });
    }

    /// <summary>The image file types offered in the insert-image picker (PRD §8.3 image set + common aliases).</summary>
    private static readonly FilePickerFileType ImageType = new("Images") { Patterns = ImageStaging.ImagePatterns };

    /// <summary>State an OS failure and a suggested action in the surface's own overlay (heuristic 9).</summary>
    private void ShowError(string message, string detail) =>
        _ = Overlays.ShowModalAsync(new MessageDialog(heading: message, message: detail));
}
