using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Bluestaq.Notes.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bluestaq.Notes.Api.Auth;

/// <summary>Defines the scheme, header, and claim names used by API-key authentication.</summary>
public static class ApiKeyAuthenticationDefaults
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string CredentialIdClaim = "api_key_id";
}

/// <summary>Authenticates requests by validating an API key against active stored credentials.</summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    NotesDbContext dbContext,
    TimeProvider timeProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private static long nextFailureLogAtTicks;

    /// <summary>Validates the request API key and creates an authenticated principal for its owner.</summary>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var values))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKey = values.ToString();
        if (values.Count != 1 || apiKey.Length is < 16 or > 256)
        {
            LogAuthenticationFailure(
                new EventId(1001, "MalformedApiKey"),
                "API-key authentication failed: malformed header.");
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var apiKeyHash = ApiKeyHasher.Hash(apiKey);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var credential = await dbContext.ApiKeys
            .Include(candidate => candidate.User)
            .SingleOrDefaultAsync(
                candidate => candidate.KeyHash == apiKeyHash
                    && candidate.RevokedAt == null
                    && (candidate.ExpiresAt == null || candidate.ExpiresAt > now),
                Context.RequestAborted);

        if (credential is null)
        {
            LogAuthenticationFailure(
                new EventId(1002, "InvalidApiKey"),
                "API-key authentication failed: credential not usable.");
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, credential.User.Id.ToString()),
            new Claim(ClaimTypes.Name, credential.User.DisplayName),
            new Claim(ApiKeyAuthenticationDefaults.CredentialIdClaim, credential.Id.ToString()),
        };
        if (CredentialPolicy.ShouldUpdateLastUsed(credential.LastUsedAt, now))
        {
            credential.LastUsedAt = now;
            await dbContext.SaveChangesAsync(Context.RequestAborted);
        }
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    /// <summary>Emits at most one sanitized authentication warning every five seconds per process.</summary>
    private void LogAuthenticationFailure(EventId eventId, string message)
    {
        var now = timeProvider.GetUtcNow();
        var nowTicks = now.UtcTicks;
        var observed = Volatile.Read(ref nextFailureLogAtTicks);
        if (nowTicks < observed)
        {
            return;
        }

        var next = now.AddSeconds(5).UtcTicks;
        if (Interlocked.CompareExchange(ref nextFailureLogAtTicks, next, observed) == observed)
        {
            Logger.LogWarning(eventId, "{AuthenticationFailure}", message);
        }
    }
}

/// <summary>Produces the one-way hashes used to store and look up API keys.</summary>
public static class ApiKeyHasher
{
    /// <summary>Hashes an API key with SHA-256 and returns its lowercase hexadecimal representation.</summary>
    public static string Hash(string apiKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
}

/// <summary>Creates cryptographically random API-key secrets for clients.</summary>
public static class ApiKeyGenerator
{
    /// <summary>Generates a URL-safe API key with the service-specific prefix.</summary>
    public static string Generate() =>
        $"bns_{Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))}";
}
