using Puffinote.Desktop.Shell;
using Xunit;

namespace Puffinote.Tests;

/// <summary>The pure ordering logic behind the sidebar's pinned-folder chips (PRD §7.2).</summary>
public sealed class PinnedFoldersTests
{
    private static string P(string s) => s.Replace('/', System.IO.Path.DirectorySeparatorChar);

    [Fact]
    public void Pin_appends_to_the_end_and_is_idempotent()
    {
        IReadOnlyList<string> pins = [P("/v/a")];
        pins = PinnedFolders.Pin(pins, P("/v/b"));
        Assert.Equal([P("/v/a"), P("/v/b")], pins);

        // Pinning something already pinned (any case) is a no-op that returns the same instance.
        var same = PinnedFolders.Pin(pins, P("/v/A"));
        Assert.Same(pins, same);
    }

    [Fact]
    public void Unpin_removes_case_insensitively()
    {
        IReadOnlyList<string> pins = [P("/v/a"), P("/v/b")];
        Assert.Equal([P("/v/b")], PinnedFolders.Unpin(pins, P("/v/A")));
    }

    [Fact]
    public void Reorder_moves_a_chip_before_or_after_a_target()
    {
        IReadOnlyList<string> pins = [P("/v/a"), P("/v/b"), P("/v/c")];

        // Move c to sit before a → [c, a, b].
        Assert.Equal([P("/v/c"), P("/v/a"), P("/v/b")], PinnedFolders.Reorder(pins, P("/v/c"), P("/v/a"), before: true));

        // Move a to sit after c → [b, c, a].
        Assert.Equal([P("/v/b"), P("/v/c"), P("/v/a")], PinnedFolders.Reorder(pins, P("/v/a"), P("/v/c"), before: false));
    }

    [Fact]
    public void Reorder_is_a_no_op_when_a_path_is_missing_or_self_targeted()
    {
        IReadOnlyList<string> pins = [P("/v/a"), P("/v/b")];
        Assert.Same(pins, PinnedFolders.Reorder(pins, P("/v/a"), P("/v/a"), before: true));
        Assert.Same(pins, PinnedFolders.Reorder(pins, P("/v/a"), P("/v/missing"), before: true));
        Assert.Same(pins, PinnedFolders.Reorder(pins, P("/v/missing"), P("/v/a"), before: true));
    }

    [Fact]
    public void MoveToFront_hoists_a_chip_to_the_head()
    {
        IReadOnlyList<string> pins = [P("/v/a"), P("/v/b"), P("/v/c")];
        Assert.Equal([P("/v/c"), P("/v/a"), P("/v/b")], PinnedFolders.MoveToFront(pins, P("/v/c")));
    }
}
