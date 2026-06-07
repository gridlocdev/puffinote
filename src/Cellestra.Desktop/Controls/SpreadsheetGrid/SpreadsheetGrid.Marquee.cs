using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// The copy/cut "marching ants" marquee: an animated dashed border around the clipboard source
/// range, persisting until paste, Esc, or a new edit. Cut and copy are drawn differently. When the
/// grid clears the marquee on its own (Esc or starting an edit), it raises
/// <see cref="ClipboardInvalidated"/> so the owner can drop a pending cut (behaviors #1, #5).
/// </summary>
public sealed partial class SpreadsheetGrid
{
    private CellRange? _marqueeRange;
    private string? _marqueeSheet;
    private bool _marqueeIsCut;
    private double _marchPhase;
    private DispatcherTimer? _marchTimer;

    private const double DashOn = 4;
    private const double DashOff = 3;

    /// <summary>Raised when the grid clears the marquee itself (Esc or an edit), to drop the clipboard.</summary>
    public event EventHandler? ClipboardInvalidated;

    private bool HasMarquee => _marqueeRange is not null;

    /// <summary>Show (or, with a null range, hide) the marquee around a clipboard source range.</summary>
    public void ShowMarquee(CellRange? range, bool isCut, string? sheetName)
    {
        _marqueeRange = range;
        _marqueeSheet = sheetName;
        _marqueeIsCut = isCut;

        if (range is null)
        {
            _marchTimer?.Stop();
        }
        else
        {
            EnsureMarchTimer();
            _marchTimer!.Start();
        }

        Redraw();
    }

    /// <summary>Clear the marquee and notify the owner so a pending cut is dropped.</summary>
    private void InvalidateClipboard()
    {
        if (!HasMarquee)
        {
            return;
        }

        ShowMarquee(null, false, null);
        ClipboardInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureMarchTimer()
    {
        _marchTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(90),
            DispatcherPriority.Background,
            (_, _) =>
            {
                _marchPhase = (_marchPhase + 1) % (DashOn + DashOff);
                if (HasMarquee)
                {
                    Redraw();
                }
            });
    }

    private void DrawMarquee(DrawingContext context)
    {
        if (_marqueeRange is not { } range || Sheet is null ||
            !string.Equals(Sheet.Name, _marqueeSheet, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!TryGetRegionRect(range.Top, range.Left, range.Bottom, range.Right, out var rect))
        {
            return; // scrolled out of view
        }

        // Cut is muted gray; copy is green — a glance tells them apart.
        var color = _marqueeIsCut
            ? Color.FromRgb(0x70, 0x70, 0x70)
            : Color.FromRgb(0x1E, 0x88, 0x4E);
        var pen = new Pen(new SolidColorBrush(color), 2)
        {
            DashStyle = new DashStyle(new double[] { DashOn, DashOff }, _marchPhase),
        };

        context.DrawRectangle(null, pen, rect.Deflate(1));
    }
}
