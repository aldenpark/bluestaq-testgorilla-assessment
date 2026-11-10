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

/// <summary>Manages team notes, note discovery, and per-note permissions.</summary>
[ApiController]
[Authorize]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[Route("api")]
[Route("api/v1")]
public sealed class NotesController(
    NotesDbContext dbContext,
    SecurityAudit securityAudit,
    TimeProvider timeProvider) : ControllerBase
{
    /// <summary>Creates a note in a team where the authenticated user is a current member.</summary>
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
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var note = new Note
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            Title = request.Title.Trim(),
            Content = request.Content,
            Visibility = request.Visibility,
            Version = 1,
            CreatedById = userId,
            UpdatedById = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        dbContext.Notes.Add(note);
        await dbContext.SaveChangesAsync(cancellationToken);

        SetEtag(note.Version);
        return CreatedAtAction(nameof(Get), new { noteId = note.Id }, ToResponse(note));
    }

    /// <summary>Returns a filtered, newest-first cursor page of notes for a team member.</summary>
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

        var userId = User.GetUserId();
        var isOwner = await dbContext.Teams.AnyAsync(
            team => team.Id == teamId && team.OwnerId == userId,
            cancellationToken);
        query = query.Where(note => note.TeamId == teamId
            && (note.Visibility == NoteVisibility.Team
                || note.CreatedById == userId
                || isOwner
                || note.Permissions.Any(permission => permission.UserId == userId)));
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

    /// <summary>Returns a note when the authenticated user belongs to its team.</summary>
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

        if (!await CanRead(note, User.GetUserId(), cancellationToken))
        {
            return Problem("The note does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        SetEtag(note.Version);
        return Ok(ToResponse(note));
    }

    /// <summary>Replaces a note's content when the caller is its creator, team owner, or an editor.</summary>
    [HttpPut("notes/{noteId:guid}")]
    [ProducesResponseType(typeof(NoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
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


        var versionResult = CheckVersion(note.Version, "note update");
        if (versionResult is not null)
        {
            return versionResult;
        }

        note.Title = request.Title.Trim();
        note.Content = request.Content;
        note.UpdatedById = userId;
        note.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        note.Version++;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem("The note changed after it was read.", statusCode: StatusCodes.Status412PreconditionFailed);
        }

        SetEtag(note.Version);
        return Ok(ToResponse(note));
    }

    /// <summary>Deletes a note when the caller is its creator or team owner.</summary>
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

        var versionResult = CheckVersion(note.Version, "note deletion");
        if (versionResult is not null)
        {
            return versionResult;
        }

        dbContext.Notes.Remove(note);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem("The note changed after it was read.", statusCode: StatusCodes.Status412PreconditionFailed);
        }

        return NoContent();
    }

    /// <summary>Lists explicit note permissions for the note creator or team owner.</summary>
    [HttpGet("notes/{noteId:guid}/permissions")]
    public async Task<ActionResult<IReadOnlyCollection<NotePermissionResponse>>> ListPermissions(
        Guid noteId,
        CancellationToken cancellationToken)
    {
        var note = await dbContext.Notes.SingleOrDefaultAsync(
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

    /// <summary>Creates or replaces a team member's explicit permission on a note.</summary>
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

        var note = await dbContext.Notes.SingleOrDefaultAsync(
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

        var versionResult = CheckVersion(note.Version, "permission update");
        if (versionResult is not null)
        {
            return versionResult;
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
        note.Version++;
        note.UpdatedById = User.GetUserId();
        note.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem("The note changed after it was read.", statusCode: StatusCodes.Status412PreconditionFailed);
        }

        SetEtag(note.Version);
        return Ok(new NotePermissionResponse(member.UserId, member.DisplayName, permission.Role));
    }

    /// <summary>Removes a team member's explicit permission from a note.</summary>
    [HttpDelete("notes/{noteId:guid}/permissions/{userId:guid}")]
    public async Task<IActionResult> DeletePermission(
        Guid noteId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var note = await dbContext.Notes.SingleOrDefaultAsync(
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

        var versionResult = CheckVersion(note.Version, "permission deletion");
        if (versionResult is not null)
        {
            return versionResult;
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
            note.Version++;
            note.UpdatedById = User.GetUserId();
            note.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Problem("The note changed after it was read.", statusCode: StatusCodes.Status412PreconditionFailed);
            }

            SetEtag(note.Version);
        }

        return NoContent();
    }

    /// <summary>Returns an HTTP error when a team is missing or the caller is not a member.</summary>
    private async Task<ActionResult?> CheckTeamAccess(Guid teamId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Teams.AnyAsync(team => team.Id == teamId, cancellationToken))
        {
            return Problem("The team does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        return await IsMember(teamId, cancellationToken) ? null : Forbid();
    }

    /// <summary>Determines whether the authenticated user currently belongs to a team.</summary>
    private Task<bool> IsMember(Guid teamId, CancellationToken cancellationToken) =>
        dbContext.TeamMemberships.AnyAsync(
            membership => membership.TeamId == teamId && membership.UserId == User.GetUserId(),
            cancellationToken);

    /// <summary>Determines whether a user may update a note under membership, ownership, and editor rules.</summary>
    private async Task<bool> CanEdit(Note note, Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsMember(note.TeamId, cancellationToken))
        {
            return false;
        }

        var access = await GetAccessContext(note, userId, cancellationToken);
        return NoteAccessPolicy.CanEdit(note.CreatedById, access.OwnerId, userId, access.Role);
    }

    /// <summary>Determines whether a user may delete a note or manage its permissions.</summary>
    private async Task<bool> CanAdminister(Note note, Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsMember(note.TeamId, cancellationToken))
        {
            return false;
        }

        var ownerId = await dbContext.Teams
            .Where(team => team.Id == note.TeamId)
            .Select(team => team.OwnerId)
            .SingleAsync(cancellationToken);
        return NoteAccessPolicy.CanAdminister(note.CreatedById, ownerId, userId);
    }

    /// <summary>Determines whether a team member may retrieve a note under its visibility rules.</summary>
    private async Task<bool> CanRead(Note note, Guid userId, CancellationToken cancellationToken)
    {
        var access = await GetAccessContext(note, userId, cancellationToken);
        return NoteAccessPolicy.CanRead(note.Visibility, note.CreatedById, access.OwnerId, userId, access.Role);
    }

    /// <summary>Loads team ownership and any explicit note role used by access policies.</summary>
    private async Task<(Guid OwnerId, NotePermissionRole? Role)> GetAccessContext(
        Note note,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var ownerId = await dbContext.Teams
            .Where(team => team.Id == note.TeamId)
            .Select(team => team.OwnerId)
            .SingleAsync(cancellationToken);
        var role = await dbContext.NotePermissions
            .Where(permission => permission.NoteId == note.Id && permission.UserId == userId)
            .Select(permission => (NotePermissionRole?)permission.Role)
            .SingleOrDefaultAsync(cancellationToken);
        return (ownerId, role);
    }

    /// <summary>Encodes a note's stable pagination position as an opaque URL-safe cursor.</summary>
    private static string EncodeCursor(Note note) =>
        WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new NoteCursor(note.UpdatedAt, note.Id)));

    /// <summary>Attempts to decode and validate an opaque note pagination cursor.</summary>
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

    /// <summary>Maps a persisted note entity to its API response contract.</summary>
    private static NoteResponse ToResponse(Note note) =>
        new(
            note.Id,
            note.TeamId,
            note.Title,
            note.Content,
            note.Visibility,
            note.Version,
            note.CreatedById,
            note.UpdatedById,
            note.CreatedAt,
            note.UpdatedAt);

    /// <summary>Writes a strong entity tag containing the current note version.</summary>
    private void SetEtag(int version) => Response.Headers.ETag = EntityTagPolicy.Format(version);

    /// <summary>Requires a matching strong entity tag before a note mutation.</summary>
    private ActionResult? CheckVersion(int currentVersion, string operation)
    {
        if (!Request.Headers.TryGetValue("If-Match", out var ifMatch))
        {
            return Problem(
                $"If-Match is required for {operation}.",
                statusCode: StatusCodes.Status428PreconditionRequired);
        }

        if (!EntityTagPolicy.TryParse(ifMatch.ToString(), out var expectedVersion))
        {
            return Problem("If-Match must contain a quoted resource version.", statusCode: StatusCodes.Status400BadRequest);
        }

        return currentVersion == expectedVersion
            ? null
            : Problem("The note changed after it was read.", statusCode: StatusCodes.Status412PreconditionFailed);
    }

    /// <summary>Captures the timestamp and identifier used for stable note pagination.</summary>
    private sealed record NoteCursor(DateTime UpdatedAt, Guid Id);
}
