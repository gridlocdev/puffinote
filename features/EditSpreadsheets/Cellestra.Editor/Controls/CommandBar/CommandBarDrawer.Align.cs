using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Commands;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Components.Controls.Icons;
using VAlign = Cellestra.Core.Styling.VerticalAlignment;

namespace Cellestra.Desktop.Controls.CommandBar;

public static partial class CommandBarDrawer
{
    // ---- Align-group extra: vertical alignment ----------------------------

    private static Control VerticalAlignSection(IAppEnvironment env, Action close, out Action refresh)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var repainters = new List<Action>();

        (string Label, VAlign Value, string Icon)[] options =
        [
            ("Top", VAlign.Top, "align-vertical-justify-start"),
            ("Middle", VAlign.Middle, "align-vertical-justify-center"),
            ("Bottom", VAlign.Bottom, "align-vertical-justify-end"),
        ];

        foreach (var (label, value, iconKey) in options)
        {
            var icon = new LucideIcon
            {
                IconKey = iconKey,
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
            ToolTip.SetTip(button, $"Align {label}");
            Avalonia.Automation.AutomationProperties.SetName(button, $"Align {label}");

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
            button.Click += (_, _) =>
            {
                env.ApplyCellStyle(s => s.WithAlignment(s.Alignment with { Vertical = value }), "Vertical align");
                env.Narrate($"Align {label.ToLowerInvariant()} · {env.Selection.ActiveAddress}");
                close();
            };

            repainters.Add(() =>
            {
                on = ActiveStyle(env).Alignment.Vertical == value;
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
