using System.Net;
using System.Text;
using TelegramChainBot.Database.Models;

namespace TelegramChainBot.Services;

public static class TelegramMessageFormatter
{
    public static string FormatChainMessage(string title, IReadOnlyList<ChainMember> members)
    {
        var sb = new StringBuilder();
        
        // HTML Encode the title to prevent markup injection
        var encodedTitle = WebUtility.HtmlEncode(title);
        sb.AppendLine(encodedTitle);
        sb.AppendLine();

        if (members.Count == 0)
        {
            sb.AppendLine("1. ");
            return sb.ToString().TrimEnd();
        }

        for (var i = 0; i < members.Count; i++)
        {
            var displayName = FormatMemberDisplayName(members[i]);
            var encodedDisplayName = WebUtility.HtmlEncode(displayName);
            sb.AppendLine($"{i + 1}. {encodedDisplayName}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatMemberDisplayName(ChainMember member)
    {
        if (string.IsNullOrWhiteSpace(member.TelegramNickname) ||
            string.Equals(member.Username, member.TelegramNickname, StringComparison.Ordinal))
        {
            return member.Username;
        }

        return $"{member.Username}（{member.TelegramNickname}）";
    }
}
