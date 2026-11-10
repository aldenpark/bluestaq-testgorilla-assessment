using Microsoft.EntityFrameworkCore;

namespace Bluestaq.Notes.Api.Data;

/// <summary>Coordinates SQLite persistence, entity relationships, and audit-event immutability.</summary>
public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<ApiKeyCredential> ApiKeys => Set<ApiKeyCredential>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<NotePermission> NotePermissions => Set<NotePermission>();
    public DbSet<SecurityAuditEvent> SecurityAuditEvents => Set<SecurityAuditEvent>();
    public DbSet<ReadinessProbe> ReadinessProbes => Set<ReadinessProbe>();

    /// <summary>Saves tracked changes after rejecting any audit-event updates or deletions.</summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAuditEventsAreAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>Asynchronously saves tracked changes after rejecting audit-event updates or deletions.</summary>
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureAuditEventsAreAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Configures relational constraints, indexes, conversions, and entity relationships.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(user => user.DisplayName).HasMaxLength(100);
            entity.Property(user => user.CredentialVersion).IsConcurrencyToken();
        });

        modelBuilder.Entity<ApiKeyCredential>(entity =>
        {
            entity.Property(key => key.KeyHash).HasMaxLength(64).IsFixedLength();
            entity.Property(key => key.Name).HasMaxLength(100);
            entity.HasIndex(key => key.KeyHash).IsUnique();
            entity.HasIndex(key => key.UserId);
            entity.HasOne(key => key.User)
                .WithMany(user => user.ApiKeys)
                .HasForeignKey(key => key.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            ConfigureUtc(entity.Property(key => key.CreatedAt));
            ConfigureNullableUtc(entity.Property(key => key.ExpiresAt));
            ConfigureNullableUtc(entity.Property(key => key.RevokedAt));
            ConfigureNullableUtc(entity.Property(key => key.LastUsedAt));
        });

        modelBuilder.Entity<Team>(entity =>
        {
            entity.Property(team => team.Name).HasMaxLength(100);
            entity.Property(team => team.CreatedAt)
                .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            entity.Property(team => team.Version).IsConcurrencyToken();
            entity.HasOne(team => team.Owner)
                .WithMany()
                .HasForeignKey(team => team.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TeamMembership>(entity =>
        {
            entity.HasKey(membership => new { membership.TeamId, membership.UserId });
            entity.Property(membership => membership.JoinedAt)
                .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            entity.HasOne(membership => membership.Team)
                .WithMany(team => team.Memberships)
                .HasForeignKey(membership => membership.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(membership => membership.User)
                .WithMany(user => user.Memberships)
                .HasForeignKey(membership => membership.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(membership => membership.UserId);
        });

        modelBuilder.Entity<Note>(entity =>
        {
            entity.Property(note => note.Title).HasMaxLength(200);
            entity.Property(note => note.Content).HasMaxLength(10_000);
            entity.Property(note => note.Visibility).HasConversion<string>().HasMaxLength(20);
            entity.Property(note => note.Version).IsConcurrencyToken();
            entity.Property(note => note.CreatedAt)
                .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            entity.Property(note => note.UpdatedAt)
                .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            entity.HasIndex(note => new { note.TeamId, note.UpdatedAt });
            entity.HasOne(note => note.Team)
                .WithMany(team => team.Notes)
                .HasForeignKey(note => note.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(note => note.CreatedBy)
                .WithMany()
                .HasForeignKey(note => note.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(note => note.UpdatedBy)
                .WithMany()
                .HasForeignKey(note => note.UpdatedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NotePermission>(entity =>
        {
            entity.HasKey(permission => new { permission.NoteId, permission.UserId });
            entity.Property(permission => permission.Role).HasConversion<string>().HasMaxLength(10);
            entity.HasOne(permission => permission.Note)
                .WithMany(note => note.Permissions)
                .HasForeignKey(permission => permission.NoteId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(permission => permission.User)
                .WithMany()
                .HasForeignKey(permission => permission.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(permission => permission.UserId);
        });

        modelBuilder.Entity<SecurityAuditEvent>(entity =>
        {
            ConfigureUtc(entity.Property(auditEvent => auditEvent.OccurredAt));
            entity.Property(auditEvent => auditEvent.Action).HasMaxLength(100);
            entity.Property(auditEvent => auditEvent.TargetType).HasMaxLength(50);
            entity.Property(auditEvent => auditEvent.TraceId).HasMaxLength(100);
            entity.Property(auditEvent => auditEvent.Details).HasMaxLength(4_000);
            entity.Property(auditEvent => auditEvent.IntegrityHash).HasMaxLength(64).IsFixedLength();
            entity.HasIndex(auditEvent => new { auditEvent.OccurredAt, auditEvent.Id });
            entity.HasIndex(auditEvent => new { auditEvent.ActorUserId, auditEvent.OccurredAt });
            entity.HasIndex(auditEvent => new { auditEvent.TeamId, auditEvent.OccurredAt });
            entity.HasIndex(auditEvent => auditEvent.TargetId);
        });

        modelBuilder.Entity<ReadinessProbe>(entity =>
        {
            entity.HasKey(probe => probe.Id);
            ConfigureUtc(entity.Property(probe => probe.CheckedAt));
            entity.HasData(new ReadinessProbe
            {
                Id = 1,
                CheckedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
        });
    }

    /// <summary>Throws when a tracked security audit event would be updated or deleted.</summary>
    private void EnsureAuditEventsAreAppendOnly()
    {
        if (ChangeTracker.Entries<SecurityAuditEvent>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Security audit events are append-only.");
        }
    }

    /// <summary>Configures a required timestamp to be materialized with UTC kind information.</summary>
    private static void ConfigureUtc(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<DateTime> property) =>
        property.HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    /// <summary>Configures an optional timestamp to be materialized with UTC kind information.</summary>
    private static void ConfigureNullableUtc(
        Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<DateTime?> property) =>
        property.HasConversion(
            value => value,
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);
}
