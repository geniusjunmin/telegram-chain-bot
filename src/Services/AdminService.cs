using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;

namespace TelegramChainBot.Services;

public sealed class AdminService(AppDbContext db)
{
    public async Task<Admin?> LoginAsync(string username, string password, CancellationToken ct)
    {
        var admin = await db.Admins.FirstOrDefaultAsync(a => a.Username == username, ct);
        if (admin == null || !VerifyPassword(password, admin.PasswordHash))
        {
            return null;
        }
        return admin;
    }

    public async Task<bool> ChangePasswordAsync(int adminId, string oldPassword, string newPassword, CancellationToken ct)
    {
        var admin = await db.Admins.FindAsync([adminId], ct);
        if (admin == null || !VerifyPassword(oldPassword, admin.PasswordHash))
        {
            return false;
        }

        admin.PasswordHash = HashPassword(newPassword);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task EnsureDefaultAdminAsync(CancellationToken ct)
    {
        if (!await db.Admins.AnyAsync(ct))
        {
            db.Admins.Add(new Admin
            {
                Username = "admin",
                PasswordHash = HashPassword("admin123") // 初始默认密码
            });
            await db.SaveChangesAsync(ct);
        }
    }

    public string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
    }

    private bool VerifyPassword(string password, string hash)
    {
        return string.Equals(HashPassword(password), hash, StringComparison.OrdinalIgnoreCase);
    }
}
