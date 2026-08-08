using Avalonia.Input;
using Puffinote.Components.Theming;

namespace Puffinote.Components.Commands;

/// <summary>
/// The three zoom commands, defined once for the whole family. Every editor's catalog splices these in over
/// its own zoom factor, so Zoom In is the same id, the same title, the same icon and the same chord in a
/// note, a sheet, a log and a board — including inside Puffinote, where a reader meets all four in one
/// window and one meaning per chord is the whole point.
/// <para>
/// The chords are <b>⌘=</b> in, <b>⌘−</b> out, and <b>⌥⌘0</b> back to 100%. What the menus spell as "⌘+" is
/// really ⌘= — no keyboard has an unshifted "+" on the number row — so the shifted form and the numeric
/// keypad's own keys ride along as <see cref="AppCommand.AlternateShortcuts"/> rather than as extra rows a
/// reader has to learn.
/// </para>
/// <para>
/// Reset takes <b>⌥⌘0</b> rather than the more usual ⌘0 because ⌘0 is already the last rung of Typage's
/// heading ladder (⌘1–⌘6 apply a level, ⌘0 removes it). Spending it on zoom in the sibling apps would make
/// one chord mean two things depending on which Puffinote tab is open, so the whole family steps aside
/// together — one meaning per chord is worth more than matching a convention in four apps and breaking it
/// in the fifth.
/// </para>
/// </summary>
public static class ZoomCommands
{
    /// <summary>The id of the "Zoom In" command, so a host can find it in a catalog by name.</summary>
    public const string ZoomInId = "view.zoomIn";

    public const string ZoomOutId = "view.zoomOut";

    public const string ActualSizeId = "view.actualSize";

    /// <summary>
    /// Build the three commands over one zoom factor. <paramref name="get"/> reads the editor's current
    /// factor and <paramref name="set"/> applies a new one (the editor clamps and repaints); nothing else
    /// about the editor is assumed, which is why one factory serves four unrelated editing surfaces.
    /// </summary>
    public static IReadOnlyList<AppCommand> Build(Func<double> get, Action<double> set)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        return
        [
            new(ZoomInId, "Zoom In", "Make everything larger.",
                Shortcut.Cmd(Key.OemPlus), () => set(ZoomScale.In(get())),
                canExecute: () => get() < ZoomScale.Max, icon: "zoom-in",
                alternateShortcuts: [Shortcut.Cmd(Key.OemPlus, KeyModifiers.Shift), Shortcut.Cmd(Key.Add)]),

            new(ZoomOutId, "Zoom Out", "Make everything smaller.",
                Shortcut.Cmd(Key.OemMinus), () => set(ZoomScale.Out(get())),
                canExecute: () => get() > ZoomScale.Min, icon: "zoom-out",
                alternateShortcuts: [Shortcut.Cmd(Key.Subtract)]),

            new(ActualSizeId, "Actual Size", "Reset zoom to 100%.",
                Shortcut.Cmd(Key.D0, KeyModifiers.Alt), () => set(ZoomScale.Default),
                canExecute: () => Math.Abs(get() - ZoomScale.Default) > 0.001d, icon: "maximize",
                alternateShortcuts: [Shortcut.Cmd(Key.NumPad0, KeyModifiers.Alt)]),
        ];
    }
}
