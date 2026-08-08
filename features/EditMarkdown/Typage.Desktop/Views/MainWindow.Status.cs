using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Puffinote.Components.Theming;

namespace Typage.Desktop.Views;

public partial class MainWindow
{
    private Border BuildStatusBar()
    {
        foreach (var block in new[] { _statusState, _statusPosition, _statusCounts, _statusZoom })
        {
            block.Foreground = _mutedText;
            block.FontSize = 12;
            block.VerticalAlignment = VerticalAlignment.Center;
        }

        // State on the left; counts/encoding pushed to the right (like Cellestra's status bar), with the
        // zoom percentage last — the same place Cellestra puts it.
        _statusCounts.HorizontalAlignment = HorizontalAlignment.Right;
        var grid = new Grid
        {
            Margin = new Avalonia.Thickness(12, 4),
            ColumnDefinitions = new ColumnDefinitions("Auto,16,Auto,*,Auto,16,Auto"),
        };
        Grid.SetColumn(_statusState, 0);
        Grid.SetColumn(_statusPosition, 2);
        Grid.SetColumn(_statusCounts, 4);
        Grid.SetColumn(_statusZoom, 6);
        grid.Children.Add(_statusState);
        grid.Children.Add(_statusPosition);
        grid.Children.Add(_statusCounts);
        grid.Children.Add(_statusZoom);

        var border = new Border
        {
            Child = grid,
            BorderThickness = new Avalonia.Thickness(0, 1, 0, 0),
            BorderBrush = _chromeBorder,
            Background = _chromeBg,
        };
        DockPanel.SetDock(border, Dock.Bottom);
        return border;
    }

    // ----- status / title -----

    private void UpdateChrome()
    {
        UpdateTitle();
        UpdateDocumentStatus();
        UpdateCaretStatus();
        UpdateZoomStatus();
    }

    private void UpdateZoomStatus()
    {
        _statusZoom.Text = ZoomScale.Format(EditorView.Zoom);
        _statusZoom.IsVisible = EditorView.Zoom != ZoomScale.Default;
    }

    private void UpdateTitle()
    {
        var name = _currentPath is null ? "Untitled" : Path.GetFileName(_currentPath);

        // Window.Title still carries the app-name suffix: it is what the Window menu and the app switcher
        // show, where "notes.md" alone would not say which app it belongs to. The title *bar* shows the
        // document's name and nothing else, centred.
        Title = (EditorView.IsDirty ? "• " : string.Empty) + name + " — Typage";
        _titleBar.DocumentName = name;
        _titleBar.IsDirty = EditorView.IsDirty;
    }

    /// <summary>
    /// Coalesce the whole-document word/char scan to ~200ms after the last edit, so a burst of typing
    /// recomputes the counts once (when the user pauses) rather than on every keystroke.
    /// </summary>
    private void ScheduleDocumentStatus()
    {
        _statsTimer ??= CreateStatsTimer();
        _statsTimer.Stop();
        _statsTimer.Start();
    }

    private DispatcherTimer CreateStatsTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            UpdateDocumentStatus();
        };

        return timer;
    }

    /// <summary>Document-level status — the word/char counts scan the document, so this is debounced on typing.</summary>
    private void UpdateDocumentStatus()
    {
        _statsTimer?.Stop(); // a synchronous refresh supersedes any pending debounced one
        var stats = EditorView.Stats; // cached; whole-document scan only on text change
        _statusState.Text = EditorView.IsDirty ? "Modified" : "Saved";
        _statusCounts.Text =
            $"{stats.Words} words · {stats.Characters} chars · ~{stats.ReadingMinutes} min · {_format.DisplayName} · {stats.NewlineLabel}";
    }

    /// <summary>Caret/selection status — cheap, updated on every selection change.</summary>
    private void UpdateCaretStatus()
    {
        var pos = EditorView.CaretPosition;
        var selected = EditorView.SelectedCharacterCount;
        _statusPosition.Text = selected > 0
            ? $"Ln {pos.DisplayLine}, Col {pos.DisplayColumn}  ({selected} selected)"
            : $"Ln {pos.DisplayLine}, Col {pos.DisplayColumn}";
    }
}
