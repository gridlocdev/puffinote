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
            InspectCommands.Trace(),
            InspectCommands.Lint(),

            // Extras.
            ExtraCommands.New(),
            ExtraCommands.Import(),
            ExtraCommands.Put(),
            ExtraCommands.Export(),
            ExtraCommands.Stats(),
            ExtraCommands.Find(),
            ExtraCommands.Diff(),
            ExtraCommands.Functions(),
            ExtraCommands.AddSheet(),
            ExtraCommands.RenameSheet(),
            ExtraCommands.DeleteSheet(),
            LayoutCommands.DuplicateSheet(),
            LayoutCommands.MoveSheet(),
            ApplyCommands.Apply(),
            ExtraCommands.Open(),
            ExtraCommands.Completions(),
            ReferenceCommands.Man(),
            ReferenceCommands.Schema(),
            ReferenceCommands.Skill(),

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

            // Formatting and layout verbs — fronts on the matching apply ops.
            LayoutCommands.Style(),
            LayoutCommands.Width(),
            LayoutCommands.Height(),
            LayoutCommands.Freeze(),
            LayoutCommands.Clear(),
            LayoutCommands.Settings(),

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
        // the one-line stderr message the harness prints for a human. Both streams are captured:
        // System.CommandLine answers a parse error (missing argument, unknown option) with the help
        // text on stdout, which is not JSON and must not reach a parser.
        var stdout = Console.Out;
        var stderr = Console.Error;
        var capturedOut = new StringWriter();
        var capturedErr = new StringWriter();
        Console.SetOut(capturedOut);
        Console.SetError(capturedErr);
        int exit;
        try
        {
            exit = CliHost.Run(BuildRoot(), args, "cellestra", Expected);
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }

        string errText = capturedErr.ToString();
        if (errText.Length > 0)
        {
            stderr.Write(errText);
        }

        if (exit != 0 && errText.Length > 0)
        {
            // Either the harness's "cellestra: …" line or System.CommandLine's own parse errors — both
            // are the user's to fix, and both get the JSON reply in place of whatever stdout held.
            string message = errText.StartsWith("cellestra: ", StringComparison.Ordinal) ? errText["cellestra: ".Length..] : errText;
            var lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
            Infrastructure.Report.Write(new { ok = false, error = string.Join(" ", lines) });
            return exit;
        }

        stdout.Write(capturedOut.ToString());
        return exit;
    }
}
