using Microsoft.EntityFrameworkCore;

namespace Bluestaq.Notes.Api.Data;

public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(user => user.DisplayName).HasMaxLength(100);
            entity.Property(user => user.ApiKeyHash).HasMaxLength(64).IsFixedLength();
            entity.HasIndex(user => user.ApiKeyHash).IsUnique();
        });

        modelBuilder.Entity<Team>(entity =>
        {
            entity.Property(team => team.Name).HasMaxLength(100);
            entity.Property(team => team.CreatedAt)
                .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
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
    }
}
