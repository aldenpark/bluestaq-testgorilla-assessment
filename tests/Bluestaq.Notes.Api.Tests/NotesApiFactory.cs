using Bluestaq.Notes.Api.Data;
using Bluestaq.Notes.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bluestaq.Notes.Api.Tests;

public sealed class NotesApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabasePath { get; } =
        Path.Combine(Path.GetTempPath(), $"bluestaq-notes-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<NotesDbContext>>();
            services.AddDbContext<NotesDbContext>((provider, options) => options
                .UseSqlite($"Data Source={DatabasePath}")
                .AddInterceptors(provider.GetRequiredService<SecurityAuditTelemetryInterceptor>()));
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();

        foreach (var path in new[] { DatabasePath, $"{DatabasePath}-shm", $"{DatabasePath}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public NotesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<NotesDbContext>()
            .UseSqlite($"Data Source={DatabasePath}")
            .Options;
        return new NotesDbContext(options);
    }
}
