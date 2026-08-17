using System.CommandLine;
using Puffinote.Components.Core.Cli;
using Typage.Cli.Commands;
using Typage.Engine.Markdown;

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
        var root = new RootCommand("typage — a dependency-light Markdown toolkit for scripts and CI.")
        {
            MarkdownCommands.Html(),
            MarkdownCommands.Cat(),
            MarkdownCommands.Tokens(),
            MarkdownCommands.Stats(),
            MarkdownCommands.Outline(),
        };

        // `typage <file>` with no verb opens the document in Typage, the way every CLI in the family
        // opens its app's own documents. Bare `typage` still prints help: this is a toolkit first and a
        // launcher second, and the list of verbs is what someone typing the bare name came for.
        var file = new Argument<string?>("file")
        {
            Description = "Document to open in Typage",
            Arity = ArgumentArity.ZeroOrOne,
        };
        root.Add(file);
        root.SetAction(parse => parse.GetValue(file) is { } path
            ? Launch(path)
            : root.Parse(["--help"]).Invoke());
        return root;
    }

    /// <summary>
    /// Open <paramref name="path"/> in Typage. A path whose extension Typage doesn't open is far more
    /// likely to be a mistyped verb than a document, so it is refused with both readings named rather
    /// than handed to the app to greet with an error dialog.
    /// </summary>
    private static int Launch(string path)
    {
        if (!MarkdownFiles.IsOne(path))
        {
            throw new CliException(
                $"'{path}' isn't a document Typage opens ({MarkdownFiles.Summary}). " +
                "If you meant a command, `typage --help` lists them.");
        }

        return AppLauncher.Open("Typage", path);
    }

    public static int Run(string[] args) =>
        // The shared harness handles the common expected-error set (CliException, bad input, missing file).
        CliHost.Run(BuildRoot(), args, "typage");
}
