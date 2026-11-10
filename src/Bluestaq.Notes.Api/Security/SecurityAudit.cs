using System.Text.Json;
using Bluestaq.Notes.Api.Auth;
using Bluestaq.Notes.Api.Data;

namespace Bluestaq.Notes.Api.Security;

public static class SecurityAuditActions
{
    public const string TeamCreated = "team.created";
    public const string TeamDeleted = "team.deleted";
    public const string UserInvited = "user.invited";
    public const string TeamMemberAdded = "team.member-added";
    public const string TeamMemberRemoved = "team.member-removed";
    public const string ApiKeyCreated = "api-key.created";
    public const string ApiKeyRotated = "api-key.rotated";
    public const string ApiKeyRevoked = "api-key.revoked";
    public const string NotePermissionSet = "note.permission-set";
    public const string NotePermissionRemoved = "note.permission-removed";
}

public static class SecurityAuditTargets
{
    public const string Team = "team";
    public const string User = "user";
    public const string TeamMembership = "team-membership";
    public const string ApiKey = "api-key";
    public const string NotePermission = "note-permission";
}

public sealed class SecurityAudit(NotesDbContext dbContext, IHttpContextAccessor httpContextAccessor)
{
    public void Record(
        string action,
        string targetType,
        Guid targetId,
        Guid? teamId = null,
        object? details = null)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("An active HTTP request is required.");

        dbContext.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
            ActorUserId = httpContext.User.GetUserId(),
            ActorCredentialId = httpContext.User.GetCredentialId(),
            Action = action,
            TeamId = teamId,
            TargetType = targetType,
            TargetId = targetId,
            TraceId = httpContext.TraceIdentifier,
            Details = details is null ? "{}" : JsonSerializer.Serialize(details, JsonSerializerOptions.Web),
        });
    }
}
