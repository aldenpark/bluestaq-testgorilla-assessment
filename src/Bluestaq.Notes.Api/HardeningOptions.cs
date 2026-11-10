namespace Bluestaq.Notes.Api;

/// <summary>Defines configurable request, audit, and API-lifecycle hardening limits.</summary>
public sealed class HardeningOptions
{
    public const string SectionName = "Hardening";

    public int RequestBodyLimitBytes { get; init; } = 16_384;
    public int AuthenticatedPermitLimit { get; init; } = 300;
    public int AnonymousPermitLimit { get; init; } = 60;
    public int RateLimitWindowSeconds { get; init; } = 60;
    public string AuditIntegrityKey { get; init; } = string.Empty;
    public bool EnableConsoleTracing { get; init; }
    public DateTime LegacyApiSunsetUtc { get; init; } = new(2027, 5, 10, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Validates limits and the minimum 256-bit audit-integrity key.</summary>
    public bool IsValid()
    {
        if (RequestBodyLimitBytes < 1_024
            || AuthenticatedPermitLimit < 1
            || AnonymousPermitLimit < 1
            || RateLimitWindowSeconds < 1)
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(AuditIntegrityKey).Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
