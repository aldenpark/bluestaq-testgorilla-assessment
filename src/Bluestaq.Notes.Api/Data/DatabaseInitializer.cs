using Microsoft.EntityFrameworkCore;

namespace Bluestaq.Notes.Api.Data;

/// <summary>Applies database migrations and creates configured development users when the database is empty.</summary>
public static class DatabaseInitializer
{
    /// <summary>Migrates the database and inserts development fixtures when configured and required.</summary>
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        await dbContext.Database.MigrateAsync();

        var seedUsers = configuration.GetSection("DevelopmentSeed:Users").Get<SeedUser[]>() ?? [];
        if (seedUsers.Length == 0 || await dbContext.Users.AnyAsync())
        {
            return;
        }

        dbContext.Users.AddRange(seedUsers.Select(seed => new User
        {
            Id = seed.Id,
            DisplayName = seed.DisplayName,
            ApiKeys =
            [
                new ApiKeyCredential
                {
                    Id = Guid.NewGuid(),
                    KeyHash = seed.ApiKeyHash,
                    Name = "Development",
                    CreatedAt = now,
                },
            ],
        }));
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Describes one development user loaded from configuration.</summary>
    private sealed record SeedUser(Guid Id, string DisplayName, string ApiKeyHash);
}
