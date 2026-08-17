using System.CommandLine;
using Pivotask.Cli.Commands;
using Pivotask.Core.Serialization;
using Puffinote.Components.Core.Cli;

namespace Pivotask.Cli;

/// <summary>
/// Builds the command tree and runs it. Factored out of <c>Program</c> so tests can drive the same
/// surface in-process — which is most of why this CLI landed before the GUI (proposal 0004 Phase 1): a
/// verb set that is fully testable headlessly exercised the core's API while it was still cheap to change.
/// </summary>
public static class CliApplication
{
    public static RootCommand BuildRoot()
    {
        var root = new RootCommand("pivotask — read and change a Pivotask board from the command line.")
        {
            TaskCommands.List(),
            TaskCommands.Show(),
            TaskCommands.Add(),
            TaskCommands.Done(done: true),
            TaskCommands.Done(done: false),
            TaskCommands.Move(),
            TaskCommands.Groups(),
        };

        // `pivotask <board>` with no verb opens the board in the app, the way every CLI in the family
        // opens its own documents. Bare `pivotask` still prints help: this is a verb set first and a
        // launcher second, and the list of verbs is what someone typing the bare name came for.
        var board = new Argument<string?>("board")
        {
            Description = "Board to open in Pivotask",
            Arity = ArgumentArity.ZeroOrOne,
        };
        root.Add(board);
        root.SetAction(parse => parse.GetValue(board) is { } path
            ? Workspace.Launch(path)
            : root.Parse(["--help"]).Invoke());
        return root;
    }

    public static int Run(string[] args) =>
        // A malformed or future-versioned board is a user-caused error, not a bug — report it as one.
        CliHost.Run(BuildRoot(), args, "pivotask", alsoExpected: ex => ex is BoardFormatException);
}
