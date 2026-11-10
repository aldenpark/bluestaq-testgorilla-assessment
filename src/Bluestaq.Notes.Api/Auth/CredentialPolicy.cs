using Bluestaq.Notes.Api.Data;

namespace Bluestaq.Notes.Api.Auth;

/// <summary>Centralizes credential usability, revocation, and activity-write rules.</summary>
public static class CredentialPolicy
{
    public static readonly TimeSpan LastUsedWriteInterval = TimeSpan.FromMinutes(15);

    /// <summary>Returns whether a credential can authenticate at the supplied instant.</summary>
    public static bool IsUsable(ApiKeyCredential credential, DateTime now) =>
        credential.RevokedAt is null && (credential.ExpiresAt is null || credential.ExpiresAt > now);

    /// <summary>Returns whether revoking one credential leaves another usable credential.</summary>
    public static bool CanRevoke(
        Guid credentialId,
        IEnumerable<ApiKeyCredential> credentials,
        DateTime now) =>
        credentials.Any(candidate => candidate.Id != credentialId && IsUsable(candidate, now));

    /// <summary>Returns whether credential activity is old enough to warrant another database write.</summary>
    public static bool ShouldUpdateLastUsed(DateTime? lastUsedAt, DateTime now) =>
        lastUsedAt is null || lastUsedAt <= now - LastUsedWriteInterval;
}
