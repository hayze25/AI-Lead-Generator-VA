using System.Security.Cryptography;

namespace BizAssistant.Api.Services;

/// <summary>Which business is the current request working for? The query filters in AppDbContext read this.</summary>
public interface ICurrentTenant
{
    /// <summary>The business id, or 0 when nobody is signed in and no business has been chosen.</summary>
    int BusinessId { get; }

    /// <summary>Acts inside one named business without a login. Used only by the n8n intake endpoint.</summary>
    void Use(int businessId);
}

/// <summary>
/// Reads the business id from the signed-in user's token. The token is signed by the API, so a user
/// cannot change which business they belong to.
/// </summary>
public class CurrentTenant(IHttpContextAccessor http) : ICurrentTenant
{
    public const string ClaimType = "business_id";

    private int? _override;

    public int BusinessId =>
        _override ?? (int.TryParse(http.HttpContext?.User.FindFirst(ClaimType)?.Value, out var id) ? id : 0);

    public void Use(int businessId) => _override = businessId;
}

/// <summary>Password hashing with PBKDF2 (built into .NET, no extra package). Stored as v1.iterations.salt.hash</summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Pass null when the user doesn't exist: it still does the same amount of work, so the response time
    /// doesn't reveal which emails are registered. A null hash never verifies.
    /// </summary>
    public static bool Verify(string password, string? stored)
    {
        var hash = stored ?? Dummy;
        var valid = VerifyCore(password, hash);
        return stored is not null && valid;
    }

    private static readonly string Dummy = Hash("not-a-real-password");

    private static bool VerifyCore(string password, string stored)
    {
        try
        {
            var parts = stored.Split('.');
            if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations)) return false;

            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
