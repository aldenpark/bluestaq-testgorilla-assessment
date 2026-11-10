using System.ComponentModel.DataAnnotations;

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
    int Page,
    int PageSize,
    int TotalCount);
