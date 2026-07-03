using Puffinote.Components.Commands;

namespace Puffinote.Desktop.Commands;

/// <summary>One collision: an <see cref="Editor"/> command bound to the same gesture as a <see cref="Reserved"/> shell command.</summary>
internal readonly record struct ShortcutConflict(AppCommand Reserved, AppCommand Editor);

/// <summary>
/// Detects where an embedded editor's shortcuts collide with Puffinote's <em>reserved</em> global keys
/// (PRD §7.6). A reserved key always wins at runtime, so the editor binding is shadowed; the shell warns
/// the user once with a friendly, actionable modal. Pure and headless-testable.
/// </summary>
internal static class ShortcutConflictScanner
{
    public static IReadOnlyList<ShortcutConflict> Find(
        IEnumerable<AppCommand> reserved, IEnumerable<AppCommand> editor)
    {
        var reservedList = reserved.Where(c => c.Gesture is not null).ToList();
        var conflicts = new List<ShortcutConflict>();
        foreach (var e in editor)
        {
            if (e.Gesture is not { } eg)
            {
                continue;
            }

            foreach (var r in reservedList)
            {
                if (r.Gesture is { } rg && rg.Key == eg.Key && rg.KeyModifiers == eg.KeyModifiers)
                {
                    conflicts.Add(new ShortcutConflict(r, e));
                    break; // one reserved owner per editor binding is enough to report
                }
            }
        }

        return conflicts;
    }
}
