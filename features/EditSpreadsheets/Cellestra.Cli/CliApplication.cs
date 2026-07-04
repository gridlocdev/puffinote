using System.CommandLine;
using Cellestra.Cli.Commands;
using Puffinote.Components.Core.Cli;

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

            // Data, structural, and layout edits — parity with the desktop app's Data/Insert menus.
            DataCommands.Sort(),
            DataCommands.RemoveDuplicates(),
            DataCommands.Split(),
            DataCommands.Table(),
            DataCommands.Validation(),
            DataCommands.Name(),
            DataCommands.Hyperlink(),
            DataCommands.InsertRows(),
            DataCommands.DeleteRows(),
            DataCommands.InsertColumns(),
            DataCommands.DeleteColumns(),
            DataCommands.HideRows(),
            DataCommands.ShowRows(),
            DataCommands.HideColumns(),
            DataCommands.ShowColumns(),
            DataCommands.Merge(),
            DataCommands.Unmerge(),

            // Chart sheets — the headless face of Insert ▸ Chart Sheet.
            ChartCommands.Chart(),
        };
    }

    public static int Run(string[] args) =>
        // The shared harness handles the common expected-error set (CliException, bad input, missing
        // file, …). Cellestra additionally treats InvalidOperationException (duplicate/unknown sheet)
        // and a malformed-JSON read as user errors rather than bugs.
        CliHost.Run(BuildRoot(), args, "cellestra",
            ex => ex is InvalidOperationException or System.Text.Json.JsonException);
}
