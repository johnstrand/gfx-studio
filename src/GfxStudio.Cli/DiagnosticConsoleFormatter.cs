using System.Text.RegularExpressions;
using GfxStudio.Core.Diagnostics;

namespace GfxStudio.Cli;

/// <summary>Formats diagnostics with a source excerpt and a short suggestion when a name is misspelled.</summary>
public static partial class DiagnosticConsoleFormatter
{
    public static string Format(GfxDiagnostic diagnostic, string source, IEnumerable<string>? knownNames = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        ArgumentNullException.ThrowIfNull(source);

        var severity = diagnostic.Severity.ToString().ToLowerInvariant();
        var header = $"{severity}[{diagnostic.Code}]: {diagnostic.Message}";
        var lines = source.Split('\n');
        var lineIndex = diagnostic.Span.Start.Line - 1;
        if (lineIndex < 0 || lineIndex >= lines.Length)
        {
            return header;
        }

        var line = lines[lineIndex].TrimEnd('\r');
        var lineNumber = diagnostic.Span.Start.Line;
        var width = Math.Max(1, lineNumber.ToString().Length);
        var column = Math.Clamp(diagnostic.Span.Start.Column - 1, 0, line.Length);
        var caretLength = diagnostic.Span.End.Line == lineNumber
            ? Math.Clamp(diagnostic.Span.End.Column - diagnostic.Span.Start.Column, 1, Math.Max(1, line.Length - column))
            : 1;
        var marker = new string(' ', column) + new string('^', caretLength);
        var suggestion = CreateSuggestion(diagnostic.Message, knownNames);

        return string.Join(Environment.NewLine,
        [
            header,
            $"  --> {diagnostic.Span.FilePath}:{lineNumber}:{diagnostic.Span.Start.Column}",
            $"{new string(' ', width + 1)} |",
            $"{lineNumber.ToString().PadLeft(width)} | {line}",
            $"{new string(' ', width + 1)} | {marker}{(suggestion is null ? string.Empty : $" {suggestion}")}"
        ]);
    }

    private static string? CreateSuggestion(string message, IEnumerable<string>? knownNames)
    {
        var match = UnknownNameRegex().Match(message);
        if (!match.Success || knownNames is null)
        {
            return null;
        }

        var misspelled = match.Groups[1].Value;
        var matchName = knownNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => (Name: name, Distance: LevenshteinDistance(misspelled, name)))
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        var threshold = Math.Max(2, misspelled.Length / 3);
        return matchName.Name is not null && matchName.Distance <= threshold
            ? $"Did you mean '{matchName.Name}'?"
            : null;
    }

    private static int LevenshteinDistance(string left, string right)
    {
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

    [GeneratedRegex("(?:Undefined (?:object or namespace|identifier)|Unknown function) '([^']+)'", RegexOptions.CultureInvariant)]
    private static partial Regex UnknownNameRegex();
}
