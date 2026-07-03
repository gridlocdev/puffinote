using System.CommandLine;
using Puffinote.Cli.Commands;
using Puffinote.Components.Core.Cli;

namespace Puffinote.Cli;

/// <summary>
/// Builds the router's command tree and runs it (ARCHITECTURE.md §11). Factored out of
/// <c>Program</c> so tests can invoke the same surface in-process, exactly like the sub-CLIs.
/// The tree is: notebook-scoped commands (<c>tree</c>/<c>search</c>/<c>info</c>) that read the notebook
/// directly, plus <c>md …</c> → Typage and <c>sheet …</c> → Cellestra, which reuse the standalone
/// CLIs' own command definitions (single source of truth — the <c>typage</c>/<c>cellestra</c>
/// binaries keep working unchanged).
/// </summary>
public static class CliApplication
{
    public static RootCommand BuildRoot()
    {
        return new RootCommand(
            "puffinote — a unified notebook, Markdown, and spreadsheet toolkit (routes to typage and cellestra).")
        {
            // Notebook-scoped commands — the headless face of the sidebar's file tree + search (PRD §7.2).
            NotebookCommands.Tree(),
            NotebookCommands.Search(),
            NotebookCommands.Info(),

            // Routed sub-CLIs — reuse each app's command tree verbatim (Cellestra Golden Rule #4 /
            // Typage single-catalog rule): defined once in the sub-app, composed here at build time.
            Router.Markdown(),
            Router.Sheet(),
        };
    }

    public static int Run(string[] args) =>
        // The shared harness reports the common expected-error set (CliException, bad input, missing
        // file) as a clean `puffinote: …` line. Fold in Cellestra's extra expected types so a routed
        // `sheet` command degrades the same way it does under standalone cellestra rather than
        // surfacing a stack trace (an unmodeled sheet / malformed-JSON read is user error, not a bug).
        CliHost.Run(BuildRoot(), args, "puffinote",
            ex => ex is InvalidOperationException or System.Text.Json.JsonException);
}
