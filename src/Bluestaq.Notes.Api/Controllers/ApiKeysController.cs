using Bluestaq.Notes.Api.Auth;
using Bluestaq.Notes.Api.Contracts;
using Bluestaq.Notes.Api.Data;
using Bluestaq.Notes.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bluestaq.Notes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/api-keys")]
public sealed class ApiKeysController(NotesDbContext dbContext, SecurityAudit securityAudit) : ControllerBase
{
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

    [HttpPost]
    public async Task<ActionResult<ApiKeyCreatedResponse>> Create(
        CreateApiKeyRequest request,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request.Name, request.ExpiresAt);
        if (validation is not null)
        {
            return validation;
        }

        var (credential, apiKey) = CreateCredential(User.GetUserId(), request.Name, request.ExpiresAt);
        dbContext.ApiKeys.Add(credential);
        securityAudit.Record(
            SecurityAuditActions.ApiKeyCreated,
            SecurityAuditTargets.ApiKey,
            credential.Id,
            details: new { credential.Name, credential.ExpiresAt });
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(List), ToCreatedResponse(credential, apiKey));
    }

    [HttpPost("{keyId:guid}/rotate")]
    public async Task<ActionResult<ApiKeyCreatedResponse>> Rotate(
        Guid keyId,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var oldCredential = await dbContext.ApiKeys.SingleOrDefaultAsync(
            key => key.Id == keyId && key.UserId == userId,
            cancellationToken);
        if (oldCredential is null)
        {
            return Problem("The API key does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        if (oldCredential.RevokedAt is not null)
        {
            return Problem("The API key is already revoked.", statusCode: StatusCodes.Status409Conflict);
        }

        var (replacement, apiKey) = CreateCredential(userId, oldCredential.Name, null);
        oldCredential.RevokedAt = DateTime.UtcNow;
        dbContext.ApiKeys.Add(replacement);
        securityAudit.Record(
            SecurityAuditActions.ApiKeyRotated,
            SecurityAuditTargets.ApiKey,
            replacement.Id,
            details: new { OldCredentialId = oldCredential.Id, replacement.Name, replacement.ExpiresAt });
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(List), ToCreatedResponse(replacement, apiKey));
    }

    [HttpDelete("{keyId:guid}")]
    public async Task<IActionResult> Revoke(Guid keyId, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var credential = await dbContext.ApiKeys.SingleOrDefaultAsync(
            key => key.Id == keyId && key.UserId == userId,
            cancellationToken);
        if (credential is null)
        {
            return Problem("The API key does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        if (credential.RevokedAt is not null)
        {
            return NoContent();
        }

        var now = DateTime.UtcNow;
        var otherUsableKeyExists = await dbContext.ApiKeys.AnyAsync(
            key => key.UserId == userId
                && key.Id != keyId
                && key.RevokedAt == null
                && (key.ExpiresAt == null || key.ExpiresAt > now),
            cancellationToken);
        if (!otherUsableKeyExists)
        {
            return Problem(
                "Create or rotate another API key before revoking the final usable key.",
                statusCode: StatusCodes.Status409Conflict);
        }

        credential.RevokedAt = now;
        securityAudit.Record(
            SecurityAuditActions.ApiKeyRevoked,
            SecurityAuditTargets.ApiKey,
            credential.Id,
            details: new { credential.Name });
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private ActionResult? Validate(string name, DateTime? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(nameof(name), "Name must contain non-whitespace characters.");
        }

        if (expiresAt <= DateTime.UtcNow)
        {
            ModelState.AddModelError(nameof(expiresAt), "ExpiresAt must be in the future.");
        }

        return ModelState.IsValid ? null : ValidationProblem(ModelState);
    }

    internal static (ApiKeyCredential Credential, string ApiKey) CreateCredential(
        Guid userId,
        string name,
        DateTime? expiresAt)
    {
        var apiKey = ApiKeyGenerator.Generate();
        return (new ApiKeyCredential
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            KeyHash = ApiKeyHasher.Hash(apiKey),
            Name = name.Trim(),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt?.ToUniversalTime(),
        }, apiKey);
    }

    internal static ApiKeyCreatedResponse ToCreatedResponse(ApiKeyCredential credential, string apiKey) =>
        new(credential.Id, credential.Name, apiKey, credential.CreatedAt, credential.ExpiresAt);
}
