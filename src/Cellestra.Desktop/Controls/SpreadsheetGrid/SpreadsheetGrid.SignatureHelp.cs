using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Cellestra.Core.Formulas.Functions;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// The function argument hint shown while typing a formula (#: positional tooltip). Reads the one
/// <see cref="FunctionRegistry"/> (Golden Rule #5) so the signature can't drift from the evaluator,
/// and uses <see cref="FormulaSignatureScanner"/> to bold the argument the caret is currently in,
/// e.g. inside <c>=SUM(A1, </c> it shows <c>SUM(number1, **number2, …**)</c>.
/// </summary>
public sealed partial class SpreadsheetGrid
{
    private static readonly FunctionRegistry SignatureFunctions = FunctionRegistry.CreateDefault();
    private static readonly IBrush SignatureActiveBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x2A, 0x7D, 0xE1));

    private readonly TextBlock _signatureText = new() { FontSize = 12, FontFamily = new FontFamily(Cellestra.Core.Defaults.FontFamily) };
    private readonly Border _signatureTip = new()
    {
        IsVisible = false,
        IsHitTestVisible = false, // never steals the editor's focus or clicks
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(8, 4),
    };

    private void InitSignatureHelp()
    {
        _signatureTip.Child = _signatureText;
        Children.Add(_signatureTip);
    }

    /// <summary>Test seam: whether the argument hint is showing.</summary>
    internal bool IsSignatureHelpVisible => _signatureTip.IsVisible;

    /// <summary>Test seam: the label of the argument currently emphasized in the hint, if any.</summary>
    internal string? ActiveSignatureArgument
    {
        get
        {
            if (!_signatureTip.IsVisible || _signatureText.Inlines is null)
            {
                return null;
            }

            foreach (var inline in _signatureText.Inlines)
            {
                if (inline is Run { FontWeight: FontWeight.Bold } run)
                {
                    return run.Text;
                }
            }

            return null;
        }
    }

    private void UpdateSignatureHelp()
    {
        string text = _editor.Text ?? string.Empty;
        if (!_editing || text.Length == 0 || text[0] != '=')
        {
            HideSignatureHelp();
            return;
        }

        var site = FormulaSignatureScanner.Find(text, _editor.CaretIndex);
        if (site is null || !SignatureFunctions.TryGet(site.FunctionName, out var function))
        {
            HideSignatureHelp();
            return;
        }

        _signatureText.Inlines = BuildSignatureInlines(function.Metadata, site.ArgumentIndex);
        _signatureTip.Background = _palette.PopupSurface;
        _signatureTip.BorderBrush = _palette.GridLine.Brush;
        _signatureText.Foreground = _palette.DefaultText;
        _signatureTip.IsVisible = true;
        InvalidateArrange();
    }

    private void HideSignatureHelp()
    {
        if (_signatureTip.IsVisible)
        {
            _signatureTip.IsVisible = false;
            InvalidateArrange();
        }
    }

    private static InlineCollection BuildSignatureInlines(FunctionMetadata meta, int activeIndex)
    {
        var args = meta.Arguments;

        // A repeatable trailing argument (e.g. SUM's number2, …) covers any further positions.
        int active = activeIndex;
        if (args.Count > 0 && args[^1].Repeatable && active >= args.Count)
        {
            active = args.Count - 1;
        }

        var inlines = new InlineCollection { new Run(meta.Name + "(") };
        for (int i = 0; i < args.Count; i++)
        {
            if (i > 0)
            {
                inlines.Add(new Run(", "));
            }

            var arg = args[i];
            string label = arg.Repeatable ? arg.Name + ", …" : arg.Name;
            if (!arg.Required)
            {
                label = "[" + label + "]";
            }

            var run = new Run(label);
            if (i == active)
            {
                run.FontWeight = FontWeight.Bold;
                run.Foreground = SignatureActiveBrush;
            }

            inlines.Add(run);
        }

        inlines.Add(new Run(")"));
        return inlines;
    }
}
