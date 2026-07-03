using System.CommandLine;

namespace Puffinote.Cli.Commands;

/// <summary>
/// Composes the two sub-CLIs under <c>md</c> and <c>sheet</c> parents (ARCHITECTURE.md §11). Each
/// standalone CLI already exposes its whole command tree through <c>CliApplication.BuildRoot()</c>;
/// the router grafts that root's subcommands under a namespacing parent, so <c>puffinote md html …</c>
/// runs Typage's <c>html</c> and <c>puffinote sheet set …</c> runs Cellestra's <c>set</c> — with no
/// duplicated command definitions and the standalone binaries untouched.
/// </summary>
internal static class Router
{
    public static Command Markdown() =>
        Route("md", "Markdown tools (Typage) — run a typage subcommand against .md files.",
            Typage.Cli.CliApplication.BuildRoot());

    public static Command Sheet() =>
        Route("sheet", "Spreadsheet tools (Cellestra) — run a cellestra subcommand against .ods files.",
            Cellestra.Cli.CliApplication.BuildRoot());

    // Move a freshly-built sub-CLI root's subcommands under a new parent. The root is discarded; its
    // subcommands are fresh instances built by this call, so re-parenting them is safe (validated
    // against System.CommandLine 2.0.8, which lets a command sit under a new parent).
    private static Command Route(string name, string description, RootCommand subRoot)
    {
        var parent = new Command(name, description);
        foreach (var sub in subRoot.Subcommands.ToList())
        {
            parent.Subcommands.Add(sub);
        }

        return parent;
    }
}
