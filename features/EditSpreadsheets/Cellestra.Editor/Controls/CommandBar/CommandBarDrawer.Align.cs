using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Commands;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Components.Controls.Icons;

namespace Cellestra.Desktop.Controls.CommandBar;

public static partial class CommandBarDrawer
{
    // ---- Align-group extra: vertical alignment ----------------------------

    // The vertical-alignment commands, by catalog id (Golden Rule #4). These buttons used to define
    // the three actions themselves, which is why vertical alignment reached the bar and nothing else —
    // not the Command Menu, not the menu bar, not the radial. The catalog owns them now; this section
    // only lays them out, keeping the axis on one row of icons rather than three labelled rows.
    private static readonly string[] VerticalAlignIds =
        ["format.alignTop", "format.alignMiddle", "format.alignBottom"];

    private static Control VerticalAlignSection(IAppEnvironment env, Action close, out Action refresh)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var repainters = new List<Action>();

        foreach (var command in VerticalAlignIds.Select(env.Commands.ById).OfType<AppCommand>())
        {
            var icon = new LucideIcon
            {
                IconKey = command.Icon,
                Size = 17,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var button = new Button
            {
                Content = icon,
                Height = 30,
                Padding = new Thickness(12, 0),
                FontSize = 12.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = Brushes.Transparent,
                BorderThickness = default,
                CornerRadius = new CornerRadius(6),
            };
            ToolTip.SetTip(button, command.Title);
            Avalonia.Automation.AutomationProperties.SetName(button, command.Title);

            bool on = false;
            bool hover = false;
            void Paint() => button.Background = on ? CommandBarChrome.ActiveBackground : hover ? CommandBarChrome.HoverBackground : Brushes.Transparent;
            button.PointerEntered += (_, _) =>
            {
                hover = true;
                Paint();
            };
            button.PointerExited += (_, _) =>
            {
                hover = false;
                Paint();
            };
            var captured = command;
            button.Click += (_, _) =>
            {
                captured.Execute();
                env.Narrate($"{captured.Title} · {env.Selection.ActiveAddress}");
                close();
            };

            repainters.Add(() =>
            {
                on = captured.IsToggleActive;
                Paint();
                if (on)
                {
                    icon.Foreground = CommandBarChrome.ActiveForeground;
                }
                else
                {
                    icon.ClearValue(LucideIcon.ForegroundProperty);
                }
            });
            row.Children.Add(button);
        }

        void Refresh()
        {
            foreach (var paint in repainters)
            {
                paint();
            }
        }

        refresh = Refresh;
        return Section("Vertical alignment", row);
    }
}
