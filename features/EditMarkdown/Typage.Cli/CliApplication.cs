using System.CommandLine;
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

    public static int Run(string[] args)
    {
        var configuration = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        try
        {
            return BuildRoot().Parse(args).Invoke(configuration);
        }
        catch (Exception ex) when (IsExpectedError(ex))
        {
            Console.Error.WriteLine($"typage: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Errors a user can cause — bad input, a missing file — are reported cleanly with exit 1.
    /// Anything else is a bug and is left to surface loudly.
    /// </summary>
    private static bool IsExpectedError(Exception ex) => ex is
        CliException or
        ArgumentException or
        IOException or
        UnauthorizedAccessException or
        FormatException;
}
