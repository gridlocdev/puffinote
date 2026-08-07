using System.CommandLine;
using System.Text;
using Puffinote.Text.Document;
using Typage.Cli.Infrastructure;
using Typage.Engine.Html;
using Typage.Engine.Markdown;

namespace Typage.Cli.Commands;

/// <summary>
/// The command set — each a thin call into <see cref="Typage.Engine"/>, the same brain the
/// editor uses. One static factory per command so tests can build and invoke the same surface
/// in-process.
/// </summary>
internal static class MarkdownCommands
{
    private static Argument<string> FileArg() =>
        new("file") { Description = "Markdown file to read (or - for stdin)" };

    public static Command Html()
    {
        var file = FileArg();
        var output = new Option<string?>("--output", "-o") { Description = "Write HTML here instead of stdout" };
        var fragment = new Option<bool>("--fragment") { Description = "Emit only the <body> markup (no document wrapper)" };
        var command = new Command("html", "Render the document to self-contained HTML (PRD §7.8)") { file, output, fragment };

        command.SetAction(parse =>
        {
            var path = parse.GetValue(file)!;
            var doc = Workspace.Load(path);
            var html = parse.GetValue(fragment)
                ? HtmlRenderer.RenderBody(doc)
                : HtmlRenderer.RenderDocument(doc, Workspace.TitleFor(path));
            Workspace.Save(html, parse.GetValue(output) ?? "-");
            return 0;
        });

        return command;
    }

    public static Command Tokens()
    {
        var file = FileArg();
        var command = new Command("tokens", "Dump the semantic token stream (debugging the tokenizer)") { file };

        command.SetAction(parse =>
        {
            var path = parse.GetValue(file)!;
            var doc = Workspace.Load(path);
            var model = MarkdownModel.Build(doc);
            var sb = new StringBuilder();
            foreach (var t in model.AllTokens())
            {
                var text = doc.GetText(t.Start, t.Length).Replace("\r", "\\r", StringComparison.Ordinal);
                sb.Append(t.Type).Append('\t').Append(t.Role)
                  .Append("\t[").Append(t.Start).Append("..").Append(t.End).Append("]\t«")
                  .Append(text).Append('»');
                if (t.Meta is not null)
                {
                    sb.Append("\tmeta=").Append(t.Meta);
                }

                sb.Append('\n');
            }

            Console.Out.Write(sb.ToString());
            return 0;
        });

        return command;
    }

    public static Command Stats()
    {
        var file = FileArg();
        var command = new Command("stats", "Word, character, line counts and reading time") { file };

        command.SetAction(parse =>
        {
            var doc = Workspace.Load(parse.GetValue(file)!);
            var s = DocumentStats.Compute(doc);
            Console.Out.Write(
                $"words\t{s.Words}\ncharacters\t{s.Characters}\nlines\t{s.Lines}\nreading_minutes\t{s.ReadingMinutes}\n");
            return 0;
        });

        return command;
    }

    public static Command Outline()
    {
        var file = FileArg();
        var command = new Command("outline", "List the document's headings as an indented outline") { file };

        command.SetAction(parse =>
        {
            var doc = Workspace.Load(parse.GetValue(file)!);
            var model = MarkdownModel.Build(doc);
            var sb = new StringBuilder();
            for (var line = 0; line < model.LineCount; line++)
            {
                var tokens = model.LineTokens(line);
                if (tokens.Count >= 1 && tokens[0].Type == TokenType.HeadingMarker)
                {
                    var level = int.TryParse(tokens[0].Meta, out var l) ? l : 1;
                    var text = doc.GetText(tokens[0].End, doc.GetLineEnd(line) - tokens[0].End).TrimEnd('\r').Trim();
                    sb.Append(new string(' ', (level - 1) * 2)).Append(text).Append('\n');
                }
            }

            Console.Out.Write(sb.ToString());
            return 0;
        });

        return command;
    }

    public static Command Cat()
    {
        var file = FileArg();
        var command = new Command("cat", "Print the raw Markdown source (passthrough; - reads stdin)") { file };

        command.SetAction(parse =>
        {
            var doc = Workspace.Load(parse.GetValue(file)!);
            Console.Out.Write(doc.GetText());
            return 0;
        });

        return command;
    }
}
