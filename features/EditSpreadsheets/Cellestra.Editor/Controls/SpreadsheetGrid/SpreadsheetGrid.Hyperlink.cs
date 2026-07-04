using Avalonia.Controls;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Hyperlink activation for the grid (PRD §4 Data: Hyperlinks). A cell's <see cref="Hyperlink"/> is
/// rendered as a link in the draw path (see <c>DrawCellContent</c>); this partial opens it on
/// <b>⌘-click</b> (and <b>⌘+Return</b> on the active cell), routed through Avalonia's OS launcher —
/// never a shell-out. Only well-formed absolute <c>http</c>/<c>https</c>/<c>mailto</c>/<c>file</c>
/// URIs are launched; anything else (relative paths, unknown schemes, malformed text) is ignored
/// safely. A hover hint ("⌘-click to open …") makes the gesture discoverable (PRD §7 heuristic #6).
/// </summary>
public sealed partial class SpreadsheetGrid
{
    /// <summary>The hyperlinked cell covering <paramref name="row"/>/<paramref name="column"/> (an inside-merge cell maps to its anchor), or null.</summary>
    private Cell? HyperlinkedCellAt(int row, int column)
    {
        var sheet = Sheet;
        if (sheet is null)
        {
            return null;
        }

        if (sheet.FindMerge(row, column) is { } merge)
        {
            row = merge.Range.Top;
            column = merge.Range.Left;
        }

        var cell = sheet.Find(row, column);
        return cell?.Hyperlink is not null ? cell : null;
    }

    /// <summary>The "⌘-click to open …" hover hint for a hyperlinked cell, or null when there isn't one.</summary>
    internal string? HyperlinkTipAt(int row, int column)
    {
        var cell = HyperlinkedCellAt(row, column);
        return cell is null ? null : $"⌘-click to open {cell.Hyperlink!.Target}";
    }

    /// <summary>
    /// Open the hyperlink on the cell at <paramref name="row"/>/<paramref name="column"/> (or its
    /// merge anchor) with the OS default handler. Returns true when a launchable link was found and a
    /// launch attempted — the caller treats that as a handled activation; false when the cell has no
    /// link or its target isn't a safe, launchable URI (in which case selection/navigation proceeds).
    /// </summary>
    private bool TryActivateHyperlink(int row, int column)
    {
        var cell = HyperlinkedCellAt(row, column);
        if (cell is null || !TryGetLaunchableUri(cell.Hyperlink!.Target, out var uri))
        {
            return false;
        }

        // TopLevel.Launcher hands the URI to the OS (Golden Rule: never shell out). Fire-and-forget;
        // a missing launcher (e.g. a detached test host) is a safe no-op, still a handled activation.
        TopLevel.GetTopLevel(this)?.Launcher?.LaunchUriAsync(uri);
        return true;
    }

    /// <summary>The schemes the grid will hand to the OS launcher, each with its <c>scheme:</c> prefix.</summary>
    private static readonly string[] AllowedSchemePrefixes =
    [
        Uri.UriSchemeHttp + ":",
        Uri.UriSchemeHttps + ":",
        Uri.UriSchemeMailto + ":",
        Uri.UriSchemeFile + ":",
    ];

    /// <summary>
    /// The URI-safety guard (pure, unit-testable): accept <paramref name="target"/> only when it both
    /// <b>explicitly declares</b> one of the allowed schemes (<c>http</c>, <c>https</c>, <c>mailto</c>,
    /// <c>file</c>) and parses as a well-formed <b>absolute</b> URI of that scheme. Requiring the
    /// literal <c>scheme:</c> prefix stops a bare filesystem path (e.g. <c>/etc/passwd</c>, which .NET
    /// would otherwise promote to a <c>file:</c> URI on Unix), a relative path, an unknown/empty
    /// scheme, or malformed text from ever being launched. Comparison is case-insensitive.
    /// </summary>
    public static bool TryGetLaunchableUri(string? target, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        string trimmed = target.Trim();
        bool declaresAllowedScheme = false;
        foreach (var prefix in AllowedSchemePrefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                declaresAllowedScheme = true;
                break;
            }
        }

        if (!declaresAllowedScheme || !Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        bool allowed = parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || parsed.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase)
            || parsed.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase);
        if (!allowed)
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
