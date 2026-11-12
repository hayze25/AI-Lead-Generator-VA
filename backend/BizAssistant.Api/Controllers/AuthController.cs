using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BizAssistant.Api.Data;
using BizAssistant.Api.Models;
using BizAssistant.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BizAssistant.Api.Controllers;

/// <summary>
/// Two kinds of login: the admin from configuration (owns the original business), and registered users
/// (each owns a business of their own). Either way the token carries the business id the user belongs to.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController(IConfiguration config, AppDbContext db) : ControllerBase
{
    public const string Issuer = "BizAssistant";
    private const int MaxFailures = 5;
    private const int MaxRegistrationsPerHour = 5;
    private static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);

    // In memory: resets when the API restarts.
    private static readonly ConcurrentDictionary<string, (int Fails, DateTime LockedUntil)> Attempts = new();
    private static readonly ConcurrentDictionary<string, Queue<DateTime>> Registrations = new();

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req, CancellationToken ct)
    {
        var client = ClientKey();

        if (Attempts.TryGetValue(client, out var state) && state.LockedUntil > DateTime.UtcNow)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((state.LockedUntil - DateTime.UtcNow).TotalMinutes));
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = $"Too many failed attempts. Try again in {minutes} minute(s)." });
        }

        var identifier = req.Username.Trim();
        var ok = false;
        var subject = "";
        var businessId = 0;

        if (SafeEquals(identifier, config["Auth:Username"] ?? ""))
        {
            // The admin from configuration, who owns the original business.
            if (SafeEquals(req.Password, config["Auth:Password"] ?? ""))
            {
                ok = true;
                subject = "admin";
                businessId = await db.DefaultBusinessIdAsync(ct);
            }
        }
        else
        {
            var email = identifier.ToLowerInvariant();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct);
            var passwordOk = PasswordHasher.Verify(req.Password, user?.PasswordHash); // same cost whether or not the user exists
            if (user is not null && passwordOk)
            {
                ok = true;
                subject = $"user:{user.Id}";
                businessId = user.BusinessId;
            }
        }

        if (!ok || businessId <= 0)
        {
            var fails = (Attempts.TryGetValue(client, out var s) ? s.Fails : 0) + 1;
            Attempts[client] = fails >= MaxFailures ? (0, DateTime.UtcNow + LockFor) : (fails, DateTime.MinValue);
            await Task.Delay(400); // slows down guessing
            return Unauthorized(new { error = "Incorrect email/username or password." });
        }

        Attempts.TryRemove(client, out _);
        return Ok(IssueToken(subject, businessId));
    }

    /// <summary>Creates a new business with its own empty workspace, plus the owner's login, and signs them in.</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req, CancellationToken ct)
    {
        if (!config.GetValue("Auth:AllowRegistration", true))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Registration is turned off." });

        if (!RegistrationAllowed(ClientKey()))
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = "Too many accounts created from this address. Please try again later." });

        var email = req.Email.Trim().ToLowerInvariant();
        const string alreadyUsed = "That email is already registered. Try signing in instead.";

        // Nobody can register the admin's name, even though it isn't stored in the Users table.
        if (SafeEquals(email, (config["Auth:Username"] ?? "").ToLowerInvariant()))
            return Conflict(new { error = alreadyUsed });
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return Conflict(new { error = alreadyUsed });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var business = new BusinessProfile
            {
                Name = req.BusinessName.Trim(),
                Description = "",
                Tone = "Friendly and professional",
                Faq = ""
            };
            db.BusinessProfiles.Add(business);
            await db.SaveChangesAsync(ct);

            db.Users.Add(new AppUser
            {
                Email = email,
                PasswordHash = PasswordHasher.Hash(req.Password),
                BusinessId = business.Id,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Ok(IssueToken($"user:{email}", business.Id));
        }
        catch (DbUpdateException)
        {
            // Two people registering the same email at once: the unique index lets only one win, and the
            // transaction is rolled back (it is never committed), so no orphan business is left behind.
            return Conflict(new { error = alreadyUsed });
        }
    }

    private object IssueToken(string subject, int businessId)
    {
        var expires = DateTime.UtcNow + TokenLifetime;
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", subject),
                new Claim(CurrentTenant.ClaimType, businessId.ToString())
            }),
            Issuer = Issuer,
            Audience = Issuer,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Auth:JwtKey"] ?? "")),
                SecurityAlgorithms.HmacSha256)
        };

        return new { token = new JsonWebTokenHandler().CreateToken(descriptor), expiresAt = expires };
    }

    private string ClientKey() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static bool RegistrationAllowed(string client)
    {
        var now = DateTime.UtcNow;
        var recent = Registrations.GetOrAdd(client, _ => new Queue<DateTime>());
        lock (recent)
        {
            while (recent.Count > 0 && now - recent.Peek() > TimeSpan.FromHours(1)) recent.Dequeue();
            if (recent.Count >= MaxRegistrationsPerHour) return false;
            recent.Enqueue(now);
            return true;
        }
    }

    /// <summary>Compares via SHA-256 so length differences don't leak and timing is constant.</summary>
    private static bool SafeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)),
            SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}
