using Microsoft.EntityFrameworkCore;

namespace Bluestaq.Notes.Api.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
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
            ApiKeyHash = seed.ApiKeyHash,
        }));
        await dbContext.SaveChangesAsync();
    }

    private sealed record SeedUser(Guid Id, string DisplayName, string ApiKeyHash);
}
