using System.Security.Claims;

namespace Bluestaq.Notes.Api.Auth;

/// <summary>Provides strongly typed access to identity values stored in authenticated claims.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>Returns the authenticated user's identifier from the name-identifier claim.</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Returns the API-key credential identifier used to authenticate the current request.</summary>
    public static Guid GetCredentialId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ApiKeyAuthenticationDefaults.CredentialIdClaim)!);
}
