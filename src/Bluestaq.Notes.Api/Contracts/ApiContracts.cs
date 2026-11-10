using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Bluestaq.Notes.Api.Data;

namespace Bluestaq.Notes.Api.Contracts;

/// <summary>Contains the values required to create a team.</summary>
public sealed class CreateTeamRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }
}

/// <summary>Identifies an existing user to add to a team.</summary>
public sealed class AddTeamMemberRequest
{
    public Guid UserId { get; init; }
}

/// <summary>Identifies the current team member who will become the new owner.</summary>
public sealed class TransferTeamOwnershipRequest
{
    public Guid UserId { get; init; }
}

/// <summary>Contains the values that may be changed on a team.</summary>
public sealed class UpdateTeamRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }
}

/// <summary>Contains the new-user and initial-credential values required for a team invitation.</summary>
public sealed class InviteTeamMemberRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public required string DisplayName { get; init; }

    [Required, StringLength(100, MinimumLength = 1)]
    public required string KeyName { get; init; }

    public DateTime? ExpiresAt { get; init; }
}

/// <summary>Describes an invited user, their membership, and the one-time credential returned to the owner.</summary>
public sealed record InvitationResponse(
    Guid UserId,
    string DisplayName,
    DateTime JoinedAt,
    ApiKeyCreatedResponse Credential);

/// <summary>Represents a team and its current members.</summary>
public sealed record TeamResponse(
    Guid Id,
    string Name,
    Guid OwnerId,
    DateTime CreatedAt,
    int Version,
    IReadOnlyCollection<TeamMemberResponse> Members);

/// <summary>Represents one user's membership in a team.</summary>
public sealed record TeamMemberResponse(Guid UserId, string DisplayName, DateTime JoinedAt);

/// <summary>Contains the title and content required to create a note.</summary>
public sealed class CreateNoteRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public required string Title { get; init; }

    [Required, StringLength(10_000)]
    public required string Content { get; init; }

    [EnumDataType(typeof(NoteVisibility))]
    public NoteVisibility Visibility { get; init; } = NoteVisibility.Team;
}

/// <summary>Contains the replacement title and content for an existing note.</summary>
public sealed class UpdateNoteRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public required string Title { get; init; }

    [Required, StringLength(10_000)]
    public required string Content { get; init; }
}

/// <summary>Represents a note returned through the API.</summary>
public sealed record NoteResponse(
    Guid Id,
    Guid TeamId,
    string Title,
    string Content,
    NoteVisibility Visibility,
    int Version,
    Guid CreatedById,
    Guid UpdatedById,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Contains one page of notes and an optional cursor for the next page.</summary>
public sealed record PagedNotesResponse(
    IReadOnlyCollection<NoteResponse> Items,
    string? NextCursor);

/// <summary>Contains the name and optional expiration for a new API key.</summary>
public sealed class CreateApiKeyRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    public DateTime? ExpiresAt { get; init; }
}

/// <summary>Represents API-key metadata without exposing the secret value or stored hash.</summary>
public sealed record ApiKeyResponse(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    DateTime? LastUsedAt);

/// <summary>Represents a newly created API key, including the secret returned only at creation time.</summary>
public sealed record ApiKeyCreatedResponse(
    Guid Id,
    string Name,
    string ApiKey,
    DateTime CreatedAt,
    DateTime? ExpiresAt);

/// <summary>Contains the role to grant a team member on a note.</summary>
public sealed class SetNotePermissionRequest
{
    [Required, EnumDataType(typeof(NotePermissionRole))]
    public NotePermissionRole? Role { get; init; }
}

/// <summary>Represents a user's explicit permission on a note.</summary>
public sealed record NotePermissionResponse(Guid UserId, string DisplayName, NotePermissionRole Role);

/// <summary>Represents an immutable security-relevant change recorded by the service.</summary>
public sealed record SecurityAuditEventResponse(
    Guid Id,
    DateTime OccurredAt,
    Guid ActorUserId,
    Guid ActorCredentialId,
    string Action,
    Guid? TeamId,
    string TargetType,
    Guid TargetId,
    string TraceId,
    string IntegrityHash,
    JsonElement Details);

/// <summary>Contains one page of security audit events and an optional cursor for the next page.</summary>
public sealed record PagedSecurityAuditEventsResponse(
    IReadOnlyCollection<SecurityAuditEventResponse> Items,
    string? NextCursor);
