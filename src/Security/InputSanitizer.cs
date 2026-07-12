using System.Text.RegularExpressions;

namespace TelegramChainBot.Security;

public static class InputSanitizer
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static string SanitizeName(string name, int maxLength = 32)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        // 1. Remove control characters
        var noControls = new string(name.Where(c => !char.IsControl(c)).ToArray());

        // 2. Collapse consecutive whitespaces into a single space
        var collapsed = WhitespaceRegex.Replace(noControls, " ").Trim();

        // 3. Limit length
        if (collapsed.Length > maxLength)
        {
            collapsed = collapsed[..maxLength];
        }

        return collapsed;
    }

    public static string SanitizeTitle(string title, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        // Remove control characters (except maybe newlines, but chain titles are usually single-line)
        var noControls = new string(title.Where(c => !char.IsControl(c)).ToArray());
        var collapsed = WhitespaceRegex.Replace(noControls, " ").Trim();

        if (collapsed.Length > maxLength)
        {
            collapsed = collapsed[..maxLength];
        }

        return collapsed;
    }
}
