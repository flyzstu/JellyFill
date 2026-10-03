using System.Text.RegularExpressions;

namespace JellyFill.Common;

/// <summary>
/// Utility to extract canonical 番号 from file paths or names.
/// </summary>
public static class FilenameParser
{
    private static readonly Regex CleanPattern = new(
        @"(?i)([-_]C\b|[-_]UC\b|[-_]4K\b|[-_]HD\b|[-_]FHD\b|中文字幕|無碼流出|破解版|\b1080p\b|\b720p\b|\b2160p\b)",
        RegexOptions.Compiled);

    private static readonly Regex[] ExtractPatterns = new[]
    {
        // 1. FC2: FC2-PPV-1234567 or FC2-123456
        new Regex(@"(?i)\b(fc2(?:-ppv)?)[-_]?(\d{5,8})\b", RegexOptions.Compiled),

        // 2. Uncensored date-like ID: 110121-001 or 010122_123
        new Regex(@"\b(\d{6})[-_](\d{2,4})\b", RegexOptions.Compiled),

        // 3. Standard censored releases: SSIS-365, SNIS-632, IPX-123, EBWH-357
        new Regex(@"(?i)\b([a-z]{2,6})[-_]?(\d{2,5})\b", RegexOptions.Compiled),

        // 4. Special alphanumeric: T28-123, 1pondo-123456_001
        new Regex(@"(?i)\b([a-z0-9]{2,8})[-_](\d{2,6})\b", RegexOptions.Compiled),
    };

    /// <summary>
    /// Extract clean 番号 from filename.
    /// </summary>
    /// <param name="raw">Raw file name or title.</param>
    /// <returns>Normalized番号 or empty string.</returns>
    public static string Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        // Clean common noise
        string clean = CleanPattern.Replace(raw, string.Empty);

        foreach (var regex in ExtractPatterns)
        {
            var match = regex.Match(clean);
            if (match.Success)
            {
                string prefix = match.Groups[1].Value.ToUpperInvariant();
                string digits = match.Groups[2].Value;
                return $"{prefix}-{digits}";
            }
        }

        return raw.Trim();
    }
}
