using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Bluestaq.Notes.Api.Data;

namespace Bluestaq.Notes.Api.Contracts;

public sealed class CreateTeamRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }
}

public sealed class AddTeamMemberRequest
{
    public Guid UserId { get; init; }
}

public sealed class UpdateTeamRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }
}

public sealed class InviteTeamMemberRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public required string DisplayName { get; init; }

    [Required, StringLength(100, MinimumLength = 1)]
    public required string KeyName { get; init; }

    public DateTime? ExpiresAt { get; init; }
}

public sealed record InvitationResponse(
    Guid UserId,
    string DisplayName,
    DateTime JoinedAt,
    ApiKeyCreatedResponse Credential);

public sealed record TeamResponse(
    Guid Id,
    string Name,
    Guid OwnerId,
    DateTime CreatedAt,
    IReadOnlyCollection<TeamMemberResponse> Members);

public sealed record TeamMemberResponse(Guid UserId, string DisplayName, DateTime JoinedAt);

public sealed class CreateNoteRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public required string Title { get; init; }

    [Required, StringLength(10_000)]
    public required string Content { get; init; }
}

public sealed class UpdateNoteRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public required string Title { get; init; }

    [Required, StringLength(10_000)]
    public required string Content { get; init; }
}

public sealed record NoteResponse(
    Guid Id,
    Guid TeamId,
    string Title,
    string Content,
    Guid CreatedById,
    Guid UpdatedById,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record PagedNotesResponse(
    IReadOnlyCollection<NoteResponse> Items,
    string? NextCursor);

public sealed class CreateApiKeyRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    public DateTime? ExpiresAt { get; init; }
}

public sealed record ApiKeyResponse(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    DateTime? LastUsedAt);

public sealed record ApiKeyCreatedResponse(
    Guid Id,
    string Name,
    string ApiKey,
    DateTime CreatedAt,
    DateTime? ExpiresAt);

public sealed class SetNotePermissionRequest
{
    [Required]
    public NotePermissionRole? Role { get; init; }
}

public sealed record NotePermissionResponse(Guid UserId, string DisplayName, NotePermissionRole Role);

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
    JsonElement Details);

public sealed record PagedSecurityAuditEventsResponse(
    IReadOnlyCollection<SecurityAuditEventResponse> Items,
    string? NextCursor);
