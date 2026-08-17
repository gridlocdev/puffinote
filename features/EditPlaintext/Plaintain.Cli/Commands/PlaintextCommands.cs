using System.CommandLine;
using System.Globalization;
using Puffinote.Components.Core.Cli;
using Puffinote.Text.Document;

namespace Plaintain.Cli;

/// <summary>
/// The one inspection verb. Everything it prints comes from <c>Puffinote.Text</c> — the same code the
/// editor reads a file with — so what <c>plaintain info</c> says about a file is what the app would show.
/// </summary>
internal static class PlaintextCommands
{
    public static Command Info()
    {
        var file = new Argument<string>("file") { Description = "File to inspect (or - for stdin)" };
        var command = new Command("info", "Encoding, line endings, and line/word/byte counts") { file };

        command.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            byte[] bytes = Read(path);
            var read = TextFileCodec.Read(bytes);
            var stats = DocumentStats.Compute(TextDocument.FromString(read.Text));

            Console.Out.WriteLine($"encoding:      {read.Format.DisplayName}{(read.IsReadOnly ? " (guessed — opens read-only)" : string.Empty)}");
            Console.Out.WriteLine($"line endings:  {stats.NewlineLabel}");
            Console.Out.WriteLine($"lines:         {stats.Lines.ToString(CultureInfo.InvariantCulture)}");
            Console.Out.WriteLine($"words:         {stats.Words.ToString(CultureInfo.InvariantCulture)}");
            Console.Out.WriteLine($"characters:    {stats.Characters.ToString(CultureInfo.InvariantCulture)}");
            Console.Out.WriteLine($"bytes:         {bytes.Length.ToString(CultureInfo.InvariantCulture)}");

            // Non-zero when the file could not be read as text with any confidence, so a script can
            // branch on it without parsing the output.
            return read.IsReadOnly ? 2 : 0;
        });

        return command;
    }

    private static byte[] Read(string path)
    {
        if (path == "-")
        {
            using var stdin = Console.OpenStandardInput();
            using var buffer = new MemoryStream();
            stdin.CopyTo(buffer);
            return buffer.ToArray();
        }

        if (!File.Exists(path))
        {
            throw new CliException($"'{path}' does not exist.");
        }

        return File.ReadAllBytes(path);
    }
}
