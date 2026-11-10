using Bluestaq.Notes.Api.Auth;
using Bluestaq.Notes.Api.Contracts;
using Bluestaq.Notes.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bluestaq.Notes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class NotesController(NotesDbContext dbContext) : ControllerBase
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
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100)
        {
            if (page < 1)
            {
                ModelState.AddModelError(nameof(page), "Page must be at least 1.");
            }

            if (pageSize is < 1 or > 100)
            {
                ModelState.AddModelError(nameof(pageSize), "PageSize must be between 1 and 100.");
            }

            return ValidationProblem(ModelState);
        }

        var accessResult = await CheckTeamAccess(teamId, cancellationToken);
        if (accessResult is not null)
        {
            return accessResult;
        }

        var query = dbContext.Notes.AsNoTracking().Where(note => note.TeamId == teamId);
        var totalCount = await query.CountAsync(cancellationToken);
        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue)
        {
            ModelState.AddModelError(nameof(page), "The requested page is too large.");
            return ValidationProblem(ModelState);
        }

        var notes = await query
            .OrderByDescending(note => note.UpdatedAt)
            .ThenBy(note => note.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(note => new NoteResponse(
                note.Id,
                note.TeamId,
                note.Title,
                note.Content,
                note.CreatedById,
                note.UpdatedById,
                note.CreatedAt,
                note.UpdatedAt))
            .ToArrayAsync(cancellationToken);

        return Ok(new PagedNotesResponse(notes, page, pageSize, totalCount));
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

        if (!await IsMember(note.TeamId, cancellationToken))
        {
            return Forbid();
        }

        note.Title = request.Title.Trim();
        note.Content = request.Content;
        note.UpdatedById = User.GetUserId();
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

        if (!await IsMember(note.TeamId, cancellationToken))
        {
            return Forbid();
        }

        dbContext.Notes.Remove(note);
        await dbContext.SaveChangesAsync(cancellationToken);
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
}
