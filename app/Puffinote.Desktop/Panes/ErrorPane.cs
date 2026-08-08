using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Commands;
using Puffinote.Components.Theming;

namespace Puffinote.Desktop.Panes;

/// <summary>
/// A non-editing pane shown in a tab when a file can't be opened (a bad/locked file, or one this build can't
/// parse — e.g. an empty <c>.ods</c> that isn't a real workbook). It puts the reason where the editor would
/// be, rather than opening a blank/broken editor or silently doing nothing. It reports no
/// <see cref="Path"/>, so the shell never records it as a recent file or tries to save/rebind it.
/// </summary>
internal sealed class ErrorPane : IEditorPane
{
    public ErrorPane(string path, string message)
    {
        Title = System.IO.Path.GetFileName(path);
        View = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 460,
            Margin = new Thickness(24),
            Foreground = Tokens.Active.MutedText,
        };
    }

    public Control View { get; }

    /// <summary>Focus the message surface — there's no editor here, but honor the seam.</summary>
    public void Focus() => View.Focus();

    public string Title { get; }

    /// <summary>Null — this isn't a real document, so it's never recorded as recent, saved, or rebound.</summary>
    public string? Path => null;

    public bool IsDirty => false;

    // Never fires (the pane is always clean); declared with no backing field so it raises no unused-event warning.
    public event EventHandler? DirtyChanged { add { } remove { } }

    public Task LoadAsync(string path) => Task.CompletedTask;

    public Task SaveAsync() => Task.CompletedTask;

    public void Rebind(string newPath) { }

    public IReadOnlyList<AppCommand> Commands => [];

    public IReadOnlyList<OutlineItem> GetOutline() => [];

    public void Dispose() { }
}
