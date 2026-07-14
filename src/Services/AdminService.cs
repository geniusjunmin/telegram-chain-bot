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
        if (admin == null || !admin.IsActive)
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
                admin.LastLoginAt = DateTimeOffset.UtcNow;
                admin.UpdatedAt = DateTimeOffset.UtcNow;
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
                admin.LastLoginAt = DateTimeOffset.UtcNow;
                admin.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            else if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                isValid = true;
                admin.PasswordHash = passwordHasher.HashPassword(admin, password);
                admin.AccessFailedCount = 0;
                admin.LockoutEnd = null;
                admin.LastLoginAt = DateTimeOffset.UtcNow;
                admin.UpdatedAt = DateTimeOffset.UtcNow;
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
            admin.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return null;
        }

        return admin;
    }

    public async Task<bool> ChangePasswordAsync(int adminId, string oldPassword, string newPassword, CancellationToken ct)
    {
        var admin = await db.AdminAccounts.FindAsync([adminId], ct);
        if (admin == null || !admin.IsActive)
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
        admin.SecurityStamp = Guid.NewGuid().ToString("N");
        admin.PasswordChangedAt = DateTimeOffset.UtcNow;
        admin.MustChangePassword = false;
        admin.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task EnsureDefaultAdminAsync(CancellationToken ct)
    {
        if (await db.AdminAccounts.AnyAsync(ct))
        {
            return;
        }

        string? password = null;
        var passwordFile = Environment.GetEnvironmentVariable("INITIAL_ADMIN_PASSWORD_FILE");
        if (string.IsNullOrWhiteSpace(passwordFile))
        {
            passwordFile = "data/initial_admin_password";
        }

        if (File.Exists(passwordFile))
        {
            password = (await File.ReadAllTextAsync(passwordFile, ct)).Trim();
        }
        else
        {
            var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            if (string.Equals(envName, "Development", StringComparison.OrdinalIgnoreCase))
            {
                password = Environment.GetEnvironmentVariable("INITIAL_ADMIN_PASSWORD")?.Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("No initial admin password was configured. Root account bootstrapping failed.");
        }

        if (password.Length < 12)
        {
            throw new InvalidOperationException("Bootstrap password is too short. It must be at least 12 characters.");
        }

        if (IsWeakPassword(password))
        {
            throw new InvalidOperationException("Bootstrap password is too simple.");
        }

        var defaultAdmin = new AdminAccount
        {
            Username = "admin",
            NormalizedUsername = "ADMIN",
            Role = AdminRole.RootAdmin,
            IsActive = true,
            MustChangePassword = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        defaultAdmin.PasswordHash = passwordHasher.HashPassword(defaultAdmin, password);
        
        db.AdminAccounts.Add(defaultAdmin);
        await db.SaveChangesAsync(ct);
    }

    private static bool IsWeakPassword(string password)
    {
        if (password.All(char.IsDigit) || password.All(char.IsLetter)) return true;
        if (password.Distinct().Count() < 4) return true;
        return false;
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
