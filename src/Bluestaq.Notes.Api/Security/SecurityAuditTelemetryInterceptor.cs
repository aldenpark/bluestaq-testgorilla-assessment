using System.Diagnostics.Metrics;
using Bluestaq.Notes.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bluestaq.Notes.Api.Security;

/// <summary>Emits structured logs and a counter only after security audit events commit successfully.</summary>
public sealed class SecurityAuditTelemetryInterceptor(
    ILogger<SecurityAuditTelemetryInterceptor> logger) : SaveChangesInterceptor
{
    private static readonly Meter Meter = new("Bluestaq.Notes.Api.Security");
    private static readonly Counter<long> CommittedEvents = Meter.CreateCounter<long>(
        "bluestaq.security.events.committed",
        description: "Number of security audit events committed to durable storage.");
    private IReadOnlyCollection<SecurityAuditEvent> pendingEvents = [];

    /// <summary>Captures pending security events before a synchronous save begins.</summary>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        CapturePendingEvents(eventData.Context);
        return result;
    }

    /// <summary>Captures pending security events before an asynchronous save begins.</summary>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CapturePendingEvents(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <summary>Emits telemetry after a synchronous save commits.</summary>
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        EmitCommittedEvents();
        return result;
    }

    /// <summary>Emits telemetry after an asynchronous save commits.</summary>
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        EmitCommittedEvents();
        return ValueTask.FromResult(result);
    }

    /// <summary>Discards pending telemetry when a save fails.</summary>
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => pendingEvents = [];

    /// <summary>Discards pending telemetry when an asynchronous save fails.</summary>
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        pendingEvents = [];
        return Task.CompletedTask;
    }

    /// <summary>Snapshots added audit rows so only successful commits produce external telemetry.</summary>
    private void CapturePendingEvents(DbContext? context) =>
        pendingEvents = context?.ChangeTracker.Entries<SecurityAuditEvent>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray() ?? [];

    /// <summary>Writes structured records and metric measurements for committed audit rows.</summary>
    private void EmitCommittedEvents()
    {
        foreach (var auditEvent in pendingEvents)
        {
            logger.LogWarning(
                new EventId(2001, "SecurityEventCommitted"),
                "Security event committed: {Action} by user {ActorUserId} using credential {ActorCredentialId} for {TargetType} {TargetId} in team {TeamId} with trace {TraceId}.",
                auditEvent.Action,
                auditEvent.ActorUserId,
                auditEvent.ActorCredentialId,
                auditEvent.TargetType,
                auditEvent.TargetId,
                auditEvent.TeamId,
                auditEvent.TraceId);
            CommittedEvents.Add(
                1,
                new KeyValuePair<string, object?>("action", auditEvent.Action),
                new KeyValuePair<string, object?>("target.type", auditEvent.TargetType));
        }

        pendingEvents = [];
    }
}
