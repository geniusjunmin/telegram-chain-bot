using System;
using System.Linq;

namespace TelegramChainBot.Services;

public class AdminPasswordPolicy
{
    private static readonly string[] CommonWeakPasswords = new[]
    {
        "password", "1234567890", "123456789012", "admin1234567", "administrator", "password12345"
    };

    public bool Validate(string password, string? username, string? oldPassword, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrEmpty(password))
        {
            errorMessage = "密码不能为空。";
            return false;
        }

        if (password.Length < 12)
        {
            errorMessage = "密码长度必须至少为 12 个字符。";
            return false;
        }

        bool hasLetter = password.Any(char.IsLetter);
        bool hasDigit = password.Any(char.IsDigit);
        bool hasSpecial = password.Any(c => !char.IsLetterOrDigit(c));

        if (!hasLetter || !hasDigit)
        {
            errorMessage = "密码必须同时包含字母 and 数字。";
            return false;
        }

        if (!hasSpecial)
        {
            errorMessage = "密码必须包含至少一个非字母数字字符（如标点符号或特殊字符）。";
            return false;
        }

        if (password.All(char.IsDigit) || password.All(char.IsLetter))
        {
            errorMessage = "密码不能是纯数字或纯字母。";
            return false;
        }

        if (CommonWeakPasswords.Any(w => password.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            errorMessage = "密码过于简单，不能包含常见弱密码。";
            return false;
        }

        if (username != null && password.Contains(username, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "密码不能包含用户名。";
            return false;
        }

        if (oldPassword != null && string.Equals(password, oldPassword, StringComparison.Ordinal))
        {
            errorMessage = "新密码不能与旧密码相同。";
            return false;
        }

        return true;
    }
}
