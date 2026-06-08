using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.Icons;

namespace Cellestra.Desktop.Controls.CommandBar;

/// <summary>
/// Builds the contents of a group's overflow drawer: the full set of that group's tools, each as a
/// labelled, described row (recognition over recall). Returns the panel plus a refresh action the
/// flyout runs on open so rows reflect the current selection. Enriched with colour swatches and a
/// font-size stepper for the Text group by a later stage.
/// </summary>
public static class CommandBarDrawer
{
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    public static (Control Content, Action Refresh) Build(CommandBarGroup group, IAppEnvironment env, Action close)
    {
        var panel = new StackPanel { Spacing = 3, MinWidth = 248 };
        panel.Children.Add(new TextBlock
        {
            Text = group.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            Margin = new Thickness(2, 0, 0, 8),
        });

        var refreshers = new List<Action>();
        foreach (string id in group.Overflow)
        {
            if (AppCommands.ById(id) is { } command)
            {
                var (row, refresh) = BuildRow(command, env, close);
                panel.Children.Add(row);
                refreshers.Add(refresh);
            }
        }

        void Refresh()
        {
            foreach (var refresh in refreshers)
            {
                refresh();
            }
        }

        return (panel, Refresh);
    }

    private static (Control Row, Action Refresh) BuildRow(AppCommand command, IAppEnvironment env, Action close)
    {
        var icon = new LucideIcon { IconKey = command.Icon, Size = 16, VerticalAlignment = VerticalAlignment.Center };
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = command.Label, FontSize = 13, FontWeight = FontWeight.Medium },
                new TextBlock { Text = command.Description, FontSize = 11, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap },
            },
        };

        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(icon, Dock.Left);
        layout.Children.Add(icon);
        if (!string.IsNullOrEmpty(command.ShortcutText))
        {
            var shortcut = new TextBlock
            {
                Text = command.ShortcutText,
                FontSize = 10.5,
                Foreground = MutedBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            DockPanel.SetDock(shortcut, Dock.Right);
            layout.Children.Add(shortcut);
        }

        icon.Margin = new Thickness(0, 0, 11, 0);
        layout.Children.Add(text);

        var row = new Button
        {
            Content = layout,
            Background = Brushes.Transparent,
            BorderThickness = default,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        row.Click += (_, _) =>
        {
            if (command.IsEnabled(env))
            {
                command.Execute(env);
            }

            close();
        };

        void Refresh()
        {
            row.IsEnabled = command.IsEnabled(env);
            bool on = command.IsToggleActive(env);
            row.Background = on ? CommandBar.ActiveBackground : Brushes.Transparent;
            if (on)
            {
                icon.Foreground = CommandBar.ActiveForeground;
            }
            else
            {
                icon.ClearValue(LucideIcon.ForegroundProperty);
            }
        }

        return (row, Refresh);
    }
}
