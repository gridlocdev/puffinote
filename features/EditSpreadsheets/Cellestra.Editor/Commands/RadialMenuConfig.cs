using System;
using System.Collections.Generic;

namespace Cellestra.Desktop.Commands;

/// <summary>
/// User configuration for the press-and-hold radial command menu (the right-click-hold pie): how
/// many segments it has and which <see cref="AppCommand"/> (by id) each segment runs. It reads the one
/// command catalog (Golden Rule #4), so a segment is just a command id — never a second copy of a
/// command. Each wedge shows its icon, with the command's title surfacing as a tooltip on hover.
/// Persisted as part of the app settings JSON and pushed into the editor like the other view state.
/// </summary>
public sealed record RadialMenuConfig
{
    /// <summary>A radial needs at least two wedges to point between; more than a dozen is unusable.</summary>
    public const int MinSegments = 2;

    public const int MaxSegments = 12;

    public const int DefaultSegmentCount = 6;

    /// <summary>How many wedges the radial is divided into.</summary>
    public int SegmentCount { get; init; } = DefaultSegmentCount;

    /// <summary>
    /// The command id bound to each segment, clockwise from the top. A null/blank entry is an
    /// unbound (inert) segment. Stored verbatim; call <see cref="Normalized"/> before use so the list
    /// length matches <see cref="SegmentCount"/>.
    /// </summary>
    public List<string?> CommandIds { get; init; } = new();

    /// <summary>
    /// The commands a fresh radial seeds its segments with, in order — the most common cell actions.
    /// New segments (when the count grows past what the user has bound) fill from this list.
    /// </summary>
    public static IReadOnlyList<string> DefaultCommandIds { get; } = new[]
    {
        "edit.cut", "edit.copy", "edit.paste", "edit.undo", "edit.redo", "edit.delete",
        "format.bold", "edit.autoSum", "data.toggleFilter", "format.cells", "insert.rowsAbove", "edit.clearAll",
    };

    /// <summary>A ready-to-use default radial (six segments, seeded from the common actions).</summary>
    public static RadialMenuConfig Default => new RadialMenuConfig().Normalized();

    /// <summary>The command id bound to <paramref name="index"/>, or null when unbound / out of range.</summary>
    public string? CommandIdAt(int index) =>
        index >= 0 && index < CommandIds.Count && !string.IsNullOrWhiteSpace(CommandIds[index])
            ? CommandIds[index]
            : null;

    /// <summary>
    /// A copy whose <see cref="SegmentCount"/> is clamped to the allowed range and whose
    /// <see cref="CommandIds"/> is exactly that long: existing bindings (including a deliberate unbound
    /// null) are kept, and any newly-exposed slot is seeded from <see cref="DefaultCommandIds"/>.
    /// </summary>
    public RadialMenuConfig Normalized()
    {
        int count = Math.Clamp(SegmentCount, MinSegments, MaxSegments);
        var ids = new List<string?>(count);
        for (int i = 0; i < count; i++)
        {
            if (i < CommandIds.Count)
            {
                // Preserve the stored binding, normalizing blank/whitespace to a clean unbound null.
                string? id = CommandIds[i];
                ids.Add(string.IsNullOrWhiteSpace(id) ? null : id);
            }
            else
            {
                // A slot the user has never seen (the count just grew) seeds from the common actions.
                ids.Add(i < DefaultCommandIds.Count ? DefaultCommandIds[i] : null);
            }
        }

        return this with { SegmentCount = count, CommandIds = ids };
    }
}
