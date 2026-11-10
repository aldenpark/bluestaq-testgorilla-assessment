using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bluestaq.Notes.Api.Data;

/// <summary>Reports whether the database is reachable, current, and rollback-safely writable.</summary>
public sealed class DatabaseHealthCheck(IServiceScopeFactory scopeFactory, TimeProvider timeProvider) : IHealthCheck
{
    /// <summary>Checks connectivity, migration currency, and a rolled-back sentinel update.</summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("Database connection failed.");
            }

            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
            if (pendingMigrations.Any())
            {
                return HealthCheckResult.Unhealthy("Database migrations are pending.");
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var probe = await dbContext.ReadinessProbes.SingleAsync(candidate => candidate.Id == 1, cancellationToken);
            probe.CheckedAt = timeProvider.GetUtcNow().UtcDateTime;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
            return HealthCheckResult.Healthy("Database is reachable, current, and writable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Database readiness check failed.", exception);
        }
    }
}
