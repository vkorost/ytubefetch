using System.Text.RegularExpressions;

namespace YTubeFetch.Services;

public static partial class SubtitleProcessor
{
    public static string StripTimestamps(string rawText)
    {
        var lines = rawText.Split('\n');
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (line == "WEBVTT")
                continue;
            if (line.StartsWith("Kind:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.StartsWith("Language:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase))
                continue;
            if (SequenceNumberRegex().IsMatch(line))
                continue;
            if (TimestampRegex().IsMatch(line))
                continue;
            if (PositioningRegex().IsMatch(line))
                continue;

            // Remove HTML tags
            var cleaned = HtmlTagRegex().Replace(line, "").Trim();

            if (string.IsNullOrWhiteSpace(cleaned))
                continue;

            // Deduplicate
            if (seen.Add(cleaned))
            {
                result.Add(cleaned);
            }
        }

        return string.Join("\n", result);
    }

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex SequenceNumberRegex();

    [GeneratedRegex(@"^[\d:.,-]+\s*-->")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"^(align|position|size|line|vertical):")]
    private static partial Regex PositioningRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();
}
