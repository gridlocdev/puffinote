using System.CommandLine;
using Cellestra.Cli.Commands;
using Cellestra.Cli.Infrastructure;

namespace Cellestra.Cli;

/// <summary>
/// Builds the command tree and runs it. Factored out of <c>Program</c> so tests can invoke the
/// same surface in-process. No network, ever — it only touches the files it's pointed at.
/// </summary>
public static class CliApplication
{
    public static RootCommand BuildRoot()
    {
        return new RootCommand("Cellestra — a keyboard-friendly .ods spreadsheet toolkit for scripts and CI.")
        {
            // Baseline — the genuinely useful core.
            BaselineCommands.Convert(),
            BaselineCommands.Cat(),
            BaselineCommands.Get(),
            BaselineCommands.Set(),
            BaselineCommands.Recalc(),
            BaselineCommands.Sheets(),
            BaselineCommands.Info(),
            BaselineCommands.Eval(),
            BaselineCommands.Validate(),

            // Extras.
            ExtraCommands.New(),
            ExtraCommands.Import(),
            ExtraCommands.Export(),
            ExtraCommands.Stats(),
            ExtraCommands.Find(),
            ExtraCommands.Diff(),
            ExtraCommands.Functions(),
            ExtraCommands.AddSheet(),
            ExtraCommands.RenameSheet(),
            ExtraCommands.DeleteSheet(),
            ExtraCommands.Apply(),
            ExtraCommands.Open(),
            ExtraCommands.Completions(),
        };
    }

    public static int Run(string[] args)
    {
        // Turn off System.CommandLine's default handler so an error surfaces as a clean one-line
        // message here instead of a stack trace. Action return codes still flow through.
        var configuration = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        try
        {
            return BuildRoot().Parse(args).Invoke(configuration);
        }
        catch (Exception ex) when (IsExpectedError(ex))
        {
            Console.Error.WriteLine($"cellestra: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Errors a user can cause — bad input, a missing file, an invalid sheet name, a bad regex —
    /// are reported cleanly with exit 1. Anything else is a bug and is left to surface loudly.
    /// </summary>
    private static bool IsExpectedError(Exception ex) => ex is
        CliException or
        ArgumentException or          // includes invalid regex / culture / sheet-name rules
        InvalidOperationException or  // e.g. duplicate sheet name, no such sheet
        IOException or                // missing file, permission, disk
        UnauthorizedAccessException or
        FormatException or
        System.Text.Json.JsonException;
}
