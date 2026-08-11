using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Controls;

/// <summary>
/// What a workspace that isn't built yet shows: its glyph, its name, the one sentence saying what will
/// live here, and the fact that it doesn't yet.
///
/// <para>A rail entry that led to a blank page would read as a broken app; one that led nowhere at all
/// would leave the rail with a single entry and no shape. Saying "not built yet" in the place the thing
/// will be is the honest version of both — and it is the same rule the status tables in these docs
/// follow: never let something claim to be finished, and never hide that it is planned.</para>
/// </summary>
internal sealed class WorkspacePlaceholder : UserControl
{
    public WorkspacePlaceholder(WorkspaceInfo workspace)
    {
        var palette = Tokens.Active;

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            MaxWidth = 380,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new LucideIcon
                {
                    IconKey = workspace.Icon,
                    Size = 36,
                    Foreground = palette.MutedText,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                new TextBlock
                {
                    Text = workspace.Title,
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = palette.Text,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                new TextBlock
                {
                    Text = workspace.Promise,
                    FontSize = 12,
                    Foreground = palette.MutedText,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                },
                new TextBlock
                {
                    Text = "Not built yet.",
                    FontSize = 12,
                    Foreground = palette.MutedText,
                    Opacity = 0.75,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            },
        };

        AutomationProperties.SetName(stack, $"{workspace.Title} — not built yet");
        Content = new Border { Background = palette.SurfaceInset, Padding = new Thickness(24), Child = stack };
    }
}
