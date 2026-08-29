using System.CommandLine;
using Cellestra.Cli.Commands;
using Cellestra.Cli.Infrastructure;
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
        var root = new RootCommand("Cellestra — a keyboard-friendly .ods spreadsheet toolkit for scripts and CI.")
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
            ApplyCommands.Apply(),
            ExtraCommands.Open(),
            ExtraCommands.Completions(),
            ReferenceCommands.Man(),
            ReferenceCommands.Schema(),

            // Data, structural, and layout edits — parity with the desktop app's Data/Insert menus.
            DataCommands.Sort(),
            DataCommands.CleanUp(),
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

        // `cellestra <file>` with no verb opens the workbook in the app, the way every CLI in the family
        // opens its own documents; `cellestra open <file>` is the same thing said explicitly, and
        // predates it. Bare `cellestra` still prints help: this is a toolkit first and a launcher
        // second, and the list of verbs is what someone typing the bare name came for.
        var file = new Argument<string?>("file")
        {
            Description = "Workbook to open in Cellestra",
            Arity = ArgumentArity.ZeroOrOne,
        };
        root.Add(file);
        root.SetAction(parse => parse.GetValue(file) is { } path
            ? Workspace.Launch(path)
            : root.Parse(["--help"]).Invoke());
        return root;
    }

    public static int Run(string[] args)
    {
        // The shared harness handles the common expected-error set (CliException, bad input, missing
        // file, …). Cellestra additionally treats InvalidOperationException (duplicate/unknown sheet)
        // and a malformed-JSON read as user errors rather than bugs.
        static bool Expected(Exception ex) => ex is InvalidOperationException or System.Text.Json.JsonException;

        if (!args.Contains("--json", StringComparer.Ordinal))
        {
            return CliHost.Run(BuildRoot(), args, "cellestra", Expected);
        }

        // Under --json a caller is a script or an agent reading stdout, so a user error is a JSON
        // document there too — the same `ok: false` shape every command's report uses — as well as
        // the one-line stderr message the harness prints for a human.
        var stderr = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        int exit;
        try
        {
            exit = CliHost.Run(BuildRoot(), args, "cellestra", Expected);
        }
        finally
        {
            Console.SetError(stderr);
        }

        string text = captured.ToString();
        if (text.Length > 0)
        {
            stderr.Write(text);
        }

        if (exit != 0 && text.StartsWith("cellestra: ", StringComparison.Ordinal))
        {
            Infrastructure.Report.Write(new { ok = false, error = text["cellestra: ".Length..].TrimEnd() });
        }

        return exit;
    }
}
