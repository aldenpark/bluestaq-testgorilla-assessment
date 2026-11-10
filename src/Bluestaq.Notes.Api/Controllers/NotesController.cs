using System.Text.Json;
using Bluestaq.Notes.Api.Auth;
using Bluestaq.Notes.Api.Contracts;
using Bluestaq.Notes.Api.Data;
using Bluestaq.Notes.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Bluestaq.Notes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class NotesController(NotesDbContext dbContext, SecurityAudit securityAudit) : ControllerBase
{
    [HttpPost("teams/{teamId:guid}/notes")]
    public async Task<ActionResult<NoteResponse>> Create(
        Guid teamId,
        CreateNoteRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            ModelState.AddModelError(nameof(request.Title), "Title must contain non-whitespace characters.");
            return ValidationProblem(ModelState);
        }

        var accessResult = await CheckTeamAccess(teamId, cancellationToken);
        if (accessResult is not null)
        {
            return accessResult;
        }

        var userId = User.GetUserId();
        var now = DateTime.UtcNow;
        var note = new Note
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            Title = request.Title.Trim(),
            Content = request.Content,
            CreatedById = userId,
            UpdatedById = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        dbContext.Notes.Add(note);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { noteId = note.Id }, ToResponse(note));
    }

    [HttpGet("teams/{teamId:guid}/notes")]
    public async Task<ActionResult<PagedNotesResponse>> List(
        Guid teamId,
        [FromQuery] string? q = null,
        [FromQuery] Guid? createdById = null,
        [FromQuery] DateTime? updatedAfter = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 100)
        {
            ModelState.AddModelError(nameof(pageSize), "PageSize must be between 1 and 100.");
            return ValidationProblem(ModelState);
        }

        if (q?.Length > 200)
        {
            ModelState.AddModelError(nameof(q), "Search query must not exceed 200 characters.");
            return ValidationProblem(ModelState);
        }

        NoteCursor? decodedCursor = null;
        if (cursor is not null && !TryDecodeCursor(cursor, out decodedCursor))
        {
            ModelState.AddModelError(nameof(cursor), "Cursor is invalid.");
            return ValidationProblem(ModelState);
        }

        var accessResult = await CheckTeamAccess(teamId, cancellationToken);
        if (accessResult is not null)
        {
            return accessResult;
        }

        IQueryable<Note> query = dbContext.Notes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var phrase = $"\"{q.Trim().Replace("\"", "\"\"")}\"";
            query = dbContext.Notes.FromSqlInterpolated($$"""
                SELECT n.*
                FROM "Notes" AS n
                INNER JOIN "NotesFts" AS f ON f.rowid = n.rowid
                WHERE "NotesFts" MATCH {{phrase}}
                """).AsNoTracking();
        }

        query = query.Where(note => note.TeamId == teamId);
        if (createdById is not null)
        {
            query = query.Where(note => note.CreatedById == createdById);
        }

        if (updatedAfter is not null)
        {
            var updatedAfterUtc = updatedAfter.Value.ToUniversalTime();
            query = query.Where(note => note.UpdatedAt >= updatedAfterUtc);
        }

        if (decodedCursor is not null)
        {
            query = query.Where(note => note.UpdatedAt < decodedCursor.UpdatedAt
                || (note.UpdatedAt == decodedCursor.UpdatedAt && note.Id.CompareTo(decodedCursor.Id) < 0));
        }

        var notes = await query
            .OrderByDescending(note => note.UpdatedAt)
            .ThenByDescending(note => note.Id)
            .Take(pageSize + 1)
            .ToArrayAsync(cancellationToken);

        var hasMore = notes.Length > pageSize;
        var items = notes.Take(pageSize).Select(ToResponse).ToArray();
        var nextCursor = hasMore ? EncodeCursor(notes[pageSize - 1]) : null;
        return Ok(new PagedNotesResponse(items, nextCursor));
    }

    [HttpGet("notes/{noteId:guid}")]
    public async Task<ActionResult<NoteResponse>> Get(Guid noteId, CancellationToken cancellationToken)
    {
        var note = await dbContext.Notes.AsNoTracking().SingleOrDefaultAsync(note => note.Id == noteId, cancellationToken);
        if (note is null)
        {
            return Problem("The note does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!await IsMember(note.TeamId, cancellationToken))
        {
            return Forbid();
        }

        return Ok(ToResponse(note));
    }

    [HttpPut("notes/{noteId:guid}")]
    public async Task<ActionResult<NoteResponse>> Update(
        Guid noteId,
        UpdateNoteRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            ModelState.AddModelError(nameof(request.Title), "Title must contain non-whitespace characters.");
            return ValidationProblem(ModelState);
        }

        var note = await dbContext.Notes.SingleOrDefaultAsync(note => note.Id == noteId, cancellationToken);
        if (note is null)
        {
            return Problem("The note does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        var userId = User.GetUserId();
        if (!await CanEdit(note, userId, cancellationToken))
        {
            return Forbid();
        }

        note.Title = request.Title.Trim();
        note.Content = request.Content;
        note.UpdatedById = userId;
        note.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(note));
    }

    [HttpDelete("notes/{noteId:guid}")]
    public async Task<IActionResult> Delete(Guid noteId, CancellationToken cancellationToken)
    {
        var note = await dbContext.Notes.SingleOrDefaultAsync(note => note.Id == noteId, cancellationToken);
        if (note is null)
        {
            return Problem("The note does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!await CanAdminister(note, User.GetUserId(), cancellationToken))
        {
            return Forbid();
        }

        dbContext.Notes.Remove(note);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("notes/{noteId:guid}/permissions")]
    public async Task<ActionResult<IReadOnlyCollection<NotePermissionResponse>>> ListPermissions(
        Guid noteId,
        CancellationToken cancellationToken)
    {
        var note = await dbContext.Notes.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == noteId,
            cancellationToken);
        if (note is null)
        {
            return Problem("The note does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!await CanAdminister(note, User.GetUserId(), cancellationToken))
        {
            return Forbid();
        }

        var permissions = await dbContext.NotePermissions
            .AsNoTracking()
            .Where(permission => permission.NoteId == noteId)
            .OrderBy(permission => permission.User.DisplayName)
            .Select(permission => new NotePermissionResponse(
                permission.UserId,
                permission.User.DisplayName,
                permission.Role))
            .ToArrayAsync(cancellationToken);
        return Ok(permissions);
    }

    [HttpPut("notes/{noteId:guid}/permissions/{userId:guid}")]
    public async Task<ActionResult<NotePermissionResponse>> SetPermission(
        Guid noteId,
        Guid userId,
        SetNotePermissionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Role is null)
        {
            ModelState.AddModelError(nameof(request.Role), "Role is required.");
            return ValidationProblem(ModelState);
        }

        var note = await dbContext.Notes.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == noteId,
            cancellationToken);
        if (note is null)
        {
            return Problem("The note does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!await CanAdminister(note, User.GetUserId(), cancellationToken))
        {
            return Forbid();
        }

        var member = await dbContext.TeamMemberships
            .Where(membership => membership.TeamId == note.TeamId && membership.UserId == userId)
            .Select(membership => new { membership.UserId, membership.User.DisplayName })
            .SingleOrDefaultAsync(cancellationToken);
        if (member is null)
        {
            return Problem("The user is not a team member.", statusCode: StatusCodes.Status409Conflict);
        }

        var permission = await dbContext.NotePermissions.FindAsync([noteId, userId], cancellationToken);
        var previousRole = permission?.Role.ToString().ToLowerInvariant();
        if (permission is null)
        {
            permission = new NotePermission { NoteId = noteId, UserId = userId, Role = request.Role.Value };
            dbContext.NotePermissions.Add(permission);
        }
        else
        {
            permission.Role = request.Role.Value;
        }

        securityAudit.Record(
            SecurityAuditActions.NotePermissionSet,
            SecurityAuditTargets.NotePermission,
            userId,
            note.TeamId,
            new
            {
                NoteId = note.Id,
                PreviousRole = previousRole,
                Role = permission.Role.ToString().ToLowerInvariant(),
            });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new NotePermissionResponse(member.UserId, member.DisplayName, permission.Role));
    }

    [HttpDelete("notes/{noteId:guid}/permissions/{userId:guid}")]
    public async Task<IActionResult> DeletePermission(
        Guid noteId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var note = await dbContext.Notes.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == noteId,
            cancellationToken);
        if (note is null)
        {
            return Problem("The note does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!await CanAdminister(note, User.GetUserId(), cancellationToken))
        {
            return Forbid();
        }

        var permission = await dbContext.NotePermissions.FindAsync([noteId, userId], cancellationToken);
        if (permission is not null)
        {
            dbContext.NotePermissions.Remove(permission);
            securityAudit.Record(
                SecurityAuditActions.NotePermissionRemoved,
                SecurityAuditTargets.NotePermission,
                userId,
                note.TeamId,
                new { NoteId = note.Id, Role = permission.Role.ToString().ToLowerInvariant() });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    private async Task<ActionResult?> CheckTeamAccess(Guid teamId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Teams.AnyAsync(team => team.Id == teamId, cancellationToken))
        {
            return Problem("The team does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        return await IsMember(teamId, cancellationToken) ? null : Forbid();
    }

    private Task<bool> IsMember(Guid teamId, CancellationToken cancellationToken) =>
        dbContext.TeamMemberships.AnyAsync(
            membership => membership.TeamId == teamId && membership.UserId == User.GetUserId(),
            cancellationToken);

    private async Task<bool> CanEdit(Note note, Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsMember(note.TeamId, cancellationToken))
        {
            return false;
        }

        return note.CreatedById == userId
            || await dbContext.Teams.AnyAsync(
                team => team.Id == note.TeamId && team.OwnerId == userId,
                cancellationToken)
            || await dbContext.NotePermissions.AnyAsync(
                permission => permission.NoteId == note.Id
                    && permission.UserId == userId
                    && permission.Role == NotePermissionRole.Editor,
                cancellationToken);
    }

    private async Task<bool> CanAdminister(Note note, Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsMember(note.TeamId, cancellationToken))
        {
            return false;
        }

        return note.CreatedById == userId
            || await dbContext.Teams.AnyAsync(
                team => team.Id == note.TeamId && team.OwnerId == userId,
                cancellationToken);
    }

    private static string EncodeCursor(Note note) =>
        WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new NoteCursor(note.UpdatedAt, note.Id)));

    private static bool TryDecodeCursor(string cursor, out NoteCursor? decoded)
    {
        try
        {
            decoded = JsonSerializer.Deserialize<NoteCursor>(WebEncoders.Base64UrlDecode(cursor));
            return decoded is not null && decoded.Id != Guid.Empty;
        }
        catch (FormatException)
        {
            decoded = null;
            return false;
        }
        catch (JsonException)
        {
            decoded = null;
            return false;
        }
    }

    private static NoteResponse ToResponse(Note note) =>
        new(
            note.Id,
            note.TeamId,
            note.Title,
            note.Content,
            note.CreatedById,
            note.UpdatedById,
            note.CreatedAt,
            note.UpdatedAt);

    private sealed record NoteCursor(DateTime UpdatedAt, Guid Id);
}
