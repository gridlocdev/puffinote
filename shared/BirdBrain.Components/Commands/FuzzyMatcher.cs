namespace BirdBrain.Components.Commands;

/// <summary>
/// Subsequence fuzzy matching for the Command Palette and similar pickers (modeled on VS Code): a
/// query matches when its characters appear in order in the candidate. Returns a score (higher is
/// better) so results can be ranked; -1 means no match. Shared across the BirdBrain family.
/// </summary>
public static class FuzzyMatcher
{
    public static int Score(string candidate, string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return 0;
        }

        int score = 0;
        int candidateIndex = 0;
        int consecutive = 0;

        foreach (char q in query)
        {
            bool matched = false;
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
