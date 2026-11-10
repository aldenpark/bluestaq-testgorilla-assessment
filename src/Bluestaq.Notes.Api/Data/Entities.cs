namespace Bluestaq.Notes.Api.Data;

public sealed class User
{
    public Guid Id { get; set; }
    public required string DisplayName { get; set; }
    public ICollection<TeamMembership> Memberships { get; set; } = [];
    public ICollection<ApiKeyCredential> ApiKeys { get; set; } = [];
}

public sealed class ApiKeyCredential
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public required string KeyHash { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public sealed class Team
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public ICollection<TeamMembership> Memberships { get; set; } = [];
    public ICollection<Note> Notes { get; set; } = [];
}

public sealed class TeamMembership
{
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime JoinedAt { get; set; }
}

public sealed class Note
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;
    public required string Title { get; set; }
    public required string Content { get; set; }
    public Guid CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;
    public Guid UpdatedById { get; set; }
    public User UpdatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<NotePermission> Permissions { get; set; } = [];
}

public enum NotePermissionRole
{
    Reader,
    Editor,
}

public sealed class NotePermission
{
    public Guid NoteId { get; set; }
    public Note Note { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public NotePermissionRole Role { get; set; }
}

public sealed class SecurityAuditEvent
{
    public Guid Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid ActorCredentialId { get; set; }
    public required string Action { get; set; }
    public Guid? TeamId { get; set; }
    public required string TargetType { get; set; }
    public Guid TargetId { get; set; }
    public required string TraceId { get; set; }
    public required string Details { get; set; }
}
