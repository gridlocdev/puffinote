namespace Puffinote.Desktop.Shell;

/// <summary>
/// Pure ordering logic for the sidebar's pinned-folder chips (PRD §7.2): an ordered, de-duplicated list of
/// absolute folder paths that the user can pin, unpin, and drag to reorder. Case-insensitive on paths (to
/// match the rest of the tree's path handling) and side-effect-free — every operation returns a new list, so
/// it is unit-testable without a window. The <see cref="FilesPanel"/> owns the live list and the persistence;
/// this just decides what the next order is.
/// </summary>
internal static class PinnedFolders
{
    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="path"/> is already pinned.</summary>
    public static bool Contains(IReadOnlyList<string> pins, string path) => pins.Any(p => Eq(p, path));

    /// <summary>Append <paramref name="path"/> to the end (a no-op if it is already pinned).</summary>
    public static IReadOnlyList<string> Pin(IReadOnlyList<string> pins, string path) =>
        Contains(pins, path) ? pins : [.. pins, path];

    /// <summary>Remove <paramref name="path"/> (a no-op when it isn't pinned).</summary>
    public static IReadOnlyList<string> Unpin(IReadOnlyList<string> pins, string path) =>
        [.. pins.Where(p => !Eq(p, path))];

    /// <summary>
    /// Move <paramref name="src"/> to sit just before (or after) <paramref name="target"/>. Returns the list
    /// unchanged when either path isn't pinned or they are the same — mirrors the prototype's drag-reorder.
    /// </summary>
    public static IReadOnlyList<string> Reorder(IReadOnlyList<string> pins, string src, string target, bool before)
    {
        if (Eq(src, target) || !Contains(pins, src) || !Contains(pins, target))
        {
            return pins;
        }

        var list = pins.Where(p => !Eq(p, src)).ToList();
        int index = list.FindIndex(p => Eq(p, target));
        if (!before)
        {
            index += 1;
        }

        list.Insert(index, src);
        return list;
    }

    /// <summary>Move <paramref name="path"/> to the front (dropping a chip onto "All notes").</summary>
    public static IReadOnlyList<string> MoveToFront(IReadOnlyList<string> pins, string path) =>
        Contains(pins, path) ? [path, .. pins.Where(p => !Eq(p, path))] : pins;
}
