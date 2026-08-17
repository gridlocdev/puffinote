using System.Globalization;
using System.Security.Cryptography;

namespace Pivotask.Core.Model;

/// <summary>
/// Short, opaque, collision-resistant ids for groups, tasks, steps, and labels.
///
/// <para>Seven base-36 characters, from a cryptographic RNG. Short because a human reads these in a
/// <c>git diff</c> and types them at the CLI (<c>pivotask done k3f9a2b</c>); random rather than sequential
/// because two people adding a task on two branches must not produce the same id, which is exactly what
/// a counter would do. Base-36 rather than hex so seven characters carry 36 bits instead of 28.</para>
/// </summary>
public static class Ids
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    /// <summary>The length of a generated id. Long enough that a board would need millions of tasks
    /// before a collision was worth thinking about; short enough to type.</summary>
    public const int Length = 7;

    public static string New()
    {
        Span<char> id = stackalloc char[Length];
        for (int i = 0; i < Length; i++)
        {
            id[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(id);
    }

    /// <summary>
    /// A deterministic id derived from <paramref name="seed"/>. For tests and for the seeded starter
    /// board, so a "new board" is byte-identical every time it is written rather than churning ids.
    /// </summary>
    public static string FromSeed(int seed)
    {
        Span<char> id = stackalloc char[Length];
        uint value = (uint)HashCode.Combine(seed, 0x5EED);
        for (int i = 0; i < Length; i++)
        {
            id[i] = Alphabet[(int)(value % (uint)Alphabet.Length)];
            value = (value / (uint)Alphabet.Length) + 7;
        }

        return new string(id);
    }

    /// <summary>Format a date the one way the format allows: <c>YYYY-MM-DD</c>, invariant.</summary>
    public static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Parse a <c>YYYY-MM-DD</c> date, or null. Strict: no locale guessing, ever.</summary>
    public static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
