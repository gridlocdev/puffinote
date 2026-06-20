using System.Text;
using Avalonia.Input;

namespace Cellestra.Desktop.Commands;

/// <summary>Renders a <see cref="KeyGesture"/> using macOS modifier symbols (⌘ ⇧ ⌥ ⌃).</summary>
public static class ShortcutFormatter
{
    public static string Format(KeyGesture gesture)
    {
        // "?" is inherently a shifted key; show it bare rather than "⇧?".
        if (gesture.Key == Key.OemQuestion && gesture.KeyModifiers == KeyModifiers.Shift)
        {
            return "?";
        }

        var sb = new StringBuilder();
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            sb.Append('⌃');
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            sb.Append('⌥');
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            sb.Append('⇧');
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            sb.Append('⌘');
        }

        sb.Append(KeyText(gesture.Key));
        return sb.ToString();
    }

    private static string KeyText(Key key) => key switch
    {
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Return => "↩",
        Key.Delete or Key.Back => "⌫",
        Key.Escape => "⎋",
        Key.OemPlus => "=",
        Key.OemMinus => "−",
        // Top-row digit keys render as their bare digit (Key.D1 → "1"), not "D1".
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        Key.F9 => "F9",
        >= Key.A and <= Key.Z => key.ToString(),
        _ => key.ToString(),
    };
}
