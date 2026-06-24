namespace Typage.Desktop.Commands;

/// <summary>
/// Subsequence fuzzy matching for the Command Palette (modeled on VS Code, ported from Cellestra):
/// a query matches when its characters appear in order in the candidate. Returns a score (higher is
/// better) so results rank sensibly; -1 means no match.
/// </summary>
public static class FuzzyMatcher
{
    public static int Score(string candidate, string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return 0;
        }

        var score = 0;
        var candidateIndex = 0;
        var consecutive = 0;

        foreach (var q in query)
        {
            var matched = false;
            for (; candidateIndex < candidate.Length; candidateIndex++)
            {
                if (char.ToLowerInvariant(candidate[candidateIndex]) == char.ToLowerInvariant(q))
                {
                    score += 1 + consecutive;            // reward runs of consecutive matches
                    if (candidateIndex == 0 || candidate[candidateIndex - 1] == ' ')
                    {
                        score += 2;                       // reward word-start matches
                    }

                    consecutive++;
                    candidateIndex++;
                    matched = true;
                    break;
                }

                consecutive = 0;
            }

            if (!matched)
            {
                return -1;
            }
        }

        return score;
    }

    public static bool IsMatch(string candidate, string query) => Score(candidate, query) >= 0;
}
