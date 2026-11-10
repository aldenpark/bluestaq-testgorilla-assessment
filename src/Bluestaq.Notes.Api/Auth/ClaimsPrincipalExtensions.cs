using System.Security.Claims;

namespace Bluestaq.Notes.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static Guid GetCredentialId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ApiKeyAuthenticationDefaults.CredentialIdClaim)!);
}
