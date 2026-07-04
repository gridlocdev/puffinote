using Puffinote.Components.Commands;
using Puffinote.Desktop.Commands;
using Xunit;

namespace Puffinote.Tests;

public sealed class CommandMergeTests
{
    private static AppCommand Cmd(string id, string title, bool enabled = true, Action? run = null) =>
        new(id, title, "desc", null, run ?? (() => { }), () => enabled);

    [Fact]
    public void Merges_shell_then_editor_and_drops_disabled_commands()
    {
        var shell = new[] { Cmd("s.a", "Alpha"), Cmd("s.b", "Beta", enabled: false) };
        var editor = new[] { Cmd("e.a", "Gamma") };

        var entries = CommandMerge.ToEntries(shell, editor);

        Assert.Equal(2, entries.Count); // Beta is disabled → filtered out
        Assert.Equal(("Alpha", CommandMerge.ShellCategory), (entries[0].Title, entries[0].Category));
        Assert.Equal(("Gamma", CommandMerge.EditorCategory), (entries[1].Title, entries[1].Category));
    }

    [Fact]
    public void Entry_run_invokes_the_underlying_command()
    {
        var ran = 0;
        var entries = CommandMerge.ToEntries([Cmd("s.a", "A", run: () => ran++)], []);

        entries[0].Run();

        Assert.Equal(1, ran);
    }
}
