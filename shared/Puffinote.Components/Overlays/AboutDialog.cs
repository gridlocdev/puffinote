using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Theming;

namespace Puffinote.Components.Overlays;

/// <summary>
/// The About screen every app in the family shows from its macOS application menu: the app's name, an
/// optional line about where the name comes from, a sentence on what it is, the version, and the licence.
/// <para>
/// Shared because five apps want the same card with four strings changed, and because the version is the
/// part that is easy to get wrong once per app — it comes from the <em>entry</em> assembly, so it is the
/// running app's version rather than whichever library the dialog happens to be compiled into.
/// </para>
/// </summary>
public sealed class AboutDialog : ModalDialog<bool>
{
    /// <param name="appName">The product name, as it is written everywhere else.</param>
    /// <param name="description">One sentence on what the app is.</param>
    /// <param name="subtitle">Optional: where the name comes from, how it is said. Null omits the line.</param>
    public AboutDialog(string appName, string description, string? subtitle = null)
    {
        Title = $"About {appName}";

        var panel = new StackPanel
        {
            Width = 360,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new LucideIcon { IconKey = "info", Size = 40, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock
                {
                    Text = appName,
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            },
        };

        if (subtitle is { Length: > 0 })
        {
            panel.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = Tokens.Active.MutedText,
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"Version {Version}",
            Foreground = Tokens.Active.MutedText,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"MIT License · © 2026 {appName} contributors",
            Foreground = Tokens.Active.MutedText,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var ok = new Button
        {
            Content = "OK",
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
        };
        ok.Click += (_, _) => Close();
        panel.Children.Add(ok);

        DialogContent = panel;
    }

    /// <summary>
    /// The running app's product version. Read from the <b>entry</b> assembly, not this one: this type
    /// lives in a shared library, and its version is not what anyone means by "what version am I running?".
    /// </summary>
    public static string Version =>
        (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            is { Length: > 0 } v
            ? TrimBuildMetadata(v)
            : "1.0.0";

    private static string TrimBuildMetadata(string version)
    {
        int plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? version[..plus] : version;
    }
}
