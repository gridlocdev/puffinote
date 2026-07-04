using System.CommandLine;
using Puffinote.Components.Core.Cli;
using Typage.Cli.Commands;
using Typage.Cli.Infrastructure;

namespace Typage.Cli;

/// <summary>
/// Builds the command tree and runs it. Factored out of <c>Program</c> so tests can invoke the
/// same surface in-process. A second consumer of <see cref="Typage.Engine"/> alongside the
/// Desktop shell — no Avalonia, no network; it only touches the files it is pointed at.
/// </summary>
public static class CliApplication
{
    public static RootCommand BuildRoot()
    {
        return new RootCommand("typage — a dependency-light Markdown toolkit for scripts and CI.")
        {
            MarkdownCommands.Html(),
            MarkdownCommands.Cat(),
            MarkdownCommands.Tokens(),
            MarkdownCommands.Stats(),
            MarkdownCommands.Outline(),
        };
    }

    public static int Run(string[] args) =>
        // The shared harness handles the common expected-error set (CliException, bad input, missing file).
        CliHost.Run(BuildRoot(), args, "typage");
}
