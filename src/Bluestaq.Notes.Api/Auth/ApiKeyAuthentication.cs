using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Bluestaq.Notes.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bluestaq.Notes.Api.Auth;

public static class ApiKeyAuthenticationDefaults
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string CredentialIdClaim = "api_key_id";
}

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    NotesDbContext dbContext)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var values))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKey = values.ToString();
        if (values.Count != 1 || apiKey.Length is < 16 or > 256)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var apiKeyHash = ApiKeyHasher.Hash(apiKey);
        var now = DateTime.UtcNow;
        var credential = await dbContext.ApiKeys
            .Include(candidate => candidate.User)
            .SingleOrDefaultAsync(
                candidate => candidate.KeyHash == apiKeyHash
                    && candidate.RevokedAt == null
                    && (candidate.ExpiresAt == null || candidate.ExpiresAt > now),
                Context.RequestAborted);

        if (credential is null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, credential.User.Id.ToString()),
            new Claim(ClaimTypes.Name, credential.User.DisplayName),
            new Claim(ApiKeyAuthenticationDefaults.CredentialIdClaim, credential.Id.ToString()),
        };
        credential.LastUsedAt = now;
        await dbContext.SaveChangesAsync(Context.RequestAborted);
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}

public static class ApiKeyHasher
{
    public static string Hash(string apiKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
}

public static class ApiKeyGenerator
{
    public static string Generate() =>
        $"bns_{Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))}";
}
