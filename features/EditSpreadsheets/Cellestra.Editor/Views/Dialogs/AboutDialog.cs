using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BirdBrain.Components.Controls.Icons;
using Cellestra.Desktop.Views.Overlays;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// The About screen (PRD §6.2 "About Cellestra"), rendered as an internal modal. Shows the app name,
/// its "CELL-estra" pronunciation, a one-line description, the version, and the MIT license. The
/// bool result is unused.
/// </summary>
public sealed class AboutDialog : ModalDialog<bool>
{
    /// <summary>The product version, read from the assembly's informational version (falls back to a default).</summary>
    private static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            is { Length: > 0 } v
            ? TrimBuildMetadata(v)
            : "1.0.0";

    private static string TrimBuildMetadata(string version)
    {
        int plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }

    public AboutDialog()
    {
        Title = "About Cellestra";

        DialogContent = new StackPanel
        {
            Width = 360,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new LucideIcon { IconKey = "info", Size = 40, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock
                {
                    Text = "Cellestra",
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                new TextBlock
                {
                    // From the PRD/README: cell + orchestra, pronounced cel-LES-trah.
                    Text = "from cell + orchestra · pronounced “cel-LES-trah”",
                    Foreground = Muted,
                    FontSize = 12,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                new TextBlock
                {
                    Text = "A clean, fast, native spreadsheet editor for macOS whose home format is OpenDocument Spreadsheet (.ods).",
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0),
                },
                new TextBlock
                {
                    Text = $"Version {Version}",
                    Foreground = Muted,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0),
                },
                new TextBlock
                {
                    Text = "MIT License · © 2026 Cellestra contributors",
                    Foreground = Muted,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                BuildOkButton(),
            },
        };
    }

    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    private Button BuildOkButton()
    {
        var ok = new Button
        {
            Content = "OK",
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
        };
        ok.Click += (_, _) => Close();
        return ok;
    }
}
