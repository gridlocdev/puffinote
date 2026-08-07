using Avalonia;
using Avalonia.Input;
using Puffinote.Text.Document;

namespace Plaintain.Control.Controls;

public sealed partial class PlainTextEditor
{
    private bool _dragging;

    /// <summary>The document offset under a point in control coordinates.</summary>
    internal int OffsetAtPoint(Point point)
    {
        int row = (int)Math.Floor((point.Y + _offset.Y) / _rowHeight);
        double x = point.X - TextLeft + _offset.X;
        int column = Math.Max(0, (int)Math.Round(x / _charWidth));
        return OffsetAt(row, column);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        ClearGoalColumn();
        int offset = OffsetAtPoint(point.Position);

        switch (e.ClickCount)
        {
            case 2:
                SelectWordAt(offset);
                break;
            case >= 3:
                SelectLineAt(offset);
                break;
            default:
                MoveCaret(offset, extend: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                _dragging = true;
                e.Pointer.Capture(this);
                break;
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
        {
            return;
        }

        MoveCaret(OffsetAtPoint(e.GetPosition(this)), extend: true);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    /// <summary>Repaint on focus changes — the caret is only drawn while the surface has the keyboard.</summary>
    private void WatchFocus()
    {
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
    }

    /// <summary>Select the word under <paramref name="offset"/> (a double-click).</summary>
    public void SelectWordAt(int offset)
    {
        int start = WordBoundaryStrict(offset, -1);
        int end = WordBoundaryStrict(offset, 1);
        Buffer.Selection = new Selection(start, end);
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Select the whole line under <paramref name="offset"/> (a triple-click).</summary>
    public void SelectLineAt(int offset)
    {
        var doc = Buffer.Document;
        int line = doc.GetPosition(Math.Clamp(offset, 0, doc.Length)).Line;
        Buffer.Selection = new Selection(doc.GetLineStart(line), Math.Min(doc.Length, doc.GetLineEnd(line) + 1));
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A word's edge from inside it. Unlike <see cref="WordBoundary"/> — which is ⌥←/→ navigation and so
    /// skips the run of separators it lands in — a double-click selects the word it is on, and nothing else.
    /// </summary>
    private int WordBoundaryStrict(int offset, int direction)
    {
        var doc = Buffer.Document;
        int i = Math.Clamp(offset, 0, doc.Length);
        if (direction < 0)
        {
            while (i > 0 && !IsWordSeparator(doc[i - 1]))
            {
                i--;
            }

            return i;
        }

        while (i < doc.Length && !IsWordSeparator(doc[i]))
        {
            i++;
        }

        return i;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        // The host ScrollViewer would handle this for us, but only once it has focus-following scroll
        // state; taking it here keeps the wheel working the same whether or not the editor is focused.
        Offset = new Vector(_offset.X - (e.Delta.X * _charWidth * 3), _offset.Y - (e.Delta.Y * _rowHeight * 3));
        e.Handled = true;
    }
}
