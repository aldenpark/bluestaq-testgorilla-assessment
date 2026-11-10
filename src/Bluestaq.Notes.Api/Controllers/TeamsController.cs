using Bluestaq.Notes.Api.Auth;
using Bluestaq.Notes.Api.Contracts;
using Bluestaq.Notes.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bluestaq.Notes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/teams")]
public sealed class TeamsController(NotesDbContext dbContext) : ControllerBase
{
    private const int SqliteConstraintPrimaryKey = 1555;

    [HttpPost]
    public async Task<ActionResult<TeamResponse>> Create(
        CreateTeamRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            ModelState.AddModelError(nameof(request.Name), "Name must contain non-whitespace characters.");
            return ValidationProblem(ModelState);
        }

        var userId = User.GetUserId();
        var now = DateTime.UtcNow;
        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            OwnerId = userId,
            CreatedAt = now,
            Memberships =
            [
                new TeamMembership { UserId = userId, JoinedAt = now },
            ],
        };

        dbContext.Teams.Add(team);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { teamId = team.Id }, await BuildResponse(team.Id, cancellationToken));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<TeamResponse>>> List(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var teamIds = await dbContext.TeamMemberships
            .Where(membership => membership.UserId == userId)
            .OrderBy(membership => membership.Team.Name)
            .Select(membership => membership.TeamId)
            .ToListAsync(cancellationToken);

        var teams = new List<TeamResponse>(teamIds.Count);
        foreach (var teamId in teamIds)
        {
            teams.Add((await BuildResponse(teamId, cancellationToken))!);
        }

        return Ok(teams);
    }

    [HttpGet("{teamId:guid}")]
    public async Task<ActionResult<TeamResponse>> Get(Guid teamId, CancellationToken cancellationToken)
    {
        var accessResult = await CheckMembership(teamId, User.GetUserId(), cancellationToken);
        if (accessResult is not null)
        {
            return accessResult;
        }

        return Ok(await BuildResponse(teamId, cancellationToken));
    }

    [HttpPost("{teamId:guid}/members")]
    public async Task<ActionResult<TeamMemberResponse>> AddMember(
        Guid teamId,
        AddTeamMemberRequest request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(request.UserId), "UserId must not be empty.");
            return ValidationProblem(ModelState);
        }

        var ownerResult = await CheckOwner(teamId, User.GetUserId(), cancellationToken);
        if (ownerResult is not null)
        {
            return ownerResult;
        }

        var user = await dbContext.Users.FindAsync([request.UserId], cancellationToken);
        if (user is null)
        {
            return Problem("The user does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        if (await dbContext.TeamMemberships.AnyAsync(
                membership => membership.TeamId == teamId && membership.UserId == request.UserId,
                cancellationToken))
        {
            return Problem("The user is already a team member.", statusCode: StatusCodes.Status409Conflict);
        }

        var membership = new TeamMembership
        {
            TeamId = teamId,
            UserId = request.UserId,
            JoinedAt = DateTime.UtcNow,
        };
        dbContext.TeamMemberships.Add(membership);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqliteException
            { SqliteExtendedErrorCode: SqliteConstraintPrimaryKey })
        {
            return Problem("The user is already a team member.", statusCode: StatusCodes.Status409Conflict);
        }

        return CreatedAtAction(
            nameof(Get),
            new { teamId },
            new TeamMemberResponse(user.Id, user.DisplayName, membership.JoinedAt));
    }

    [HttpDelete("{teamId:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(
        Guid teamId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var ownerResult = await CheckOwner(teamId, User.GetUserId(), cancellationToken);
        if (ownerResult is not null)
        {
            return ownerResult;
        }

        var team = await dbContext.Teams.AsNoTracking().SingleAsync(team => team.Id == teamId, cancellationToken);
        if (team.OwnerId == userId)
        {
            return Problem("The team owner cannot be removed.", statusCode: StatusCodes.Status409Conflict);
        }

        var membership = await dbContext.TeamMemberships.FindAsync([teamId, userId], cancellationToken);
        if (membership is null)
        {
            return Problem("The team membership does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        dbContext.TeamMemberships.Remove(membership);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<ActionResult?> CheckMembership(Guid teamId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Teams.AnyAsync(team => team.Id == teamId, cancellationToken))
        {
            return Problem("The team does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        return await dbContext.TeamMemberships.AnyAsync(
            membership => membership.TeamId == teamId && membership.UserId == userId,
            cancellationToken)
            ? null
            : Forbid();
    }

    private async Task<ActionResult?> CheckOwner(Guid teamId, Guid userId, CancellationToken cancellationToken)
    {
        var ownerId = await dbContext.Teams
            .Where(team => team.Id == teamId)
            .Select(team => (Guid?)team.OwnerId)
            .SingleOrDefaultAsync(cancellationToken);

        return ownerId switch
        {
            null => Problem("The team does not exist.", statusCode: StatusCodes.Status404NotFound),
            var id when id != userId => Forbid(),
            _ => null,
        };
    }

    private async Task<TeamResponse?> BuildResponse(Guid teamId, CancellationToken cancellationToken) =>
        await dbContext.Teams
            .AsNoTracking()
            .Where(team => team.Id == teamId)
            .Select(team => new TeamResponse(
                team.Id,
                team.Name,
                team.OwnerId,
                team.CreatedAt,
                team.Memberships
                    .OrderBy(membership => membership.User.DisplayName)
                    .Select(membership => new TeamMemberResponse(
                        membership.UserId,
                        membership.User.DisplayName,
                        membership.JoinedAt))
                    .ToArray()))
            .SingleOrDefaultAsync(cancellationToken);
}
