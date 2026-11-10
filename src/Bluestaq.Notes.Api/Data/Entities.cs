using System.Text.Json.Serialization;

namespace Bluestaq.Notes.Api.Data;

/// <summary>Represents a person who can authenticate and collaborate in teams.</summary>
public sealed class User
{
    public Guid Id { get; set; }
    public required string DisplayName { get; set; }
    public int CredentialVersion { get; set; } = 1;
    public ICollection<TeamMembership> Memberships { get; set; } = [];
    public ICollection<ApiKeyCredential> ApiKeys { get; set; } = [];
}

/// <summary>Represents a named, hashed API-key credential owned by a user.</summary>
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

/// <summary>Represents a collaborative workspace owned by one user.</summary>
public sealed class Team
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public int Version { get; set; } = 1;
    public ICollection<TeamMembership> Memberships { get; set; } = [];
    public ICollection<Note> Notes { get; set; } = [];
}

/// <summary>Associates a user with a team and records when they joined.</summary>
public sealed class TeamMembership
{
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime JoinedAt { get; set; }
}

/// <summary>Represents a team-owned note and its authorship metadata.</summary>
public sealed class Note
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;
    public required string Title { get; set; }
    public required string Content { get; set; }
    public NoteVisibility Visibility { get; set; } = NoteVisibility.Team;
    public int Version { get; set; } = 1;
    public Guid CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;
    public Guid UpdatedById { get; set; }
    public User UpdatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<NotePermission> Permissions { get; set; } = [];
}

/// <summary>Defines whether a note is visible to all team members or only explicitly authorized members.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NoteVisibility>))]
public enum NoteVisibility
{
    Team,
    Restricted,
}

/// <summary>Defines the explicit access levels that may be granted on a note.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotePermissionRole>))]
public enum NotePermissionRole
{
    Reader,
    Editor,
}

/// <summary>Associates a team member with an explicit role on a note.</summary>
public sealed class NotePermission
{
    public Guid NoteId { get; set; }
    public Note Note { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public NotePermissionRole Role { get; set; }
}

/// <summary>Represents an immutable record of a security-relevant change.</summary>
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
    public required string IntegrityHash { get; set; }
}

/// <summary>Provides a durable sentinel for rollback-safe database writeability probes.</summary>
public sealed class ReadinessProbe
{
    public int Id { get; set; }
    public DateTime CheckedAt { get; set; }
}
