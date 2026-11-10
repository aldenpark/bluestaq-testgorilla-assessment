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

/// <summary>Exposes caller-scoped and owner-scoped views of the immutable security audit trail.</summary>
[ApiController]
[Authorize]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[Route("api")]
public sealed class SecurityEventsController(NotesDbContext dbContext) : ControllerBase
{
    /// <summary>Lists security events performed by or directly affecting the authenticated user.</summary>
    [HttpGet("security-events")]
    public Task<ActionResult<PagedSecurityAuditEventsResponse>> ListPersonal(
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();
        var query = dbContext.SecurityAuditEvents.Where(auditEvent =>
            auditEvent.ActorUserId == userId
            || (auditEvent.TargetId == userId
                && (auditEvent.TargetType == SecurityAuditTargets.User
                    || auditEvent.TargetType == SecurityAuditTargets.TeamMembership
                    || auditEvent.TargetType == SecurityAuditTargets.NotePermission)));
        return Page(query, cursor, pageSize, cancellationToken);
    }

    /// <summary>Lists a team's security events when the authenticated user owns that team.</summary>
    [HttpGet("teams/{teamId:guid}/security-events")]
    public async Task<ActionResult<PagedSecurityAuditEventsResponse>> ListTeam(
        Guid teamId,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var ownerId = await dbContext.Teams
            .Where(team => team.Id == teamId)
            .Select(team => (Guid?)team.OwnerId)
            .SingleOrDefaultAsync(cancellationToken);
        if (ownerId is null)
        {
            return Problem("The team does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        if (ownerId != User.GetUserId())
        {
            return Forbid();
        }

        return await Page(
            dbContext.SecurityAuditEvents.Where(auditEvent => auditEvent.TeamId == teamId),
            cursor,
            pageSize,
            cancellationToken);
    }

    /// <summary>Validates pagination inputs and materializes a newest-first page of audit events.</summary>
    private async Task<ActionResult<PagedSecurityAuditEventsResponse>> Page(
        IQueryable<SecurityAuditEvent> query,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (pageSize is < 1 or > 100)
        {
            ModelState.AddModelError(nameof(pageSize), "PageSize must be between 1 and 100.");
            return ValidationProblem(ModelState);
        }

        AuditCursor? decodedCursor = null;
        if (cursor is not null && !TryDecodeCursor(cursor, out decodedCursor))
        {
            ModelState.AddModelError(nameof(cursor), "Cursor is invalid.");
            return ValidationProblem(ModelState);
        }

        if (decodedCursor is not null)
        {
            query = query.Where(auditEvent => auditEvent.OccurredAt < decodedCursor.OccurredAt
                || (auditEvent.OccurredAt == decodedCursor.OccurredAt
                    && auditEvent.Id.CompareTo(decodedCursor.Id) < 0));
        }

        var auditEvents = await query
            .AsNoTracking()
            .OrderByDescending(auditEvent => auditEvent.OccurredAt)
            .ThenByDescending(auditEvent => auditEvent.Id)
            .Take(pageSize + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = auditEvents.Length > pageSize;
        var items = auditEvents.Take(pageSize).Select(ToResponse).ToArray();
        var nextCursor = hasMore ? EncodeCursor(auditEvents[pageSize - 1]) : null;
        return Ok(new PagedSecurityAuditEventsResponse(items, nextCursor));
    }

    /// <summary>Maps a persisted audit event to its API response contract.</summary>
    private static SecurityAuditEventResponse ToResponse(SecurityAuditEvent auditEvent) =>
        new(
            auditEvent.Id,
            auditEvent.OccurredAt,
            auditEvent.ActorUserId,
            auditEvent.ActorCredentialId,
            auditEvent.Action,
            auditEvent.TeamId,
            auditEvent.TargetType,
            auditEvent.TargetId,
            auditEvent.TraceId,
            ParseDetails(auditEvent.Details));

    /// <summary>Parses stored JSON details into a response-safe detached JSON value.</summary>
    private static JsonElement ParseDetails(string details)
    {
        using var document = JsonDocument.Parse(details);
        return document.RootElement.Clone();
    }

    /// <summary>Encodes an audit event's stable pagination position as an opaque URL-safe cursor.</summary>
    private static string EncodeCursor(SecurityAuditEvent auditEvent) =>
        WebEncoders.Base64UrlEncode(
            JsonSerializer.SerializeToUtf8Bytes(new AuditCursor(auditEvent.OccurredAt, auditEvent.Id)));

    /// <summary>Attempts to decode and validate an opaque audit-event pagination cursor.</summary>
    private static bool TryDecodeCursor(string cursor, out AuditCursor? decoded)
    {
        try
        {
            decoded = JsonSerializer.Deserialize<AuditCursor>(WebEncoders.Base64UrlDecode(cursor));
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

    /// <summary>Captures the timestamp and identifier used for stable audit-event pagination.</summary>
    private sealed record AuditCursor(DateTime OccurredAt, Guid Id);
}
