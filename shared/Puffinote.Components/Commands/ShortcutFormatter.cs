using System.Text;
using Avalonia.Input;

namespace Puffinote.Components.Commands;

/// <summary>
/// Renders a <see cref="Shortcut"/> for display: macOS modifier symbols (⌃⌥⇧⌘, no separators) on macOS,
/// and "Ctrl+Shift+…" text elsewhere. Shared by every app in the Puffinote family so a shortcut reads
/// natively on each platform. (Menus render their own gestures: the macOS native menu draws the OS glyphs
/// from its key-equivalent; an in-window menu shows Avalonia's "Ctrl+…" text.)
/// </summary>
public static class ShortcutFormatter
{
    /// <summary>Format <paramref name="shortcut"/> for the current platform.</summary>
    public static string Format(Shortcut shortcut) => Format(shortcut, Shortcut.IsMac);

    /// <summary>Format <paramref name="shortcut"/> as it should read on macOS (<paramref name="mac"/> true) or elsewhere.</summary>
    public static string Format(Shortcut shortcut, bool mac)
    {
        var mods = shortcut.ModifiersFor(mac);

        // "?" is inherently the shifted "/" key; show it bare rather than "⇧?" / "Shift+/".
        if (shortcut.Key == Key.OemQuestion && mods == KeyModifiers.Shift)
        {
            return "?";
        }

        var sb = new StringBuilder();
        if (mac)
        {
            if (mods.HasFlag(KeyModifiers.Control))
            {
                sb.Append('⌃');
            }

            if (mods.HasFlag(KeyModifiers.Alt))
            {
                sb.Append('⌥');
            }

            if (mods.HasFlag(KeyModifiers.Shift))
            {
                sb.Append('⇧');
            }

            if (mods.HasFlag(KeyModifiers.Meta))
            {
                sb.Append('⌘');
            }
        }
        else
        {
            if (mods.HasFlag(KeyModifiers.Control))
            {
                sb.Append("Ctrl+");
            }

            if (mods.HasFlag(KeyModifiers.Alt))
            {
                sb.Append("Alt+");
            }

            if (mods.HasFlag(KeyModifiers.Shift))
            {
                sb.Append("Shift+");
            }

            if (mods.HasFlag(KeyModifiers.Meta))
            {
                sb.Append("Win+");
            }
        }

        sb.Append(KeyText(shortcut.Key));
        return sb.ToString();
    }

    private static string KeyText(Key key) => key switch
    {
        // Top-row digits render as the bare digit (Key.D1 → "1"), not "D1".
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.A and <= Key.Z => key.ToString(),
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemPlus => "=",
        Key.OemMinus => "−",
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Return => "↩",
        Key.Delete or Key.Back => "⌫",
        Key.Escape => "⎋",
        _ => key.ToString(), // F9, F11, Enter, etc. read fine as-is
    };
}
