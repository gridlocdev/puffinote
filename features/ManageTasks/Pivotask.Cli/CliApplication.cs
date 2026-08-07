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
    public static RootCommand BuildRoot() =>
        new("ptask — read and change a Pivotask board from the command line.")
        {
            TaskCommands.List(),
            TaskCommands.Show(),
            TaskCommands.Add(),
            TaskCommands.Done(done: true),
            TaskCommands.Done(done: false),
            TaskCommands.Move(),
            TaskCommands.Groups(),
        };

    public static int Run(string[] args) =>
        // A malformed or future-versioned board is a user-caused error, not a bug — report it as one.
        CliHost.Run(BuildRoot(), args, "ptask", alsoExpected: ex => ex is BoardFormatException);
}
