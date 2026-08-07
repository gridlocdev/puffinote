using System.CommandLine;
using Puffinote.Components.Core.Cli;

namespace Plaintain.Cli;

/// <summary>
/// Builds the command tree and runs it. Factored out of <c>Program</c> so tests can invoke the same
/// surface in-process.
///
/// <para><b>`pltn` is a launcher plus exactly one read-only verb</b> (proposal 0003 §9.3). Opening a
/// file in the app is the whole job; <c>info</c> exists because a launcher is nearly untestable in a
/// headless container, and because "what encoding is this, and how many lines?" is the one question
/// about a text file worth answering without opening it. It is a deliberate stopping point — this does
/// not grow a verb surface the way <c>typage</c> has one.</para>
/// </summary>
public static class CliApplication
{
    public static RootCommand BuildRoot()
    {
        var root = new RootCommand("pltn — open a file in Plaintain, or look at what it is made of.")
        {
            PlaintextCommands.Info(),
        };

        // `pltn <file>` with no verb launches the app on that file; `pltn` alone launches it empty.
        var file = new Argument<string?>("file") { Description = "File to open in Plaintain", Arity = ArgumentArity.ZeroOrOne };
        root.Add(file);
        root.SetAction(parse => Launcher.Open(parse.GetValue(file)));
        return root;
    }

    public static int Run(string[] args) =>
        // The shared harness reports the common expected-error set cleanly and leaves real bugs loud.
        CliHost.Run(BuildRoot(), args, "pltn");
}
