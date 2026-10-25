namespace GfxStudio.Core.Diagnostics;

/// <summary>
/// Provides string distance and typo suggestion utilities for diagnostics.
/// </summary>
public static class PropertySuggestionHelper
{
    /// <summary>
    /// Computes the Levenshtein edit distance between two strings (case-insensitive).
    /// </summary>
    public static int LevenshteinDistance(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitution = previous[rightIndex - 1] + (char.ToUpperInvariant(left[leftIndex - 1]) == char.ToUpperInvariant(right[rightIndex - 1]) ? 0 : 1);
                current[rightIndex] = Math.Min(substitution, Math.Min(previous[rightIndex] + 1, current[rightIndex - 1] + 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    /// <summary>
    /// Finds the closest matching candidate string within the maximum edit distance.
    /// </summary>
    public static string? FindBestMatch(string input, IEnumerable<string> candidates, int maxDistance = 2)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(candidates);

        var match = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => (Name: name, Distance: LevenshteinDistance(input, name)))
            .Where(candidate => candidate.Distance <= maxDistance)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return match.Name;
    }

    /// <summary>
    /// Generates a suggestion suffix (e.g. <c>" Did you mean 'color'?"</c>) if a candidate is within <paramref name="maxDistance"/>.
    /// </summary>
    public static string GetSuggestionSuffix(string input, IEnumerable<string> candidates, int maxDistance = 2)
    {
        var match = FindBestMatch(input, candidates, maxDistance);
        return match is not null ? $" Did you mean '{match}'?" : string.Empty;
    }
}
