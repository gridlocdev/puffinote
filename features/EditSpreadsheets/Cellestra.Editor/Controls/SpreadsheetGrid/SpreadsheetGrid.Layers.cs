using Avalonia.Controls;
using Avalonia.Media;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

public sealed partial class SpreadsheetGrid
{
    /// <summary>The draw-only child. Panel.Render is sealed, so drawing lives on this Control.</summary>
    private sealed class Surface : Control
    {
        private readonly SpreadsheetGrid _owner;

        public Surface(SpreadsheetGrid owner)
        {
            _owner = owner;
            IsHitTestVisible = false; // pointer events route to the owning panel
        }

        public override void Render(DrawingContext context) => _owner.DrawTo(context);
    }

    /// <summary>
    /// A transparent, click-through layer that floats directly above the inline editor and draws the
    /// editor's underline/strikethrough — the one thing a plain <see cref="TextBox"/> can't show. It
    /// measures the live text with the cell's typeface (matching the editor's font), so the lines sit
    /// exactly under/through the glyphs the TextBox renders.
    /// </summary>
    private sealed class EditorDecorationLayer : Control
    {
        private readonly SpreadsheetGrid _owner;

        public EditorDecorationLayer(SpreadsheetGrid owner)
        {
            _owner = owner;
            IsHitTestVisible = false;
            ClipToBounds = true;
        }

        public override void Render(DrawingContext context) => _owner.DrawEditorDecorations(context);
    }
}
