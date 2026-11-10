using System.Globalization;
using System.Text.Json;
using Bluestaq.Notes.Api.Controllers;
using Bluestaq.Notes.Api.Data;
using Bluestaq.Notes.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace Bluestaq.Notes.Api.Administration;

/// <summary>Runs explicit out-of-band bootstrap, credential-recovery, and audit-maintenance commands.</summary>
public static class AdministrativeCommandRunner
{
    private static readonly HashSet<string> Commands =
        ["bootstrap", "recover-credential", "verify-audit", "prune-audit"];

    /// <summary>Returns an exit code for a recognized command, or null when normal HTTP hosting should continue.</summary>
    public static async Task<int?> TryRunAsync(
        string[] args,
        IServiceProvider services,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || !Commands.Contains(args[0]))
        {
            return null;
        }

        try
        {
            var arguments = ParseArguments(args[1..]);
            await using var scope = services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
            var integrity = scope.ServiceProvider.GetRequiredService<SecurityAuditIntegrity>();
            var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
            return args[0] switch
            {
                "bootstrap" => await Bootstrap(arguments, dbContext, integrity, timeProvider, output, cancellationToken),
                "recover-credential" => await RecoverCredential(
                    arguments, dbContext, integrity, timeProvider, output, cancellationToken),
                "verify-audit" => await VerifyAudit(dbContext, integrity, output, cancellationToken),
                "prune-audit" => await PruneAudit(
                    arguments, dbContext, integrity, timeProvider, output, cancellationToken),
                _ => 1,
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await error.WriteLineAsync(exception.Message);
            return 1;
        }
    }

    private static async Task<int> Bootstrap(
        IReadOnlyDictionary<string, string?> arguments,
        NotesDbContext dbContext,
        SecurityAuditIntegrity integrity,
        TimeProvider timeProvider,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var displayName = Required(arguments, "display-name");
        var keyName = Required(arguments, "key-name");
        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException("Bootstrap is allowed only when no users exist.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var user = new User { Id = Guid.NewGuid(), DisplayName = displayName.Trim() };
        var (credential, apiKey) = ApiKeysController.CreateCredential(user.Id, keyName, null, now);
        user.ApiKeys.Add(credential);
        dbContext.Users.Add(user);
        AddAdministrativeAudit(
            dbContext, integrity, now, "administration.bootstrap", SecurityAuditTargets.User, user.Id,
            new { user.DisplayName, CredentialId = credential.Id, credential.Name });
        await dbContext.SaveChangesAsync(cancellationToken);
        await output.WriteLineAsync(JsonSerializer.Serialize(new
        {
            UserId = user.Id,
            CredentialId = credential.Id,
            ApiKey = apiKey,
        }));
        return 0;
    }

    private static async Task<int> RecoverCredential(
        IReadOnlyDictionary<string, string?> arguments,
        NotesDbContext dbContext,
        SecurityAuditIntegrity integrity,
        TimeProvider timeProvider,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(Required(arguments, "user-id"));
        var keyName = Required(arguments, "key-name");
        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("The recovery user does not exist.");
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.ApiKeys
            .Where(key => key.UserId == userId && key.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(key => key.RevokedAt, now),
                cancellationToken);
        var updatedUsers = await dbContext.Users
            .Where(candidate => candidate.Id == userId && candidate.CredentialVersion == user.CredentialVersion)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    candidate => candidate.CredentialVersion,
                    candidate => candidate.CredentialVersion + 1),
                cancellationToken);
        if (updatedUsers != 1)
        {
            throw new DbUpdateConcurrencyException("The credential set changed during recovery.");
        }

        var (credential, apiKey) = ApiKeysController.CreateCredential(user.Id, keyName, null, now);
        dbContext.ApiKeys.Add(credential);
        AddAdministrativeAudit(
            dbContext, integrity, now, "administration.credential-recovered", SecurityAuditTargets.User, user.Id,
            new { CredentialId = credential.Id, credential.Name });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await output.WriteLineAsync(JsonSerializer.Serialize(new
        {
            UserId = user.Id,
            CredentialId = credential.Id,
            ApiKey = apiKey,
        }));
        return 0;
    }

    private static async Task<int> VerifyAudit(
        NotesDbContext dbContext,
        SecurityAuditIntegrity integrity,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var events = await dbContext.SecurityAuditEvents.AsNoTracking().ToArrayAsync(cancellationToken);
        var legacyCount = events.Count(auditEvent => auditEvent.IntegrityHash == "legacy");
        var invalid = events.Where(auditEvent => auditEvent.IntegrityHash != "legacy" && !integrity.Verify(auditEvent))
            .Select(auditEvent => auditEvent.Id)
            .ToArray();
        await output.WriteLineAsync(JsonSerializer.Serialize(new
        {
            Checked = events.Length,
            LegacyUnverifiable = legacyCount,
            Invalid = invalid,
        }));
        return invalid.Length == 0 ? 0 : 1;
    }

    private static async Task<int> PruneAudit(
        IReadOnlyDictionary<string, string?> arguments,
        NotesDbContext dbContext,
        SecurityAuditIntegrity integrity,
        TimeProvider timeProvider,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var before = DateTime.Parse(
            Required(arguments, "before"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        var count = await dbContext.SecurityAuditEvents.CountAsync(
            auditEvent => auditEvent.OccurredAt < before, cancellationToken);
        if (!arguments.ContainsKey("confirm"))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(new { Before = before, Count = count, Confirmed = false }));
            return 0;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER IF EXISTS \"SecurityAuditEvents_NoUpdate\"; " +
            "DROP TRIGGER IF EXISTS \"SecurityAuditEvents_NoDelete\";",
            cancellationToken);
        var deleted = await dbContext.SecurityAuditEvents
            .Where(auditEvent => auditEvent.OccurredAt < before)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER \"SecurityAuditEvents_NoUpdate\" BEFORE UPDATE ON \"SecurityAuditEvents\" " +
            "BEGIN SELECT RAISE(ABORT, 'Security audit events are append-only'); END; " +
            "CREATE TRIGGER \"SecurityAuditEvents_NoDelete\" BEFORE DELETE ON \"SecurityAuditEvents\" " +
            "BEGIN SELECT RAISE(ABORT, 'Security audit events are append-only'); END;",
            cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        AddAdministrativeAudit(
            dbContext, integrity, now, "administration.audit-pruned", "security-audit", Guid.Empty,
            new { Before = before, Deleted = deleted });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await output.WriteLineAsync(JsonSerializer.Serialize(new { Before = before, Deleted = deleted, Confirmed = true }));
        return 0;
    }

    private static void AddAdministrativeAudit(
        NotesDbContext dbContext,
        SecurityAuditIntegrity integrity,
        DateTime occurredAt,
        string action,
        string targetType,
        Guid targetId,
        object details)
    {
        var auditEvent = new SecurityAuditEvent
        {
            Id = Guid.NewGuid(),
            OccurredAt = occurredAt,
            ActorUserId = Guid.Empty,
            ActorCredentialId = Guid.Empty,
            Action = action,
            TeamId = null,
            TargetType = targetType,
            TargetId = targetId,
            TraceId = $"admin:{action}",
            Details = JsonSerializer.Serialize(details, JsonSerializerOptions.Web),
            IntegrityHash = "pending",
        };
        auditEvent.IntegrityHash = integrity.Compute(auditEvent);
        dbContext.SecurityAuditEvents.Add(auditEvent);
    }

    private static Dictionary<string, string?> ParseArguments(string[] args)
    {
        var parsed = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{args[index]}'.");
            }

            var key = args[index][2..];
            var value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++index]
                : null;
            if (!parsed.TryAdd(key, value))
            {
                throw new ArgumentException($"Argument '--{key}' was supplied more than once.");
            }
        }

        return parsed;
    }

    private static string Required(IReadOnlyDictionary<string, string?> arguments, string name) =>
        arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"--{name} is required.");
}
