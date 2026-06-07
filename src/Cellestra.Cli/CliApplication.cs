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
        try
        {
            return BuildRoot().Parse(args).Invoke();
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine($"cellestra: {ex.Message}");
            return 1;
        }
    }
}
