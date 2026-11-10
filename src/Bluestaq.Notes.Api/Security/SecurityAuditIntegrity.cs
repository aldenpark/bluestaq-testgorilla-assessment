using System.Security.Cryptography;
using System.Text;
using Bluestaq.Notes.Api.Data;
using Microsoft.Extensions.Options;

namespace Bluestaq.Notes.Api.Security;

/// <summary>Creates and verifies keyed integrity tags for immutable security audit events.</summary>
public sealed class SecurityAuditIntegrity(IOptions<HardeningOptions> options)
{
    private readonly byte[] key = Convert.FromBase64String(options.Value.AuditIntegrityKey);

    /// <summary>Computes a lowercase HMAC-SHA256 tag over every immutable event field.</summary>
    public string Compute(SecurityAuditEvent auditEvent)
    {
        var canonical = string.Join(
            '\n',
            auditEvent.Id.ToString("N"),
            auditEvent.OccurredAt.ToUniversalTime().ToString("O"),
            auditEvent.ActorUserId.ToString("N"),
            auditEvent.ActorCredentialId.ToString("N"),
            auditEvent.Action,
            auditEvent.TeamId?.ToString("N") ?? string.Empty,
            auditEvent.TargetType,
            auditEvent.TargetId.ToString("N"),
            auditEvent.TraceId,
            auditEvent.Details);
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>Verifies an event tag in fixed time; legacy migration markers are unverifiable.</summary>
    public bool Verify(SecurityAuditEvent auditEvent) =>
        auditEvent.IntegrityHash.Length == 64
        && CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(auditEvent.IntegrityHash),
            Convert.FromHexString(Compute(auditEvent)));
}
