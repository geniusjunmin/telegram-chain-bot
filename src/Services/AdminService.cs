using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;

namespace TelegramChainBot.Services;

public sealed class AdminService(
    AppDbContext db,
    IPasswordHasher<AdminAccount> passwordHasher)
{
    public async Task<AdminAccount?> LoginAsync(string username, string password, CancellationToken ct)
    {
        var admin = await db.AdminAccounts.FirstOrDefaultAsync(a => a.Username == username, ct);
        if (admin == null || admin.IsDisabled)
        {
            return null;
        }

        // Check lock out
        if (admin.LockoutEnd.HasValue && admin.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return null;
        }

        var isLegacy = IsLegacySha256Hash(admin.PasswordHash);
        var isValid = false;

        if (isLegacy)
        {
            isValid = VerifyLegacySha256(password, admin.PasswordHash);
            if (isValid)
            {
                // Upgrade to modern hash
                admin.PasswordHash = passwordHasher.HashPassword(admin, password);
                admin.AccessFailedCount = 0;
                admin.LockoutEnd = null;
                await db.SaveChangesAsync(ct);
            }
        }
        else
        {
            var result = passwordHasher.VerifyHashedPassword(admin, admin.PasswordHash, password);
            if (result == PasswordVerificationResult.Success)
            {
                isValid = true;
                admin.AccessFailedCount = 0;
                admin.LockoutEnd = null;
                await db.SaveChangesAsync(ct);
            }
            else if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                isValid = true;
                admin.PasswordHash = passwordHasher.HashPassword(admin, password);
                admin.AccessFailedCount = 0;
                admin.LockoutEnd = null;
                await db.SaveChangesAsync(ct);
            }
        }

        if (!isValid)
        {
            admin.AccessFailedCount++;
            if (admin.AccessFailedCount >= 5)
            {
                admin.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
            }
            await db.SaveChangesAsync(ct);
            return null;
        }

        return admin;
    }

    public async Task<bool> ChangePasswordAsync(int adminId, string oldPassword, string newPassword, CancellationToken ct)
    {
        var admin = await db.AdminAccounts.FindAsync([adminId], ct);
        if (admin == null || admin.IsDisabled)
        {
            return false;
        }

        var isLegacy = IsLegacySha256Hash(admin.PasswordHash);
        var isValid = false;

        if (isLegacy)
        {
            isValid = VerifyLegacySha256(oldPassword, admin.PasswordHash);
        }
        else
        {
            var result = passwordHasher.VerifyHashedPassword(admin, admin.PasswordHash, oldPassword);
            isValid = result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded;
        }

        if (!isValid)
        {
            return false;
        }

        admin.PasswordHash = passwordHasher.HashPassword(admin, newPassword);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task EnsureDefaultAdminAsync(CancellationToken ct)
    {
        if (!await db.AdminAccounts.AnyAsync(ct))
        {
            var defaultAdmin = new AdminAccount
            {
                Username = "admin",
                Role = AdminRole.RootAdmin
            };
            defaultAdmin.PasswordHash = passwordHasher.HashPassword(defaultAdmin, "admin123");
            
            db.AdminAccounts.Add(defaultAdmin);
            await db.SaveChangesAsync(ct);
        }
    }

    private static bool IsLegacySha256Hash(string hash)
    {
        return hash.Length == 64 && System.Text.RegularExpressions.Regex.IsMatch(hash, @"^[0-9a-fA-F]{64}$");
    }

    private static bool VerifyLegacySha256(string password, string hash)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        var hex = Convert.ToHexString(bytes);
        return string.Equals(hex, hash, StringComparison.OrdinalIgnoreCase);
    }
}
