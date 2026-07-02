using System.CommandLine;

namespace Puffinote.Components.Core.Cli;

/// <summary>
/// The shared run harness for the family's CLIs (both apps had the same one). Turns off
/// System.CommandLine's default exception handler so a user-caused error surfaces as a clean one-line
/// <c>"{tool}: {message}"</c> on stderr with exit code 1, while action return codes still flow through
/// and a genuine bug is left to surface loudly.
/// </summary>
public static class CliHost
{
    /// <summary>
    /// Parse and invoke <paramref name="root"/> for <paramref name="args"/>, reporting user-caused errors
    /// cleanly as <paramref name="toolName"/>. <paramref name="alsoExpected"/> lets an app treat extra
    /// exception types as user errors (e.g. Cellestra's <see cref="InvalidOperationException"/> /
    /// <c>JsonException</c>) on top of the common set.
    /// </summary>
    public static int Run(RootCommand root, string[] args, string toolName, Func<Exception, bool>? alsoExpected = null)
    {
        var configuration = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        try
        {
            return root.Parse(args).Invoke(configuration);
        }
        catch (Exception ex) when (IsExpected(ex) || (alsoExpected?.Invoke(ex) ?? false))
        {
            Console.Error.WriteLine($"{toolName}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Errors a user can cause — bad input, a missing file, a bad number/format — reported cleanly with
    /// exit 1. Anything else is a bug and is left to surface loudly.
    /// </summary>
    private static bool IsExpected(Exception ex) => ex is
        CliException or
        ArgumentException or          // includes invalid regex / culture / rule violations
        IOException or                // missing file, permission, disk
        UnauthorizedAccessException or
        FormatException;
}
