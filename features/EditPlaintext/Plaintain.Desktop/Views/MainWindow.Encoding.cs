using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Overlays;
using Puffinote.Components.Theming;
using Puffinote.Text.Document;

namespace Plaintain.Desktop.Views;

/// <summary>
/// Changing which encoding a file is written in — the action that actually rescues a Latin-1 file, as
/// opposed to <see cref="ConfirmEncodingAsync"/>, which only agrees to write the guess back unchanged.
/// <para>
/// It is an explicit command and will never be anything else. Plaintain's whole file contract (PRD §4,
/// golden rule 5) is that bytes are preserved exactly, and an editor that quietly "upgraded" files to
/// UTF-8 on save would be rewriting documents nobody asked it to touch. So: a menu item, a dialog that
/// says what it is about to do, and no automatic behaviour anywhere.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// The encodings offered, in the order a person would look for them. Latin-1 is on the list because
    /// converting <em>to</em> it is occasionally what someone needs (a tool downstream that cannot read
    /// anything else), not only because files arrive in it.
    /// </summary>
    private static readonly IReadOnlyList<TextFileFormat> ConvertibleFormats =
    [
        TextFileFormat.Utf8NoBom,
        TextFileFormat.Utf8Bom,
        TextFileFormat.Utf16Le,
        TextFileFormat.Utf16Be,
        TextFileFormat.Latin1,
    ];

    /// <summary>
    /// Ask which encoding to write this file in, adopt it, and — for a file that has one — write it out
    /// there and then, because "convert the encoding" that leaves the file unconverted until some later
    /// save is not the thing anyone asked for.
    /// </summary>
    private async Task ConvertEncodingAsync()
    {
        var chooser = new ComboBox
        {
            ItemsSource = ConvertibleFormats.Select(f => f.DisplayName).ToList(),
            SelectedIndex = Math.Max(0, ConvertibleFormats.ToList().FindIndex(f => f.DisplayName == _format.DisplayName)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var body = new StackPanel
        {
            Spacing = 10,
            Width = 380,
            Children =
            {
                new TextBlock
                {
                    Text = $"This file is currently read and written as {_format.DisplayName}. " +
                           "Converting rewrites its bytes in the encoding you pick — the characters on screen stay " +
                           "as they are.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Tokens.Active.MutedText,
                    FontSize = 12,
                },
                chooser,
                new TextBlock
                {
                    // The one thing someone converting a mis-decoded file has to understand: what is on
                    // screen is what gets written. If the text looks wrong now, converting makes it wrong
                    // on disk — the fix for that is to reopen the file, not to convert it.
                    Text = "If the text on screen already looks wrong, it was decoded wrongly — converting would "
                         + "write that wrongness to disk. Close and reopen the file instead.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Tokens.Active.MutedText,
                    FontSize = 11,
                },
            },
        };

        if (!await _overlay.ShowModalAsync(new FormDialog("Convert encoding", body, okLabel: "Convert"))
            || chooser.SelectedIndex < 0)
        {
            return;
        }

        AdoptFormat(ConvertibleFormats[chooser.SelectedIndex]);
    }

    /// <summary>
    /// Adopt <paramref name="format"/> as the one this document is written in, and write it now when it
    /// has somewhere to go. Choosing an encoding by hand is a <em>declaration</em>, so it also settles the
    /// unconfirmed-guess state: the file is no longer being written through something nobody vouched for.
    /// </summary>
    private void AdoptFormat(TextFileFormat format)
    {
        _format = format;
        EncodingUnconfirmed = false;
        EditorView.Editor.IsReadOnlyEncoding = false;

        if (CurrentPath is { } path)
        {
            WriteToPath(path);
        }

        UpdateChrome();
    }

    // ---- Test seams ------------------------------------------------------

    /// <summary>Test seam: convert to <paramref name="format"/> as the dialog's Convert button would.</summary>
    internal void ConvertEncodingForTest(TextFileFormat format) => AdoptFormat(format);

    /// <summary>Test seam: the encodings the conversion dialog offers.</summary>
    internal static IReadOnlyList<string> ConvertibleEncodingNamesForTest =>
        [.. ConvertibleFormats.Select(f => f.DisplayName)];

    /// <summary>
    /// Test seam: take down whatever notice is on the overlay, as clicking its button would. Opening a
    /// file whose encoding was guessed raises one, and a headless suite has nobody to dismiss it — a modal
    /// left standing takes the whole test run down with it.
    /// </summary>
    internal void DismissDialogForTest() => _overlay.Close();
}
