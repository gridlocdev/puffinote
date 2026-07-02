using System.Globalization;
using Puffinote.Components.Core.Cli;

namespace Cellestra.Cli.Infrastructure;

/// <summary>Resolves the <c>--locale</c> override into a culture, so scripted output is reproducible.</summary>
public static class Locale
{
    public static CultureInfo? Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null; // fall back to the document locale, else the system locale
        }

        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            throw new CliException($"Unknown locale '{name}' (try a tag like en-US or de-DE).");
        }
    }
}
