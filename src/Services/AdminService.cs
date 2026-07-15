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
    IPasswordHasher<AdminAccount> passwordHasher,
    AdminPasswordPolicy passwordPolicy)
{
    public async Task<AdminAccount?> LoginAsync(string username, string password, CancellationToken ct)
    {
        var admin = await db.AdminAccounts.FirstOrDefaultAsync(a => a.NormalizedUsername == username.ToUpperInvariant(), ct);
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

        if (!passwordPolicy.Validate(newPassword, admin.Username, oldPassword, out _))
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
        // 1. Check if there exists at least one active RootAdmin in the database
        if (await db.AdminAccounts.AnyAsync(a => a.IsActive && a.Role == AdminRole.RootAdmin, ct))
        {
            return;
        }

        // 2. Fetch the initial admin config parameters
        var usernameEnv = Environment.GetEnvironmentVariable("INITIAL_ADMIN_USERNAME");
        var username = string.IsNullOrWhiteSpace(usernameEnv) ? "admin" : usernameEnv.Trim();

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
            password = Environment.GetEnvironmentVariable("INITIAL_ADMIN_PASSWORD")?.Trim();
        }

        // 3. Fallback to default credentials if no password was configured
        if (string.IsNullOrWhiteSpace(password))
        {
            password = "Root@12345678";
            Console.WriteLine($"No initial admin password was configured. Bootstrapping with default credentials: username='{username}', password='{password}' (Change it immediately on first login).");
        }

        // 4. Validate password against unified password policy
        if (!passwordPolicy.Validate(password, username, null, out var policyError))
        {
            throw new InvalidOperationException($"Bootstrap initial password policy validation failed: {policyError}");
        }

        // 5. Check if user already exists
        var normalized = username.ToUpperInvariant();
        var admin = await db.AdminAccounts.FirstOrDefaultAsync(a => a.NormalizedUsername == normalized, ct);
        if (admin != null)
        {
            // If they exist but are not active RootAdmin, upgrade them to active RootAdmin and reset password
            admin.Role = AdminRole.RootAdmin;
            admin.IsActive = true;
            admin.MustChangePassword = true;
            admin.SecurityStamp = Guid.NewGuid().ToString("N");
            admin.PasswordHash = passwordHasher.HashPassword(admin, password);
            admin.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            // Create a completely new root admin account
            admin = new AdminAccount
            {
                Username = username,
                NormalizedUsername = normalized,
                Role = AdminRole.RootAdmin,
                IsActive = true,
                MustChangePassword = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                PasswordHash = string.Empty
            };
            admin.PasswordHash = passwordHasher.HashPassword(admin, password);
            db.AdminAccounts.Add(admin);
        }

        await db.SaveChangesAsync(ct);
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
