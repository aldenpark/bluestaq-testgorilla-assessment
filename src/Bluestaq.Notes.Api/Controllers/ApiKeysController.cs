using Bluestaq.Notes.Api.Auth;
using Bluestaq.Notes.Api.Contracts;
using Bluestaq.Notes.Api.Data;
using Bluestaq.Notes.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bluestaq.Notes.Api.Controllers;

/// <summary>Manages the authenticated user's API-key credentials.</summary>
[ApiController]
[Authorize]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[Route("api/api-keys")]
[Route("api/v1/api-keys")]
public sealed class ApiKeysController(
    NotesDbContext dbContext,
    SecurityAudit securityAudit,
    TimeProvider timeProvider) : ControllerBase
{
    /// <summary>Lists metadata for every API key owned by the authenticated user.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<ApiKeyResponse>>> List(
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var keys = await dbContext.ApiKeys
            .AsNoTracking()
            .Where(key => key.UserId == userId)
            .OrderByDescending(key => key.CreatedAt)
            .Select(key => new ApiKeyResponse(
                key.Id,
                key.Name,
                key.CreatedAt,
                key.ExpiresAt,
                key.RevokedAt,
                key.LastUsedAt))
            .ToArrayAsync(cancellationToken);

        return Ok(keys);
    }

    /// <summary>Creates a named API key and returns its secret value once.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiKeyCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiKeyCreatedResponse>> Create(
        CreateApiKeyRequest request,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request.Name, request.ExpiresAt);
        if (validation is not null)
        {
            return validation;
        }

        var user = await dbContext.Users.SingleAsync(
            candidate => candidate.Id == User.GetUserId(),
            cancellationToken);
        var (credential, apiKey) = CreateCredential(
            user.Id,
            request.Name,
            request.ExpiresAt,
            timeProvider.GetUtcNow().UtcDateTime);
        user.CredentialVersion++;
        dbContext.ApiKeys.Add(credential);
        securityAudit.Record(
            SecurityAuditActions.ApiKeyCreated,
            SecurityAuditTargets.ApiKey,
            credential.Id,
            details: new { credential.Name, credential.ExpiresAt });
        if (!await TrySaveCredentialMutation(cancellationToken))
        {
            return Problem(
                "The credential set changed concurrently. Retry with current credential state.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return CreatedAtAction(nameof(List), ToCreatedResponse(credential, apiKey));
    }

    /// <summary>Revokes an existing API key and returns a replacement secret.</summary>
    [HttpPost("{keyId:guid}/rotate")]
    [ProducesResponseType(typeof(ApiKeyCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiKeyCreatedResponse>> Rotate(
        Guid keyId,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var user = await dbContext.Users
            .Include(candidate => candidate.ApiKeys)
            .SingleAsync(candidate => candidate.Id == userId, cancellationToken);
        var oldCredential = user.ApiKeys.SingleOrDefault(key => key.Id == keyId);
        if (oldCredential is null)
        {
            return Problem("The API key does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        if (oldCredential.RevokedAt is not null)
        {
            return Problem("The API key is already revoked.", statusCode: StatusCodes.Status409Conflict);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var (replacement, apiKey) = CreateCredential(
            userId,
            oldCredential.Name,
            oldCredential.ExpiresAt,
            now);
        oldCredential.RevokedAt = now;
        user.CredentialVersion++;
        dbContext.ApiKeys.Add(replacement);
        securityAudit.Record(
            SecurityAuditActions.ApiKeyRotated,
            SecurityAuditTargets.ApiKey,
            replacement.Id,
            details: new { OldCredentialId = oldCredential.Id, replacement.Name, replacement.ExpiresAt });
        if (!await TrySaveCredentialMutation(cancellationToken))
        {
            return Problem(
                "The credential set changed concurrently. Retry with current credential state.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return CreatedAtAction(nameof(List), ToCreatedResponse(replacement, apiKey));
    }

    /// <summary>Revokes an API key while preventing removal of the user's final usable credential.</summary>
    [HttpDelete("{keyId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Revoke(Guid keyId, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var user = await dbContext.Users
            .Include(candidate => candidate.ApiKeys)
            .SingleAsync(candidate => candidate.Id == userId, cancellationToken);
        var credential = user.ApiKeys.SingleOrDefault(key => key.Id == keyId);
        if (credential is null)
        {
            return Problem("The API key does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        if (credential.RevokedAt is not null)
        {
            return NoContent();
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (!CredentialPolicy.CanRevoke(keyId, user.ApiKeys, now))
        {
            return Problem(
                "Create or rotate another API key before revoking the final usable key.",
                statusCode: StatusCodes.Status409Conflict);
        }

        credential.RevokedAt = now;
        user.CredentialVersion++;
        securityAudit.Record(
            SecurityAuditActions.ApiKeyRevoked,
            SecurityAuditTargets.ApiKey,
            credential.Id,
            details: new { credential.Name });
        if (!await TrySaveCredentialMutation(cancellationToken))
        {
            return Problem(
                "The credential set changed concurrently. Retry with current credential state.",
                statusCode: StatusCodes.Status409Conflict);
        }
        return NoContent();
    }

    /// <summary>Validates a credential name and optional expiration against API-key rules.</summary>
    private ActionResult? Validate(string name, DateTime? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(nameof(name), "Name must contain non-whitespace characters.");
        }

        if (expiresAt <= timeProvider.GetUtcNow().UtcDateTime)
        {
            ModelState.AddModelError(nameof(expiresAt), "ExpiresAt must be in the future.");
        }

        return ModelState.IsValid ? null : ValidationProblem(ModelState);
    }

    /// <summary>Creates a credential entity and its corresponding one-time plaintext API key.</summary>
    internal static (ApiKeyCredential Credential, string ApiKey) CreateCredential(
        Guid userId,
        string name,
        DateTime? expiresAt,
        DateTime now)
    {
        var apiKey = ApiKeyGenerator.Generate();
        return (new ApiKeyCredential
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            KeyHash = ApiKeyHasher.Hash(apiKey),
            Name = name.Trim(),
            CreatedAt = now,
            ExpiresAt = expiresAt?.ToUniversalTime(),
        }, apiKey);
    }

    /// <summary>Maps a new credential and plaintext secret to the creation response contract.</summary>
    internal static ApiKeyCreatedResponse ToCreatedResponse(ApiKeyCredential credential, string apiKey) =>
        new(credential.Id, credential.Name, apiKey, credential.CreatedAt, credential.ExpiresAt);

    /// <summary>Saves a serialized credential mutation and reports optimistic-concurrency conflicts.</summary>
    private async Task<bool> TrySaveCredentialMutation(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
